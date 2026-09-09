namespace WeatherService.Api.RateLimiting;

/// <summary>
/// How much traffic an anonymous caller may put through the endpoints that
/// proxy Open-Meteo.
///
/// The two budgets are separate because the traffic shapes are: a forecast is
/// one call per city a person chooses, while the picker's search fires on every
/// debounced keystroke. One number sized for search leaves the expensive
/// endpoint wide open; one sized for forecasts throttles ordinary typing.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Off is a deliberate switch, not a default. Integration tests that are not
    /// about throttling turn it off so they are not counting requests, and a
    /// developer poking at the API locally has no reason to fight it.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Requests per window, per caller, for the weather endpoints.</summary>
    public int WeatherPermits { get; set; } = 60;

    /// <summary>Requests per window, per caller, for location search.</summary>
    public int SearchPermits { get; set; } = 120;
}

/// <summary>The policy names the controllers refer to.</summary>
public static class RateLimitPolicies
{
    public const string Weather = "weather";

    public const string Search = "search";
}
