using System.ComponentModel.DataAnnotations;

namespace WeatherService.Infrastructure.Configuration;

/// <summary>Everything about talking to the external weather service.</summary>
public sealed class WeatherProviderOptions
{
    public const string SectionName = "WeatherProvider";

    /// <summary>
    /// Open-Meteo needs no API key, which is the reason it was chosen: the
    /// service can be cloned and run without registering anywhere.
    /// </summary>
    [Required]
    public Uri BaseAddress { get; set; } = new("https://api.open-meteo.com/");

    /// <summary>Geocoding lives on a different host, so it needs its own client.</summary>
    [Required]
    public Uri GeocodingBaseAddress { get; set; } = new("https://geocoding-api.open-meteo.com/");

    public ResilienceSettings Resilience { get; set; } = new();
}

/// <summary>
/// The resilience pipeline, spelled out rather than hidden behind
/// <c>AddStandardResilienceHandler()</c>.
///
/// The standard handler would give roughly these strategies in one line, but it
/// would also make every number below invisible — and these numbers ARE the
/// design. A breaker that trips too eagerly turns a blip into an outage; one
/// that trips too late never protects anything.
/// </summary>
public sealed class ResilienceSettings
{
    /// <summary>
    /// Ceiling for a single attempt, retries excluded.
    ///
    /// Every attempt has to fit inside <see cref="TotalTimeout"/> alongside the
    /// backoff between them, or the last one is cut short every single time —
    /// latency spent on a request that cannot finish, and one the breaker never
    /// gets to count. ResilienceSettingsTests holds that sum to the budget.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:01:00")]
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Ceiling for the whole operation, retries included.
    ///
    /// Kept deliberately short. This service degrades to a fresh cache, a stale
    /// cache and then stored history, so there is rarely nothing to serve —
    /// which makes every second spent retrying a second stolen from an answer
    /// already in memory. A long budget is for a caller with no plan B.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.100", "00:05:00")]
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>Zero disables retrying entirely.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 2;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Share of failed calls inside the sampling window that trips the breaker.</summary>
    [Range(0.1, 1.0)]
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Calls required in the window before the ratio is even considered.
    /// Without this, a single failure during a quiet minute is a 100% failure
    /// rate and would open the circuit on no evidence at all.
    ///
    /// It is a gate, so it has to be reachable: the attempts a hung upstream
    /// produces inside <see cref="SamplingDuration"/> must clear it, or the
    /// ratio is never evaluated and the breaker never opens.
    /// </summary>
    [Range(2, 1000)]
    public int MinimumThroughput { get; set; } = 5;

    /// <summary>Rolling window the failure ratio is measured over.</summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the circuit stays open before letting a single trial call
    /// through to see whether the upstream came back.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);
}
