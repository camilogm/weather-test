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
/// </summary>
public sealed class EfForecastHistory : IForecastHistory
{
    private readonly WeatherDbContext _context;
    private readonly ILogger<EfForecastHistory> _logger;

    public EfForecastHistory(WeatherDbContext context, ILogger<EfForecastHistory> logger)
    {
        _context = context;
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
    }

    public async Task<WeeklyForecast?> GetLatestAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        // Pulled into a local so EF treats it as a parameter rather than trying
        // to translate the property access into SQL.
        var locationKey = location.Key;

        var snapshot = await _context
            .Forecasts.AsNoTracking()
            .Include(s => s.Days)
            .Where(s => s.LocationKey == locationKey)
            .OrderByDescending(s => s.RetrievedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return snapshot is null ? null : ToDomain(snapshot);
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
