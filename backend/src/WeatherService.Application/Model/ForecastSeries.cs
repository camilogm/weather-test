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
    DateTimeOffset RetrievedAt)
{
    /// <summary>
    /// The next <paramref name="days"/> entries that have not already happened,
    /// as of <paramref name="asOf"/>.
    ///
    /// Taking the first N entries instead would be correct only for a series
    /// fetched moments ago. A stored one opens on the day it was recorded, so
    /// counting from index zero replays days that are over and calls them a
    /// forecast — precisely the failure the degradation chain exists to avoid.
    ///
    /// The cutoff is deliberately one day looser than "today". Open-Meteo dates
    /// are local to the forecast location while this clock is UTC, and a
    /// location far enough west runs a whole calendar day behind it — so the
    /// entry matching yesterday's UTC date may be that location's today.
    /// Keeping a finished day costs one card in the interface; dropping a live
    /// one costs the day people actually opened the page to see. Pinning this
    /// exactly would mean resolving each location's timezone, which is a
    /// network call and a cache of its own to save a single card.
    /// </summary>
    public ForecastSeries Upcoming(int days, DateTimeOffset asOf)
    {
        var earliest = DateOnly.FromDateTime(asOf.UtcDateTime).AddDays(-1);

        return this with
        {
            Days = [.. Days.Where(day => day.Date >= earliest).Take(days)],
        };
    }
}
