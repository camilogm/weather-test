namespace WeatherService.Application.Model;

/// <summary>
/// A cached item that outlives its own freshness on purpose.
///
/// Freshness and retention are two different clocks: an entry stops being
/// <i>fresh</i> after a few minutes, but is <i>kept</i> for hours so it can still
/// be served when the provider is unreachable (stale-while-error).
/// </summary>
public sealed record CachedValue<T>(T Value, DateTimeOffset StoredAt, DateTimeOffset FreshUntil)
{
    public bool IsFresh(DateTimeOffset now) => now < FreshUntil;
}
