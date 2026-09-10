namespace WeatherService.Application.Model;

/// <summary>
/// A whole week of forecast for one location, plus the instant it was obtained.
/// <see cref="RetrievedAt"/> is what lets a caller reason about staleness when
/// the response is served from a degraded source.
/// </summary>
public sealed record ForecastSeries(
    GeoLocation Location,
    IReadOnlyList<DailyForecast> Days,
    DateTimeOffset RetrievedAt);
