using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Infrastructure.Persistence;

/// <summary>
/// EF Core adapter for <see cref="IForecastHistory"/>.
///
/// Writes are append-only: each live forecast is a new snapshot, never an update.
/// History is a log, not a cache — overwriting would destroy the very record that
/// makes the last rung of the degradation chain useful.
///
/// A log still needs a floor and a ceiling, and they are different numbers:
/// <see cref="RetainFor"/> is how long a row is kept, <see cref="UsableFor"/> is
/// how long it may be served. Keeping the wider window means the serving window
/// can be widened later without having already thrown the rows away.
/// </summary>
public sealed class EfForecastHistory : IForecastHistory
{
    /// <summary>
    /// How long a stored snapshot may still be replayed to a caller.
    ///
    /// A snapshot describes the seven days that followed the moment it was taken.
    /// Let it age far enough and every day in it has already happened, and the
    /// last rung of the degradation chain starts dressing up the past as a
    /// forecast — a 200 that is worse than the 503 it replaced.
    /// </summary>
    public static readonly TimeSpan UsableFor = TimeSpan.FromDays(3);

    /// <summary>
    /// How long a stored snapshot is kept on disk.
    ///
    /// The write rate is already capped by the forecast cache's freshness window,
    /// so the table only grows unboundedly because nothing ever removes a row.
    /// This is that something.
    /// </summary>
    public static readonly TimeSpan RetainFor = TimeSpan.FromDays(7);

    private readonly WeatherDbContext _context;
    private readonly TimeProvider _clock;
    private readonly ILogger<EfForecastHistory> _logger;

    public EfForecastHistory(
        WeatherDbContext context,
        TimeProvider clock,
        ILogger<EfForecastHistory> logger)
    {
        _context = context;
        _clock = clock;
        _logger = logger;
    }

    public async Task SaveAsync(WeeklyForecast forecast, CancellationToken cancellationToken)
    {
        _context.Forecasts.Add(ToSnapshot(forecast));
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "Stored a forecast snapshot for {Location} retrieved at {RetrievedAt:o}",
            forecast.Location.Name,
            forecast.RetrievedAt);

        await PruneQuietlyAsync(forecast.Location, cancellationToken);
    }

    public async Task<WeeklyForecast?> GetLatestAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        // Pulled into locals so EF treats them as parameters rather than trying
        // to translate the property access into SQL.
        var locationKey = location.Key;
        var usableFrom = _clock.GetUtcNow().UtcDateTime - UsableFor;

        var snapshot = await _context
            .Forecasts.AsNoTracking()
            .Include(s => s.Days)
            .Where(s => s.LocationKey == locationKey && s.RetrievedAtUtc >= usableFrom)
            .OrderByDescending(s => s.RetrievedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return snapshot is null ? null : ToDomain(snapshot);
    }

    /// <summary>
    /// Drops this location's expired rows on the way out of a write.
    ///
    /// Equality on the key plus a range on the timestamp is exactly the shape of
    /// ix_forecast_snapshots_location_key_retrieved_at, so the sweep costs one
    /// index scan rather than a table scan.
    ///
    /// Housekeeping, so it never fails the write: the snapshot is already
    /// committed by the time this runs, and refusing to answer because the
    /// cleanup tripped would trade a real problem for an imaginary one. It is
    /// also left outside a transaction on purpose — the retry execution strategy
    /// configured for the context rejects ambient transactions, and a sweep that
    /// misses simply happens on the next write.
    /// </summary>
    private async Task PruneQuietlyAsync(GeoLocation location, CancellationToken cancellationToken)
    {
        var locationKey = location.Key;
        var expiredBefore = _clock.GetUtcNow().UtcDateTime - RetainFor;

        try
        {
            // ExecuteDelete goes straight to SQL and never loads the child rows,
            // so what removes the days is the ON DELETE CASCADE declared on the
            // foreign key in the database, not EF's change tracker.
            var pruned = await _context
                .Forecasts.Where(s => s.LocationKey == locationKey && s.RetrievedAtUtc < expiredBefore)
                .ExecuteDeleteAsync(cancellationToken);

            if (pruned > 0)
            {
                _logger.LogDebug(
                    "Pruned {Count} expired forecast snapshots for {Location}",
                    pruned,
                    location.Name);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Could not prune expired forecast snapshots for {Location}",
                location.Name);
        }
    }

    private static ForecastSnapshot ToSnapshot(WeeklyForecast forecast) =>
        new()
        {
            Id = Guid.NewGuid(),
            LocationKey = forecast.Location.Key,
            LocationName = forecast.Location.Name,
            Latitude = forecast.Location.Latitude,
            Longitude = forecast.Location.Longitude,
            RetrievedAtUtc = forecast.RetrievedAt.UtcDateTime,
            Days = forecast
                .Days.Select(day => new ForecastDay
                {
                    Id = Guid.NewGuid(),
                    Date = day.Date,
                    MinTemperatureC = day.MinTemperatureC,
                    MaxTemperatureC = day.MaxTemperatureC,
                    Condition = day.Condition,
                })
                .ToList(),
        };

    private static WeeklyForecast ToDomain(ForecastSnapshot snapshot) =>
        new(
            new GeoLocation(snapshot.LocationName, snapshot.Latitude, snapshot.Longitude),
            snapshot
                .Days.OrderBy(day => day.Date)
                .Select(day => new DailyForecast(
                    day.Date,
                    day.MinTemperatureC,
                    day.MaxTemperatureC,
                    day.Condition))
                .ToArray(),
            // SQLite hands the value back with an unspecified kind; pinning it to
            // UTC is what makes the round trip land on the instant we stored.
            new DateTimeOffset(DateTime.SpecifyKind(snapshot.RetrievedAtUtc, DateTimeKind.Utc)));
}
