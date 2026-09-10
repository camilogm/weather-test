namespace WeatherService.Application.Model;

/// <summary>
/// A run of consecutive daily forecasts for one location, plus the instant it
/// was obtained. Always the service's whole horizon as fetched and stored;
/// callers that want less take a projection of it.
///
/// <see cref="RetrievedAt"/> is what lets a caller reason about staleness when
/// the response is served from a degraded source — and, once a series can
/// outlive a few of its own days, about which of them are still ahead.
/// </summary>
public sealed record ForecastSeries(
    GeoLocation Location,
    IReadOnlyList<DailyForecast> Days,
    DateTimeOffset RetrievedAt);
