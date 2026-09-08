using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;
using WeatherService.Infrastructure.Persistence;

namespace WeatherService.IntegrationTests.Api;

/// <summary>
/// Boots the real application — real routing, real model binding, real
/// middleware, real ProblemDetails, real EF — and replaces exactly two things:
/// the external weather service and the clock.
///
/// Everything the service actually owns stays under test. Swapping the whole
/// pipeline out for hand-rolled fakes would test the fakes.
/// </summary>
public sealed class WeatherApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeWeatherProvider Provider { get; } = new();

    public FakeLocationResolver Locations { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        // Held open for the lifetime of the fixture: closing it destroys the
        // in-memory database.
        await _connection.OpenAsync();

        using var scope = Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
        await database.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
    }

    public async Task<WeatherDbContext> OpenDatabaseAsync()
    {
        var scope = Services.CreateScope();
        return await Task.FromResult(scope.ServiceProvider.GetRequiredService<WeatherDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting lands in host configuration, which the app reads while it is
        // still registering services — early enough to pick the SQLite context.
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Database:MigrateOnStartup", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<SqliteWeatherDbContext>>();
            services.RemoveAll<SqliteWeatherDbContext>();
            services.AddDbContext<SqliteWeatherDbContext>(options => options.UseSqlite(_connection));

            services.RemoveAll<IWeatherProvider>();
            services.AddSingleton<IWeatherProvider>(Provider);

            services.RemoveAll<ILocationResolver>();
            services.AddSingleton<ILocationResolver>(Locations);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>An upstream that can be told to answer, or to be down.</summary>
public sealed class FakeWeatherProvider : IWeatherProvider
{
    public string Name => "fake";

    public bool IsDown { get; set; }

    public int ForecastCalls { get; private set; }

    public Task<WeeklyForecast> GetWeeklyForecastAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        ForecastCalls++;

        if (IsDown)
        {
            throw new WeatherProviderException(Name, "the upstream is down");
        }

        var start = new DateOnly(2026, 9, 7);
        var days = Enumerable
            .Range(0, 7)
            .Select(offset => new DailyForecast(
                start.AddDays(offset),
                21.0 + offset,
                31.0 + offset,
                WeatherCondition.PartlyCloudy))
            .ToArray();

        return Task.FromResult(new WeeklyForecast(location, days, DateTimeOffset.UtcNow));
    }

    public Task<CurrentWeather> GetCurrentWeatherAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        if (IsDown)
        {
            throw new WeatherProviderException(Name, "the upstream is down");
        }

        return Task.FromResult(
            new CurrentWeather(
                location,
                28.5,
                32.1,
                11.2,
                74,
                WeatherCondition.PartlyCloudy,
                DateTimeOffset.UtcNow));
    }
}

/// <summary>Knows a couple of cities and nothing else, on purpose.</summary>
public sealed class FakeLocationResolver : ILocationResolver
{
    private static readonly Dictionary<string, GeoLocation> Cities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["San Salvador"] = GeoLocation.SanSalvador,
            ["Guatemala City"] = new("Guatemala City", 14.6349, -90.5069),
        };

    public Task<GeoLocation> ResolveAsync(string city, CancellationToken cancellationToken) =>
        Cities.TryGetValue(city, out var found)
            ? Task.FromResult(found)
            : throw new UnknownLocationException(city);
}
