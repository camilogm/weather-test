using Microsoft.Extensions.Logging;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Application.Current;

/// <summary>
/// Current conditions, read through the same cache the forecast uses.
///
/// The chain is shorter here than for the forecast, and deliberately so: stored
/// history is a record of FORECASTS, and replaying yesterday's observation as if
/// it were current would be an outright lie. A stale reading labelled as stale is
/// honest; a stale reading dressed up as live is not. So this falls back to the
/// stale cache and then stops.
/// </summary>
public sealed class CurrentWeatherService : ICurrentWeatherService
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(3);

    private readonly IWeatherProvider _provider;
    private readonly IWeatherCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<CurrentWeatherService> _logger;

    public CurrentWeatherService(
        IWeatherProvider provider,
        IWeatherCache cache,
        TimeProvider clock,
        ILogger<CurrentWeatherService> logger)
    {
        _provider = provider;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    public async Task<CurrentWeatherResult> GetCurrentWeatherAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        var key = $"weather:current:{location.Key}";
        var cached = _cache.Get<CurrentWeather>(key);

        if (cached is not null && cached.IsFresh(_clock.GetUtcNow()))
        {
            _logger.LogDebug("Current weather cache hit for {Location}", location.Name);
            return new CurrentWeatherResult(cached.Value, WeatherDataSource.Cache);
        }

        try
        {
            var weather = await _provider.GetCurrentWeatherAsync(location, cancellationToken);
            _cache.Set(key, weather, FreshFor, KeepFor);

            return new CurrentWeatherResult(weather, WeatherDataSource.Provider);
        }
        catch (WeatherProviderException providerFailure)
        {
            if (cached is not null)
            {
                _logger.LogWarning(
                    providerFailure,
                    "Serving stale current weather for {Location} observed at {ObservedAt:o}",
                    location.Name,
                    cached.Value.ObservedAt);

                return new CurrentWeatherResult(cached.Value, WeatherDataSource.StaleCache);
            }

            throw new CurrentWeatherUnavailableException(location, providerFailure);
        }
    }
}
