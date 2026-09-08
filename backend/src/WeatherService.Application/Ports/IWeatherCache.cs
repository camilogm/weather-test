using WeatherService.Application.Model;

namespace WeatherService.Application.Ports;

/// <summary>
/// The port to the cache.
///
/// Deliberately NOT <c>IMemoryCache</c>: the use case must not know whether the
/// cache lives in this process, in Redis, or nowhere. It also needs a concept
/// <c>IMemoryCache</c> does not model — an entry that is expired but still
/// readable — which is what makes stale-while-error possible.
/// </summary>
public interface IWeatherCache
{
    CachedValue<T>? Get<T>(string key)
        where T : class;

    void Set<T>(string key, T value, TimeSpan freshFor, TimeSpan keepFor)
        where T : class;
}
