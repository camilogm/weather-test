using Microsoft.Extensions.Logging;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Application.Forecast;

/// <summary>
/// The forecast use case, and the owner of the degradation chain:
///
///     fresh cache -> provider -> stale cache -> history -> give up
///
/// It performs no I/O of its own; every outbound call goes through a port, which
/// is why the whole chain is exercised by unit tests with no network, clock or
/// database in sight. Resilience mechanics (timeout, retry, circuit breaker) are
/// deliberately absent here: those wrap the provider adapter at the composition
/// root, because they are transport concerns.
/// </summary>
public sealed class ForecastService : IForecastService
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);

    private readonly IWeatherProvider _provider;
    private readonly IWeatherCache _cache;
    private readonly IForecastHistory _history;
    private readonly TimeProvider _clock;
    private readonly ILogger<ForecastService> _logger;

    public ForecastService(
        IWeatherProvider provider,
        IWeatherCache cache,
        IForecastHistory history,
        TimeProvider clock,
        ILogger<ForecastService> logger)
    {
        _provider = provider;
        _cache = cache;
        _history = history;
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

        try
        {
            var forecast = await _provider.GetWeeklyForecastAsync(location, cancellationToken);

            _cache.Set(key, forecast, FreshFor, KeepFor);
            await PersistQuietlyAsync(forecast, cancellationToken);

            _logger.LogInformation(
                "Forecast for {Location} retrieved from {Provider}",
                location.Name,
                _provider.Name);

            return new ForecastResult(forecast, ForecastSource.Provider);
        }
        catch (WeatherProviderException providerFailure)
        {
            _logger.LogWarning(
                providerFailure,
                "Provider {Provider} could not answer for {Location}; degrading",
                _provider.Name,
                location.Name);

            return await DegradeAsync(location, cached, providerFailure, cancellationToken);
        }
    }

    /// <summary>
    /// Walks the remaining sources, most recent first. A stale cache entry beats
    /// stored history because it was written later by definition.
    /// </summary>
    private async Task<ForecastResult> DegradeAsync(
        GeoLocation location,
        CachedValue<WeeklyForecast>? cached,
        WeatherProviderException cause,
        CancellationToken cancellationToken)
    {
        if (cached is not null)
        {
            _logger.LogWarning(
                "Serving a stale forecast for {Location} cached at {StoredAt:o}",
                location.Name,
                cached.StoredAt);

            return new ForecastResult(cached.Value, ForecastSource.StaleCache);
        }

        var persisted = await ReadHistoryQuietlyAsync(location, cancellationToken);
        if (persisted is not null)
        {
            _logger.LogWarning(
                "Serving a historical forecast for {Location} recorded at {RetrievedAt:o}",
                location.Name,
                persisted.RetrievedAt);

            return new ForecastResult(persisted, ForecastSource.Historical);
        }

        throw new ForecastUnavailableException(location, cause);
    }

    /// <summary>
    /// Recording history is a side effect of answering, not part of the answer.
    /// A database outage must not turn a perfectly good forecast into a 500.
    /// </summary>
    private async Task PersistQuietlyAsync(WeeklyForecast forecast, CancellationToken cancellationToken)
    {
        try
        {
            await _history.SaveAsync(forecast, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Could not persist the forecast for {Location}; serving it anyway",
                forecast.Location.Name);
        }
    }

    /// <summary>
    /// Already degrading. If the database is down too there is simply nothing
    /// left to serve, and the caller deserves that answer rather than an
    /// unrelated exception type leaking out as a 500.
    /// </summary>
    private async Task<WeeklyForecast?> ReadHistoryQuietlyAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _history.GetLatestAsync(location, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not read forecast history for {Location}", location.Name);
            return null;
        }
    }

    private static string CacheKeyFor(GeoLocation location) => $"forecast:weekly:{location.Key}";
}
