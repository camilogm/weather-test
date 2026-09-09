using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WeatherService.Api.Contracts;
using WeatherService.Api.RateLimiting;
using WeatherService.Application.Ports;

namespace WeatherService.Api.Controllers;

/// <summary>
/// Place lookup for the interface's city picker.
///
/// This exists so the front end never talks to the geocoding provider directly.
/// Going straight there from the browser would bypass the circuit breaker, the
/// cache and the error handling this service already owns — and would put a
/// third-party host in the page's critical path.
/// </summary>
[ApiController]
[Route("locations")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Search)]
public sealed class LocationsController : ControllerBase
{
    private readonly ILocationResolver _locations;

    public LocationsController(ILocationResolver locations) => _locations = locations;

    /// <summary>Candidate places for a partial name.</summary>
    [HttpGet]
    [ProducesResponseType<LocationSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<LocationSearchResponse>> Search(
        [FromQuery] LocationSearchQuery search,
        CancellationToken cancellationToken)
    {
        var matches = await _locations.SearchAsync(search.Query, search.Limit, cancellationToken);

        // Suggestions are worth re-reading from a cache rather than re-fetching:
        // people retype the same prefixes constantly.
        Response.Headers.CacheControl = "public, max-age=3600";

        return Ok(new LocationSearchResponse([.. matches.Select(match => match.ToResponse())]));
    }
}
