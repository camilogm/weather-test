using Microsoft.Extensions.Logging;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Application.Forecast;

/// <summary>
/// The forecast use case. Owns the read-through cache and — as tests are added —
/// the rest of the degradation chain. It performs no I/O of its own: every
/// outbound call goes through a port.
/// </summary>
public sealed class ForecastService : IForecastService
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    private readonly IWeatherProvider _provider;
    private readonly IWeatherCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<ForecastService> _logger;

    public ForecastService(
        IWeatherProvider provider,
        IWeatherCache cache,
        TimeProvider clock,
        ILogger<ForecastService> logger)
    {
        _provider = provider;
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ForecastResult> GetWeeklyForecastAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        var key = CacheKeyFor(location);
        var cached = _cache.Get<WeeklyForecast>(key);

        if (cached is not null && cached.IsFresh(_clock.GetUtcNow()))
        {
            _logger.LogDebug("Forecast cache hit for {Location}", location.Name);
            return new ForecastResult(cached.Value, ForecastSource.Cache);
        }

        var forecast = await _provider.GetWeeklyForecastAsync(location, cancellationToken);
        _cache.Set(key, forecast, FreshFor, KeepFor);

        _logger.LogInformation(
            "Forecast for {Location} retrieved from {Provider}",
            location.Name,
            _provider.Name);

        return new ForecastResult(forecast, ForecastSource.Provider);
    }

    /// <summary>
    /// Coordinates are rounded so that near-identical requests share a cache slot;
    /// four decimals is roughly 11 metres, far below any weather grid resolution.
    /// </summary>
    private static string CacheKeyFor(GeoLocation location) =>
        $"forecast:weekly:{location.Latitude:F4},{location.Longitude:F4}";
}
