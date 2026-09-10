using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WeatherService.Api.Contracts;
using WeatherService.Api.RateLimiting;
using WeatherService.Application.Current;
using WeatherService.Application.Forecast;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Api.Controllers;

[ApiController]
[Route("weather")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Weather)]
public sealed class WeatherController : ControllerBase
{
    /// <summary>What the front end asks for when it asks for nothing.</summary>
    private const string DefaultCity = "San Salvador";

    private readonly IForecastService _forecasts;
    private readonly ICurrentWeatherService _current;
    private readonly ILocationResolver _locations;

    public WeatherController(
        IForecastService forecasts,
        ICurrentWeatherService current,
        ILocationResolver locations)
    {
        _forecasts = forecasts;
        _current = current;
        _locations = locations;
    }

    /// <summary>Current conditions for a city or an explicit coordinate pair.</summary>
    [HttpGet("current")]
    [ProducesResponseType<CurrentWeatherResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CurrentWeatherResponse>> GetCurrent(
        [FromQuery] WeatherQuery query,
        CancellationToken cancellationToken)
    {
        var location = await ResolveAsync(query, cancellationToken);
        var result = await _current.GetCurrentWeatherAsync(location, cancellationToken);

        var provenance = Declare(result.Source, result.IsDegraded, result.Weather.ObservedAt);

        return Ok(
            new CurrentWeatherResponse(
                result.Weather.Location.ToResponse(),
                provenance,
                result.Weather.TemperatureC,
                result.Weather.ApparentTemperatureC,
                result.Weather.WindSpeedKph,
                result.Weather.RelativeHumidityPercent,
                result.Weather.Condition.ToString(),
                result.Weather.Condition.ToIcon(),
                result.Weather.ObservedAt));
    }

    /// <summary>The next seven days for a city or an explicit coordinate pair.</summary>
    [HttpGet("forecast")]
    [ProducesResponseType<ForecastResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ForecastResponse>> GetForecast(
        [FromQuery] WeatherQuery query,
        CancellationToken cancellationToken)
    {
        var location = await ResolveAsync(query, cancellationToken);
        var result = await _forecasts.GetForecastAsync(location, cancellationToken);

        var provenance = Declare(result.Source, result.IsDegraded, result.Forecast.RetrievedAt);

        return Ok(
            new ForecastResponse(
                result.Forecast.Location.ToResponse(),
                provenance,
                [.. result.Forecast.Days.Select(day => day.ToResponse())]));
    }

    /// <summary>
    /// Explicit coordinates win when both are supplied; otherwise the city name
    /// is geocoded, defaulting to the city this service exists for.
    /// </summary>
    private async Task<GeoLocation> ResolveAsync(WeatherQuery query, CancellationToken cancellationToken)
    {
        if (query.Latitude.HasValue && query.Longitude.HasValue)
        {
            return new GeoLocation(
                string.IsNullOrWhiteSpace(query.City) ? "Custom location" : query.City.Trim(),
                query.Latitude.Value,
                query.Longitude.Value);
        }

        var city = string.IsNullOrWhiteSpace(query.City) ? DefaultCity : query.City.Trim();
        return await _locations.ResolveAsync(city, cancellationToken);
    }

    /// <summary>
    /// Announces where the data came from, in headers as well as the body.
    /// Degraded answers are also marked no-store so a proxy cannot pin a stale
    /// reading in front of a service that has since recovered.
    /// </summary>
    private ProvenanceResponse Declare(WeatherDataSource source, bool degraded, DateTimeOffset retrievedAt)
    {
        Response.Headers["X-Weather-Data-Source"] = source.ToString();
        Response.Headers["X-Weather-Degraded"] = degraded ? "true" : "false";
        Response.Headers["X-Weather-Retrieved-At"] = retrievedAt.ToString("o");

        if (degraded)
        {
            Response.Headers.CacheControl = "no-store";
        }

        return new ProvenanceResponse(source.ToString(), degraded, retrievedAt);
    }
}
