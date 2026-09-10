using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using WeatherService.Api.RateLimiting;
using WeatherService.Application.Forecast;
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

    public FakeWeatherProvider Provider { get; }

    public FakeLocationResolver Locations { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Off unless a test says otherwise. A suite that is not about throttling
    /// should not have to count its own requests, and one that shares a class
    /// fixture would otherwise fail depending on how many tests ran first.
    /// Mutate this before the host is built.
    /// </summary>
    public RateLimitOptions RateLimits { get; } = new() { Enabled = false };

    public WeatherApiFactory()
    {
        // Wired here rather than in a property initialiser: those run in
        // declaration order, so Clock would still be null.
        Provider = new FakeWeatherProvider { Clock = Clock };
    }

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

        builder.UseSetting("RateLimiting:Enabled", RateLimits.Enabled ? "true" : "false");
        builder.UseSetting("RateLimiting:Window", RateLimits.Window.ToString());
        builder.UseSetting(
            "RateLimiting:WeatherPermits",
            RateLimits.WeatherPermits.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting(
            "RateLimiting:SearchPermits",
            RateLimits.SearchPermits.ToString(CultureInfo.InvariantCulture));

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
    /// <summary>
    /// The same frozen clock the application runs on.
    ///
    /// Stamping DateTimeOffset.UtcNow here instead would put wall-clock time on
    /// the one field the history read measures against, which is how a suite
    /// starts failing on a Tuesday for reasons no one can reproduce.
    /// </summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    public string Name => "fake";

    public bool IsDown { get; set; }

    public int ForecastCalls { get; private set; }

    public Task<ForecastSeries> GetForecastAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        ForecastCalls++;

        if (IsDown)
        {
            throw new WeatherProviderException(Name, "the upstream is down");
        }

        // The whole horizon, like the real adapter. A fake that answered the
        // requested range instead would quietly hide the fact that the range
        // never reaches a provider at all.
        var start = new DateOnly(2026, 9, 7);
        var days = Enumerable
            .Range(0, ForecastHorizon.MaximumDays)
            .Select(offset => new DailyForecast(
                start.AddDays(offset),
                21.0 + offset,
                31.0 + offset,
                WeatherCondition.PartlyCloudy))
            .ToArray();

        return Task.FromResult(new ForecastSeries(location, days, Clock.GetUtcNow()));
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
                Clock.GetUtcNow()));
    }
}

/// <summary>Knows a handful of cities and nothing else, on purpose.</summary>
public sealed class FakeLocationResolver : ILocationResolver
{
    private static readonly LocationMatch[] Cities =
    [
        new("San Salvador", "San Salvador", "El Salvador", "SV", 13.6929, -89.2182),
        new("San Salvador de Jujuy", "Jujuy", "Argentina", "AR", -24.1858, -65.2995),
        new("Guatemala City", "Guatemala", "Guatemala", "GT", 14.6349, -90.5069),
    ];

    /// <summary>Set to have the geocoder behave as if it were down.</summary>
    public bool IsDown { get; set; }

    public Task<GeoLocation> ResolveAsync(string city, CancellationToken cancellationToken)
    {
        if (IsDown)
        {
            throw new UnknownLocationException(city);
        }

        var match = Cities.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, city, StringComparison.OrdinalIgnoreCase));

        return match is null
            ? throw new UnknownLocationException(city)
            : Task.FromResult(match.ToGeoLocation());
    }

    public Task<IReadOnlyList<LocationMatch>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        if (IsDown)
        {
            throw new WeatherProviderException("fake-geocoding", "the geocoder is down");
        }

        if (query.Trim().Length < 2)
        {
            return Task.FromResult<IReadOnlyList<LocationMatch>>([]);
        }

        IReadOnlyList<LocationMatch> matches =
        [
            .. Cities.Where(candidate =>
                    candidate.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(limit),
        ];

        return Task.FromResult(matches);
    }
}
