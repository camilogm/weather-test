using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using WeatherService.Application.Current;
using WeatherService.Application.Forecast;
using WeatherService.Application.Ports;
using WeatherService.Infrastructure.Caching;
using WeatherService.Infrastructure.Configuration;
using WeatherService.Infrastructure.Persistence;
using WeatherService.Infrastructure.Providers.OpenMeteo;

namespace WeatherService.Infrastructure;

public static class DependencyInjection
{
    /// <summary>The composition root for everything outside the domain.</summary>
    public static IServiceCollection AddWeatherInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IWeatherCache, MemoryWeatherCache>();

        services.AddScoped<IForecastHistory, EfForecastHistory>();
        services.AddScoped<IForecastService, ForecastService>();
        services.AddScoped<ICurrentWeatherService, CurrentWeatherService>();

        services.AddWeatherDatabase(configuration);
        services.AddOpenMeteoProvider(options =>
            configuration.GetSection(WeatherProviderOptions.SectionName).Bind(options));

        return services;
    }

    /// <summary>
    /// Binds the concrete context for the configured engine, then exposes it
    /// under the shared base type so nothing downstream has to care which one
    /// is running.
    /// </summary>
    public static IServiceCollection AddWeatherDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options =
            configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
            ?? new DatabaseOptions();

        if (options.Provider is DatabaseProvider.Sqlite)
        {
            services.AddDbContext<SqliteWeatherDbContext>(builder =>
                builder.UseSqlite(options.SqliteConnectionString));

            services.AddScoped<WeatherDbContext>(provider =>
                provider.GetRequiredService<SqliteWeatherDbContext>());
        }
        else
        {
            services.AddDbContext<PostgresWeatherDbContext>(builder =>
                builder.UseNpgsql(
                    options.ConnectionString,
                    npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null)));

            services.AddScoped<WeatherDbContext>(provider =>
                provider.GetRequiredService<PostgresWeatherDbContext>());
        }

        return services;
    }

    /// <summary>
    /// Registers the Open-Meteo clients behind their own resilience pipelines.
    ///
    /// The pipeline is written out rather than delegated to
    /// <c>AddStandardResilienceHandler()</c>. The one-liner produces roughly the
    /// same strategies but hides every number, and the numbers are the design.
    ///
    /// Order matters, outermost first:
    ///
    ///   total timeout   caps the whole operation including retries
    ///     retry         rides out a blip, with jitter so clients do not
    ///                   synchronise into a thundering herd on recovery
    ///       breaker     stops calling a service that is clearly down
    ///         attempt   caps one individual try
    ///
    /// The breaker sits INSIDE the retry on purpose: retries should count toward
    /// tripping it. Put it outside and a single logical call could hammer a dying
    /// service several times without the breaker ever noticing.
    ///
    /// Forecast and geocoding get SEPARATE clients, so they get separate breaker
    /// state. A geocoder having a bad day must not cut off forecasts that are
    /// answering perfectly well.
    /// </summary>
    public static IServiceCollection AddOpenMeteoProvider(
        this IServiceCollection services,
        Action<WeatherProviderOptions> configure)
    {
        services.AddOptions<WeatherProviderOptions>().Configure(configure).ValidateDataAnnotations();

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.TryAddSingleton<IWeatherCache, MemoryWeatherCache>();

        services
            .AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(ConfigureClient(o => o.BaseAddress))
            .AddResilienceHandler("open-meteo-forecast", BuildPipeline);

        services
            .AddHttpClient<ILocationResolver, OpenMeteoLocationResolver>(
                ConfigureClient(o => o.GeocodingBaseAddress))
            .AddResilienceHandler("open-meteo-geocoding", BuildPipeline);

        return services;
    }

    private static Action<IServiceProvider, HttpClient> ConfigureClient(
        Func<WeatherProviderOptions, Uri> baseAddress) =>
        (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<WeatherProviderOptions>>().Value;

            client.BaseAddress = baseAddress(options);

            // Timeouts belong to the pipeline below. Leaving HttpClient's own
            // timeout in play would race it and surface as a TaskCanceledException
            // that no strategy can classify.
            client.Timeout = Timeout.InfiniteTimeSpan;
        };

    private static void BuildPipeline(
        ResiliencePipelineBuilder<HttpResponseMessage> pipeline,
        ResilienceHandlerContext context)
    {
        var settings = context
            .ServiceProvider.GetRequiredService<IOptions<WeatherProviderOptions>>()
            .Value.Resilience;

        pipeline.AddTimeout(settings.TotalTimeout);

        // Polly rejects MaxRetryAttempts = 0, so "no retries" means leaving the
        // strategy out rather than configuring it to zero.
        if (settings.MaxRetries > 0)
        {
            pipeline.AddRetry(
                new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = settings.MaxRetries,
                    Delay = settings.RetryDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                });
        }

        pipeline.AddCircuitBreaker(
            new HttpCircuitBreakerStrategyOptions
            {
                FailureRatio = settings.FailureRatio,
                MinimumThroughput = settings.MinimumThroughput,
                SamplingDuration = settings.SamplingDuration,
                BreakDuration = settings.BreakDuration,
            });

        pipeline.AddTimeout(settings.AttemptTimeout);
    }
}
