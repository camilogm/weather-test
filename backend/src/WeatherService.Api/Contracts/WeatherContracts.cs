using System.ComponentModel.DataAnnotations;
using WeatherService.Application.Forecast;
using WeatherService.Application.Model;

namespace WeatherService.Api.Contracts;

/// <summary>
/// Query accepted by both endpoints: a city name, or explicit coordinates for
/// the "geographic region" case. Defaults to San Salvador, which is what the
/// front end asks for.
/// </summary>
public sealed class WeatherQuery : IValidatableObject
{
    public string? City { get; init; }

    [Range(-90, 90)]
    public double? Latitude { get; init; }

    [Range(-180, 180)]
    public double? Longitude { get; init; }

    /// <summary>
    /// How many days of forecast to return, from today. Omitted means
    /// <see cref="ForecastHorizon.DefaultDays"/>, which is what this endpoint
    /// answered before the range existed — so no client that never heard of it
    /// sees its response change.
    ///
    /// Out of range is a 400 rather than a silent clamp: a caller that asked for
    /// thirty days and got sixteen without being told would have no way to know
    /// its request was not honoured. Only <c>forecast</c> reads it; on
    /// <c>current</c> it is simply ignored, the way an unknown parameter is.
    /// </summary>
    [Range(ForecastHorizon.MinimumDays, ForecastHorizon.MaximumDays)]
    public int? Days { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Latitude.HasValue ^ Longitude.HasValue)
        {
            yield return new ValidationResult(
                "Latitude and longitude must be supplied together.",
                [nameof(Latitude), nameof(Longitude)]);
        }
    }
}

public sealed record LocationResponse(string Name, double Latitude, double Longitude);

/// <summary>
/// Provenance travels in the body as well as the headers, so a client that only
/// reads JSON can still tell a live reading from a degraded one.
/// </summary>
public sealed record ProvenanceResponse(string Source, bool Degraded, DateTimeOffset RetrievedAt);

public sealed record DailyForecastResponse(
    DateOnly Date,
    double MinTemperatureC,
    double MaxTemperatureC,
    string Condition,
    string Icon);

public sealed record ForecastResponse(
    LocationResponse Location,
    ProvenanceResponse Provenance,
    IReadOnlyList<DailyForecastResponse> Days);

public sealed record CurrentWeatherResponse(
    LocationResponse Location,
    ProvenanceResponse Provenance,
    double TemperatureC,
    double ApparentTemperatureC,
    double WindSpeedKph,
    int RelativeHumidityPercent,
    string Condition,
    string Icon,
    DateTimeOffset ObservedAt);

/// <summary>
/// Maps domain objects onto the wire.
///
/// The extra step is on purpose: without it, renaming a domain property would
/// silently break every client, and the API contract would be whatever the
/// domain happened to look like that week.
/// </summary>
public static class WeatherContractMapper
{
    public static LocationResponse ToResponse(this GeoLocation location) =>
        new(location.Name, location.Latitude, location.Longitude);

    public static DailyForecastResponse ToResponse(this DailyForecast day) =>
        new(
            day.Date,
            day.MinTemperatureC,
            day.MaxTemperatureC,
            day.Condition.ToString(),
            day.Condition.ToIcon());

    /// <summary>
    /// A stable slug the front end can map to whatever artwork it likes.
    /// Open-Meteo ships no icons, so the API hands over a vocabulary rather than
    /// letting each client re-derive one from raw WMO codes.
    /// </summary>
    public static string ToIcon(this WeatherCondition condition) =>
        condition switch
        {
            WeatherCondition.Clear => "clear",
            WeatherCondition.MainlyClear => "mostly-clear",
            WeatherCondition.PartlyCloudy => "partly-cloudy",
            WeatherCondition.Overcast => "overcast",
            WeatherCondition.Fog => "fog",
            WeatherCondition.Drizzle => "drizzle",
            WeatherCondition.Rain or WeatherCondition.RainShowers => "rain",
            WeatherCondition.FreezingRain => "freezing-rain",
            WeatherCondition.Snow or WeatherCondition.SnowShowers => "snow",
            WeatherCondition.Thunderstorm => "thunderstorm",
            _ => "unknown",
        };
}
