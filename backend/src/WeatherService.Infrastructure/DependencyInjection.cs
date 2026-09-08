using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using WeatherService.Application.Ports;
using WeatherService.Infrastructure.Configuration;
using WeatherService.Infrastructure.Providers.OpenMeteo;

namespace WeatherService.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Open-Meteo client behind its own resilience pipeline.
    ///
    /// The pipeline is written out rather than delegated to
    /// <c>AddStandardResilienceHandler()</c>. The standard handler produces
    /// roughly the same strategies in one line, but it also hides every number,
    /// and the numbers are the actual design decision.
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
    /// </summary>
    public static IServiceCollection AddOpenMeteoProvider(
        this IServiceCollection services,
        Action<WeatherProviderOptions> configure)
    {
        services.AddOptions<WeatherProviderOptions>().Configure(configure).ValidateDataAnnotations();

        services.TryAddSingleton(TimeProvider.System);

        services
            .AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(
                (serviceProvider, client) =>
                {
                    var options = serviceProvider
                        .GetRequiredService<IOptions<WeatherProviderOptions>>()
                        .Value;

                    client.BaseAddress = options.BaseAddress;

                    // Timeouts belong to the pipeline below. Leaving HttpClient's
                    // own timeout in play would race it and surface as a
                    // TaskCanceledException that no strategy can classify.
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
            .AddResilienceHandler(
                "open-meteo",
                (pipeline, context) =>
                {
                    var settings = context
                        .ServiceProvider.GetRequiredService<IOptions<WeatherProviderOptions>>()
                        .Value.Resilience;

                    pipeline.AddTimeout(settings.TotalTimeout);

                    // Polly rejects MaxRetryAttempts = 0, so "no retries" means
                    // leaving the strategy out rather than configuring it to zero.
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
                });

        return services;
    }
}
