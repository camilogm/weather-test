using Microsoft.Extensions.Logging;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Application.Forecast;

/// <summary>
/// The forecast use case, and the owner of the degradation chain:
///
///     fresh cache -> provider -> stale cache -> history -> give up
///
/// Every rung of that chain deals in the service's whole horizon. The range a
/// caller asked for is applied once, at the end, as a projection — which is what
/// lets one fetch, one cache entry and one stored snapshot answer every range.
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

    public async Task<ForecastResult> GetForecastAsync(
        GeoLocation location,
        int days,
        CancellationToken cancellationToken)
    {
        var found = await FindAsync(location, cancellationToken);
        var upcoming = found.Forecast.Upcoming(days, _clock.GetUtcNow());

        // Every day the series holds is already over. Unreachable while the
        // history's usability window stays narrower than the horizon, and kept
        // regardless: an empty list rendered as a forecast is a confident answer
        // about nothing, and the caller is owed the honest 503 instead.
        if (upcoming.Days.Count == 0)
        {
            throw new ForecastUnavailableException(location);
        }

        return found with { Forecast = upcoming };
    }

    /// <summary>
    /// Walks the degradation chain and returns the whole horizon from whichever
    /// rung answers first. It knows nothing about ranges — trimming happens once,
    /// above, so no source can be tempted to store or cache a trimmed series.
    /// </summary>
    private async Task<ForecastResult> FindAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        var key = CacheKeyFor(location);
        var cached = _cache.Get<ForecastSeries>(key);

        if (cached is not null && cached.IsFresh(_clock.GetUtcNow()))
        {
            _logger.LogDebug("Forecast cache hit for {Location}", location.Name);
            return new ForecastResult(cached.Value, WeatherDataSource.Cache);
        }

        try
        {
            var forecast = await _provider.GetForecastAsync(location, cancellationToken);

            _cache.Set(key, forecast, FreshFor, KeepFor);
            await PersistQuietlyAsync(forecast, cancellationToken);

            _logger.LogInformation(
                "Forecast for {Location} retrieved from {Provider}",
                location.Name,
                _provider.Name);

            return new ForecastResult(forecast, WeatherDataSource.Provider);
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
        CachedValue<ForecastSeries>? cached,
        WeatherProviderException cause,
        CancellationToken cancellationToken)
    {
        if (cached is not null)
        {
            _logger.LogWarning(
                "Serving a stale forecast for {Location} cached at {StoredAt:o}",
                location.Name,
                cached.StoredAt);

            return new ForecastResult(cached.Value, WeatherDataSource.StaleCache);
        }

        var persisted = await ReadHistoryQuietlyAsync(location, cancellationToken);
        if (persisted is not null)
        {
            _logger.LogWarning(
                "Serving a historical forecast for {Location} recorded at {RetrievedAt:o}",
                location.Name,
                persisted.RetrievedAt);

            return new ForecastResult(persisted, WeatherDataSource.Historical);
        }

        throw new ForecastUnavailableException(location, cause);
    }

    /// <summary>
    /// Recording history is a side effect of answering, not part of the answer.
    /// A database outage must not turn a perfectly good forecast into a 500.
    /// </summary>
    private async Task PersistQuietlyAsync(ForecastSeries forecast, CancellationToken cancellationToken)
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
    private async Task<ForecastSeries?> ReadHistoryQuietlyAsync(
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

    /// <summary>
    /// Keyed on the location alone. Folding the requested range in would split
    /// one warm entry into one per range, and a service already holding sixteen
    /// days would go back to the network to be asked for seven of them.
    /// </summary>
    private static string CacheKeyFor(GeoLocation location) => $"forecast:{location.Key}";
}
