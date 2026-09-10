using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WeatherService.Application.Model;
using WeatherService.Infrastructure.Persistence;

namespace WeatherService.IntegrationTests.Persistence;

/// <summary>
/// Runs against a real SQLite engine held open in memory — a relational database
/// that enforces keys and executes actual SQL. The EF InMemory provider is
/// deliberately not used: it is not relational and happily accepts writes a real
/// database would reject, which turns these into false green tests.
///
/// The schema is created by running the migrations, so a broken migration fails
/// here rather than on the reviewer's machine.
/// </summary>
public sealed class EfForecastHistoryTests : IAsyncLifetime
{
    private static readonly GeoLocation SanSalvador = new("San Salvador", 13.6929, -89.2182);
    private static readonly GeoLocation Guatemala = new("Guatemala City", 14.6349, -90.5069);
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);

    private SqliteConnection _connection = null!;
    private SqliteWeatherDbContext _context = null!;
    private EfForecastHistory _sut = null!;

    public async Task InitializeAsync()
    {
        // Keeping the connection open is what keeps the in-memory database alive.
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<SqliteWeatherDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new SqliteWeatherDbContext(options);
        await _context.Database.MigrateAsync();

        _sut = new EfForecastHistory(_context, _clock, NullLogger<EfForecastHistory>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Returns_null_when_nothing_was_ever_stored_for_the_location()
    {
        var found = await _sut.GetLatestAsync(SanSalvador, CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task Round_trips_a_full_week_of_forecast()
    {
        var forecast = AForecastFor(SanSalvador, Now);

        await _sut.SaveAsync(forecast, CancellationToken.None);
        var found = await _sut.GetLatestAsync(SanSalvador, CancellationToken.None);

        found.Should().NotBeNull();
        found!.Location.Name.Should().Be("San Salvador");
        found.Location.Latitude.Should().BeApproximately(13.6929, 0.0001);
        found.RetrievedAt.Should().Be(Now);
        found.Days.Should().HaveCount(7);
        found.Days.Should().BeInAscendingOrder(day => day.Date);
        found.Days[0].Condition.Should().Be(WeatherCondition.PartlyCloudy);
        found.Days[0].MinTemperatureC.Should().Be(21.0);
        found.Days[6].MaxTemperatureC.Should().Be(37.0);
    }

    [Fact]
    public async Task Returns_the_most_recently_stored_forecast()
    {
        await _sut.SaveAsync(AForecastFor(SanSalvador, Now.AddHours(-6)), CancellationToken.None);
        await _sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);
        await _sut.SaveAsync(AForecastFor(SanSalvador, Now.AddHours(-3)), CancellationToken.None);

        var found = await _sut.GetLatestAsync(SanSalvador, CancellationToken.None);

        found!.RetrievedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Keeps_locations_apart()
    {
        await _sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);

        var found = await _sut.GetLatestAsync(Guatemala, CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task Stores_each_snapshot_rather_than_overwriting_the_previous_one()
    {
        // History is a log, not a cache. Overwriting would destroy the record the
        // brief asks us to keep.
        await _sut.SaveAsync(AForecastFor(SanSalvador, Now.AddHours(-6)), CancellationToken.None);
        await _sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);

        var snapshots = await _context.Forecasts.CountAsync();

        snapshots.Should().Be(2);
    }

    [Fact]
    public async Task Serves_history_recorded_inside_the_usable_window()
    {
        await SeedAsync(SanSalvador, Now - EfForecastHistory.UsableFor + TimeSpan.FromHours(1));

        var found = await _sut.GetLatestAsync(SanSalvador, CancellationToken.None);

        found.Should().NotBeNull();
    }

    [Fact]
    public async Task Ignores_history_older_than_the_usable_window()
    {
        // A stored forecast describes the seven days that followed the moment it
        // was taken. Once it ages past that span every day in it has already
        // happened, and replaying it would dress up the past as a forecast.
        await SeedAsync(SanSalvador, Now - EfForecastHistory.UsableFor - TimeSpan.FromHours(1));

        var found = await _sut.GetLatestAsync(SanSalvador, CancellationToken.None);

        found.Should().BeNull();
    }

    [Fact]
    public async Task Prefers_a_usable_snapshot_over_a_newer_unusable_one_being_absent()
    {
        // The age cap must filter, not just cut off at the newest row: a stale
        // row sitting on top must not hide a usable one underneath it.
        await SeedAsync(SanSalvador, Now.AddDays(-2));
        await SeedAsync(SanSalvador, Now - EfForecastHistory.RetainFor + TimeSpan.FromHours(1));

        var found = await _sut.GetLatestAsync(SanSalvador, CancellationToken.None);

        found!.RetrievedAt.Should().Be(Now.AddDays(-2));
    }

    [Fact]
    public async Task Writing_prunes_snapshots_past_the_retention_window()
    {
        await SeedAsync(SanSalvador, Now - EfForecastHistory.RetainFor - TimeSpan.FromHours(1));
        await SeedAsync(SanSalvador, Now.AddDays(-2));

        await _sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);

        var remaining = await _context
            .Forecasts.AsNoTracking()
            .Select(snapshot => snapshot.RetrievedAtUtc)
            .ToListAsync();

        remaining.Should().BeEquivalentTo([Now.AddDays(-2).UtcDateTime, Now.UtcDateTime]);
    }

    [Fact]
    public async Task Pruning_takes_the_stored_days_with_it()
    {
        // ExecuteDelete bypasses EF's change tracker, so the cascade that clears
        // these rows has to be the one declared on the foreign key in the
        // database itself. If that ever gets dropped, this fails.
        await SeedAsync(SanSalvador, Now - EfForecastHistory.RetainFor - TimeSpan.FromHours(1));

        await _sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);

        var days = await _context.ForecastDays.CountAsync();

        days.Should().Be(7);
    }

    [Fact]
    public async Task Pruning_only_touches_the_location_being_written()
    {
        await SeedAsync(Guatemala, Now.AddDays(-30));

        await _sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);

        var guatemala = await _context.Forecasts.CountAsync(s => s.LocationKey == Guatemala.Key);

        guatemala.Should().Be(1);
    }

    /// <summary>
    /// Writes a snapshot straight through the context rather than through the
    /// port, so ageing rows can be planted without the write path pruning them
    /// on the way in.
    /// </summary>
    private async Task SeedAsync(GeoLocation location, DateTimeOffset retrievedAt)
    {
        var forecast = AForecastFor(location, retrievedAt);

        _context.Forecasts.Add(
            new ForecastSnapshot
            {
                Id = Guid.NewGuid(),
                LocationKey = location.Key,
                LocationName = location.Name,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                RetrievedAtUtc = retrievedAt.UtcDateTime,
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
            });

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private static ForecastSeries AForecastFor(GeoLocation location, DateTimeOffset retrievedAt) =>
        new(
            location,
            Enumerable
                .Range(0, 7)
                .Select(offset => new DailyForecast(
                    DateOnly.FromDateTime(retrievedAt.UtcDateTime).AddDays(offset),
                    MinTemperatureC: 21.0 + offset,
                    MaxTemperatureC: 31.0 + offset,
                    Condition: WeatherCondition.PartlyCloudy))
                .ToArray(),
            retrievedAt);
}
