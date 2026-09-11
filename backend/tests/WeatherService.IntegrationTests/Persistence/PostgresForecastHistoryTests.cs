using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WeatherService.Application.Model;
using WeatherService.Infrastructure.Persistence;

namespace WeatherService.IntegrationTests.Persistence;

/// <summary>
/// The half <see cref="EfForecastHistoryTests"/> cannot speak for.
///
/// Those tests run on SQLite and prove the LOGIC — the windows, the ordering, the
/// pruning. What they cannot prove is anything about the engine production
/// actually runs, and the gap was real: the Postgres migrations were versioned in
/// this repository and executed by no test at all. A broken one would have passed
/// every check here and failed on `docker compose up`, in front of whoever ran it
/// first.
///
/// So this suite is deliberately NOT a mirror of the SQLite one. Re-proving the
/// age cap on a second engine buys very little; what earns its place is everything
/// that is engine-specific and therefore invisible to SQLite:
///
/// - that the Postgres migrations apply from nothing, and leave nothing pending
/// - that the column types they chose can actually carry the values written
/// - that ON DELETE CASCADE exists in the real schema, since the prune relies on
///   the database to clear child rows rather than on EF's change tracker
/// - that snake_case naming landed, because Postgres folds unquoted identifiers
///   to lower case and a PascalCase column is one that needs quoting forever
///
/// Each test gets its own freshly migrated database inside one shared container,
/// so nothing leaks between them and the migrations are exercised every time
/// rather than once.
/// </summary>
public sealed class PostgresForecastHistoryTests
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly GeoLocation SanSalvador = new("San Salvador", 13.6929, -89.2182);
    private static readonly GeoLocation Guatemala = new("Guatemala City", 14.6349, -90.5069);
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresContainerFixture _postgres;
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly string _database = $"weather_{Guid.NewGuid():N}";

    private PostgresWeatherDbContext? _context;
    private EfForecastHistory? _sut;

    public PostgresForecastHistoryTests(PostgresContainerFixture postgres) => _postgres = postgres;

    private PostgresWeatherDbContext Context =>
        _context ?? throw new InvalidOperationException("Postgres was not available.");

    private EfForecastHistory Sut =>
        _sut ?? throw new InvalidOperationException("Postgres was not available.");

    public async Task InitializeAsync()
    {
        if (_postgres.AdminConnectionString is null)
        {
            return;
        }

        await CreateDatabaseAsync(_postgres.AdminConnectionString, _database);

        var options = new DbContextOptionsBuilder<PostgresWeatherDbContext>()
            .UseNpgsql(ConnectionStringFor(_postgres.AdminConnectionString, _database))
            .Options;

        _context = new PostgresWeatherDbContext(options);

        // The point of the whole suite: the real migrations, on the real engine,
        // from an empty database.
        await _context.Database.MigrateAsync();

        _sut = new EfForecastHistory(_context, _clock, NullLogger<EfForecastHistory>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }

    // ------------------------------------------------------------------ migrations

    /// <summary>
    /// The gap this suite exists to close. `MigrateAsync` in
    /// <see cref="InitializeAsync"/> having not thrown is already most of the
    /// proof; this pins the rest — that the migration was recorded, and that the
    /// model in code does not want a migration nobody has written yet.
    /// </summary>
    [RequiresDockerFact]
    public async Task Applies_every_postgres_migration_and_leaves_none_pending()
    {
        var applied = await Context.Database.GetAppliedMigrationsAsync();
        var pending = await Context.Database.GetPendingMigrationsAsync();

        applied.Should().NotBeEmpty("the schema has to come from the migrations, not from EnsureCreated");
        pending.Should().BeEmpty();
    }

    /// <summary>
    /// Postgres folds an unquoted identifier to lower case, so a column EF named
    /// `RetrievedAtUtc` becomes one that every hand-written query has to quote
    /// forever. The naming convention is what prevents that, and a convention
    /// nothing checks is a convention that half-applies on the next property.
    /// </summary>
    [RequiresDockerFact]
    public async Task Names_every_column_in_snake_case()
    {
        var columns = await QueryStringsAsync(
            """
            SELECT column_name FROM information_schema.columns
            WHERE table_name = 'forecast_snapshots'
            ORDER BY column_name
            """);

        columns
            .Should()
            .BeEquivalentTo(
                ["id", "latitude", "location_key", "location_name", "longitude", "retrieved_at_utc"]);
    }

    /// <summary>
    /// The prune uses `ExecuteDeleteAsync`, which goes straight to SQL and never
    /// loads the child rows — so what clears them is the cascade declared in the
    /// DATABASE. On SQLite that is proven by row counts; here it is proven against
    /// the catalogue, because a migration that dropped the cascade would still
    /// pass a count on an engine that never had it.
    /// </summary>
    [RequiresDockerFact]
    public async Task Declares_the_cascade_the_prune_depends_on()
    {
        var rules = await QueryStringsAsync(
            """
            SELECT rc.delete_rule
            FROM information_schema.referential_constraints rc
            JOIN information_schema.table_constraints tc
              ON tc.constraint_name = rc.constraint_name
            WHERE tc.table_name = 'forecast_days'
            """);

        rules.Should().ContainSingle().Which.Should().Be("CASCADE");
    }

    // ------------------------------------------------------------- column types

    /// <summary>
    /// Npgsql refuses to write a <see cref="DateTime"/> whose kind is unspecified
    /// into `timestamp with time zone`, and the repository hands it
    /// `RetrievedAt.UtcDateTime`. That pairing either works or throws outright,
    /// and only a real Postgres can say which.
    /// </summary>
    [RequiresDockerFact]
    public async Task Round_trips_the_retrieval_instant_through_a_timestamptz()
    {
        await Sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);

        var found = await Sut.GetLatestAsync(SanSalvador, CancellationToken.None);

        found.Should().NotBeNull();
        found!.RetrievedAt.Should().Be(Now);
        found.RetrievedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    /// <summary>
    /// `DateOnly` maps to a real `date` column here rather than to text. Stored as
    /// text it would still round-trip through EF and sort lexicographically by
    /// accident, which holds right up until a locale or a format changes.
    /// </summary>
    [RequiresDockerFact]
    public async Task Stores_each_day_as_a_real_date_and_keeps_them_in_order()
    {
        await Sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);

        var found = await Sut.GetLatestAsync(SanSalvador, CancellationToken.None);
        var type = await QueryStringsAsync(
            """
            SELECT data_type FROM information_schema.columns
            WHERE table_name = 'forecast_days' AND column_name = 'date'
            """);

        type.Should().ContainSingle().Which.Should().Be("date");
        found!.Days.Should().HaveCount(7).And.BeInAscendingOrder(day => day.Date);
        found.Days[0].Date.Should().Be(DateOnly.FromDateTime(Now.UtcDateTime));
    }

    /// <summary>
    /// The condition is persisted as text, not as an ordinal, so reordering the
    /// enum cannot silently rewrite rows already on disk. This reads the stored
    /// value rather than the mapped one, because EF would translate an ordinal
    /// back into the right name and hide the difference.
    /// </summary>
    [RequiresDockerFact]
    public async Task Stores_the_condition_as_its_name()
    {
        await Sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);

        var stored = await QueryStringsAsync("SELECT DISTINCT condition FROM forecast_days");

        stored.Should().ContainSingle().Which.Should().Be(nameof(WeatherCondition.PartlyCloudy));
    }

    // ----------------------------------------------------------------- behaviour

    /// <summary>
    /// The one behavioural test worth repeating on this engine, because it is the
    /// one SQLite could not run at all: ordering by the retrieval instant.
    /// `OrderByDescending` over a `DateTimeOffset` threw
    /// `SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY`,
    /// which is why the column is a `DateTime`. That workaround has to still sort
    /// correctly on the engine it was not written for.
    /// </summary>
    [RequiresDockerFact]
    public async Task Returns_the_most_recently_stored_snapshot()
    {
        await Sut.SaveAsync(AForecastFor(SanSalvador, Now.AddHours(-6)), CancellationToken.None);
        await Sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);
        await Sut.SaveAsync(AForecastFor(SanSalvador, Now.AddHours(-3)), CancellationToken.None);

        var found = await Sut.GetLatestAsync(SanSalvador, CancellationToken.None);

        found!.RetrievedAt.Should().Be(Now);
    }

    /// <summary>
    /// End to end on the real engine: an expired snapshot is deleted by SQL the
    /// prune never loaded, and its days go with it through the cascade. The count
    /// on `forecast_days` is the part that would survive a missing cascade as an
    /// orphan rather than as an error.
    /// </summary>
    [RequiresDockerFact]
    public async Task Pruning_removes_the_expired_snapshot_and_its_days()
    {
        await SeedAsync(SanSalvador, Now - EfForecastHistory.RetainFor - TimeSpan.FromHours(1));
        await SeedAsync(Guatemala, Now.AddDays(-30));

        await Sut.SaveAsync(AForecastFor(SanSalvador, Now), CancellationToken.None);

        var remaining = await Context
            .Forecasts.AsNoTracking()
            .Where(snapshot => snapshot.LocationKey == SanSalvador.Key)
            .Select(snapshot => snapshot.RetrievedAtUtc)
            .ToListAsync();

        remaining.Should().BeEquivalentTo([Now.UtcDateTime]);

        // Seven from the snapshot just written, seven from Guatemala's untouched
        // row — the expired snapshot's days are gone, and only its own.
        var days = await Context.ForecastDays.CountAsync();
        days.Should().Be(14);
    }

    // ------------------------------------------------------------------- helpers

    private static async Task CreateDatabaseAsync(string adminConnectionString, string database)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        // The name is a GUID this class generated, so there is nothing to quote
        // against; it is interpolated because CREATE DATABASE takes no parameters.
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
        await create.ExecuteNonQueryAsync();
    }

    private static string ConnectionStringFor(string adminConnectionString, string database) =>
        new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = database }
            .ConnectionString;

    private async Task<List<string>> QueryStringsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Context.Database.GetConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    /// <summary>
    /// Writes straight through the context rather than through the port, so an
    /// ageing row can be planted without the write path pruning it on the way in.
    /// </summary>
    private async Task SeedAsync(GeoLocation location, DateTimeOffset retrievedAt)
    {
        var forecast = AForecastFor(location, retrievedAt);

        Context.Forecasts.Add(
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

        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
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
