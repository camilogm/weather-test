using Microsoft.Extensions.Caching.Memory;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Infrastructure.Caching;

/// <summary>
/// In-process implementation of <see cref="IWeatherCache"/>.
///
/// It adds the one thing <see cref="IMemoryCache"/> cannot express on its own:
/// an entry that has stopped being fresh but is still readable. Freshness and
/// retention are tracked separately inside the envelope, so the use case can
/// serve a stale reading while the provider is unreachable instead of having
/// nothing left but a 503.
/// </summary>
public sealed class MemoryWeatherCache : IWeatherCache
{
    private readonly IMemoryCache _memory;
    private readonly TimeProvider _clock;

    public MemoryWeatherCache(IMemoryCache memory, TimeProvider clock)
    {
        _memory = memory;
        _clock = clock;
    }

    public CachedValue<T>? Get<T>(string key)
        where T : class
    {
        var slot = SlotFor<T>(key);

        if (!_memory.TryGetValue(slot, out Envelope<T>? envelope) || envelope is null)
        {
            return null;
        }

        if (_clock.GetUtcNow() >= envelope.KeepUntil)
        {
            _memory.Remove(slot);
            return null;
        }

        return new CachedValue<T>(envelope.Value, envelope.StoredAt, envelope.FreshUntil);
    }

    public void Set<T>(string key, T value, TimeSpan freshFor, TimeSpan keepFor)
        where T : class
    {
        var now = _clock.GetUtcNow();

        _memory.Set(
            SlotFor<T>(key),
            new Envelope<T>(value, now, now.Add(freshFor), now.Add(keepFor)),
            new MemoryCacheEntryOptions
            {
                // Retention is enforced by the envelope above; this only lets the
                // runtime reclaim memory for entries nobody comes back for.
                AbsoluteExpirationRelativeToNow = keepFor,
            });
    }

    /// <summary>
    /// The stored type is part of the slot so two different shapes can never be
    /// read back through the wrong one under the same caller-supplied key.
    /// </summary>
    private static string SlotFor<T>(string key) => $"{typeof(T).FullName}::{key}";

    private sealed record Envelope<T>(
        T Value,
        DateTimeOffset StoredAt,
        DateTimeOffset FreshUntil,
        DateTimeOffset KeepUntil);
}
