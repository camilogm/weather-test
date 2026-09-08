namespace WeatherService.Application.Forecast;

/// <summary>
/// Where the answer actually came from.
///
/// This is not diagnostics trivia: it is surfaced to the client as a response
/// header so a consumer can tell a live reading from a degraded one. A silent
/// fallback is a lie of omission.
/// </summary>
public enum ForecastSource
{
    /// <summary>Fresh from the external provider.</summary>
    Provider,

    /// <summary>Served from cache while still inside its freshness window.</summary>
    Cache,

    /// <summary>Cache entry past its freshness window, served because the provider failed.</summary>
    StaleCache,

    /// <summary>Last resort: reconstructed from data previously persisted in the database.</summary>
    Historical,
}
