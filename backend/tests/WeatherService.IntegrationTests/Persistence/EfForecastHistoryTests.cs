using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

        _sut = new EfForecastHistory(_context, NullLogger<EfForecastHistory>.Instance);
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

    private static WeeklyForecast AForecastFor(GeoLocation location, DateTimeOffset retrievedAt) =>
        new(
            location,
            Enumerable
                .Range(0, 7)
                .Select(offset => new DailyForecast(
                    new DateOnly(2026, 9, 7).AddDays(offset),
                    MinTemperatureC: 21.0 + offset,
                    MaxTemperatureC: 31.0 + offset,
                    Condition: WeatherCondition.PartlyCloudy))
                .ToArray(),
            retrievedAt);
}
