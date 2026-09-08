using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;
using WeatherService.Infrastructure;
using WeatherService.Infrastructure.Configuration;
using WeatherService.Infrastructure.Providers.OpenMeteo;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace WeatherService.IntegrationTests.Resilience;

/// <summary>
/// Proves the resilience pipeline actually does what it claims, against a real
/// HTTP server that can be told to misbehave on demand.
///
/// The point of a circuit breaker is not "pick a different provider" — it is to
/// STOP WAITING. A hung dependency with no breaker parks a thread per in-flight
/// request until it times out, and a few hundred concurrent requests later the
/// service is dead for reasons that have nothing to do with its own code. So the
/// assertion that matters is not "the call failed": it is that once the circuit
/// is open, NO FURTHER REQUEST REACHES THE NETWORK AT ALL.
/// </summary>
public sealed class CircuitBreakerTests : IAsyncLifetime
{
    private static readonly GeoLocation SanSalvador = new("San Salvador", 13.6929, -89.2182);

    private const string ValidForecastPayload = """
        {
          "daily": {
            "time": ["2026-09-07","2026-09-08","2026-09-09","2026-09-10","2026-09-11","2026-09-12","2026-09-13"],
            "weather_code": [0, 2, 3, 61, 95, 45, 80],
            "temperature_2m_max": [31.2, 32.0, 30.4, 28.9, 27.5, 30.1, 29.8],
            "temperature_2m_min": [21.4, 21.9, 22.0, 21.1, 20.8, 21.5, 21.2]
          }
        }
        """;

    private WireMockServer _upstream = null!;
    private ServiceProvider? _services;

    public Task InitializeAsync()
    {
        _upstream = WireMockServer.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        _upstream.Stop();
        _upstream.Dispose();
    }

    [Fact]
    public async Task Stops_reaching_the_network_once_the_circuit_is_open()
    {
        GivenTheUpstreamAlwaysFails();
        var provider = CreateProvider(resilience =>
        {
            // Retries off so the request count maps one-to-one onto attempts.
            resilience.MaxRetries = 0;
            resilience.MinimumThroughput = 4;
            resilience.FailureRatio = 0.5;
            resilience.SamplingDuration = TimeSpan.FromSeconds(30);
            resilience.BreakDuration = TimeSpan.FromMinutes(1);
        });

        // Trip it: four consecutive failures at a 50% threshold.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await ExpectProviderFailure(provider);
        }

        _upstream.LogEntries.Should().HaveCount(4, "every attempt so far had to be tried for real");

        // The circuit is open now. These must fail without leaving the process.
        await ExpectProviderFailure(provider);
        await ExpectProviderFailure(provider);
        await ExpectProviderFailure(provider);

        _upstream
            .LogEntries.Should()
            .HaveCount(4, "an open circuit fails fast instead of waiting on a dependency that is down");
    }

    [Fact]
    public async Task Closes_again_once_the_break_expires_and_the_upstream_recovers()
    {
        GivenTheUpstreamAlwaysFails();
        var provider = CreateProvider(resilience =>
        {
            resilience.MaxRetries = 0;
            resilience.MinimumThroughput = 2;
            resilience.FailureRatio = 0.5;
            resilience.SamplingDuration = TimeSpan.FromSeconds(30);
            resilience.BreakDuration = TimeSpan.FromSeconds(1);
        });

        await ExpectProviderFailure(provider);
        await ExpectProviderFailure(provider);
        await ExpectProviderFailure(provider); // fails fast, circuit is open

        _upstream.Reset();
        GivenTheUpstreamIsHealthy();
        await Task.Delay(TimeSpan.FromMilliseconds(1_500)); // let the break elapse

        // Half-open: this trial call is allowed through, succeeds, and closes the circuit.
        var forecast = await provider.GetWeeklyForecastAsync(SanSalvador, CancellationToken.None);

        forecast.Days.Should().HaveCount(7);
    }

    [Fact]
    public async Task Rides_out_a_transient_failure_with_a_retry()
    {
        _upstream
            .Given(Request.Create().WithPath("/v1/forecast").UsingGet())
            .InScenario("transient")
            .WillSetStateTo("recovered")
            .RespondWith(Response.Create().WithStatusCode(503));

        _upstream
            .Given(Request.Create().WithPath("/v1/forecast").UsingGet())
            .InScenario("transient")
            .WhenStateIs("recovered")
            .RespondWith(
                Response.Create().WithStatusCode(200).WithBody(ValidForecastPayload));

        var provider = CreateProvider(resilience =>
        {
            resilience.MaxRetries = 2;
            resilience.RetryDelay = TimeSpan.FromMilliseconds(10);
        });

        var forecast = await provider.GetWeeklyForecastAsync(SanSalvador, CancellationToken.None);

        forecast.Days.Should().HaveCount(7);
        _upstream.LogEntries.Should().HaveCount(2, "the first attempt failed and the retry succeeded");
    }

    // ----------------------------------------------------------------------- arrange

    private void GivenTheUpstreamAlwaysFails() =>
        _upstream
            .Given(Request.Create().WithPath("/v1/forecast").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("upstream is down"));

    private void GivenTheUpstreamIsHealthy() =>
        _upstream
            .Given(Request.Create().WithPath("/v1/forecast").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(ValidForecastPayload));

    private IWeatherProvider CreateProvider(Action<ResilienceSettings> configureResilience)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenMeteoProvider(options =>
        {
            options.BaseAddress = new Uri(_upstream.Url!);
            configureResilience(options.Resilience);
        });

        _services = services.BuildServiceProvider();
        return _services.GetRequiredService<IWeatherProvider>();
    }

    private static async Task ExpectProviderFailure(IWeatherProvider provider)
    {
        var act = () => provider.GetWeeklyForecastAsync(SanSalvador, CancellationToken.None);
        await act.Should().ThrowAsync<WeatherProviderException>();
    }
}
