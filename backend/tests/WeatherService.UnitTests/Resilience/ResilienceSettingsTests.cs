using FluentAssertions;
using WeatherService.Infrastructure.Configuration;

namespace WeatherService.UnitTests.Resilience;

/// <summary>
/// The production defaults, checked as arithmetic.
///
/// <see cref="CircuitBreakerTests"/> proves the pipeline works; it does not
/// prove these numbers ever put it in the state being proved, because it
/// overrides them to reach that state quickly. That gap is how a configuration
/// can be wrong under a suite that is entirely green: the mechanism is tested,
/// the settings driving it are not.
///
/// So these assert the two relationships the settings have to hold. They are
/// arithmetic rather than behaviour on purpose — the real timings would cost
/// half a minute of wall clock to observe, which is exactly why nobody checks
/// them by hand.
/// </summary>
public sealed class ResilienceSettingsTests
{
    [Fact]
    public void Every_attempt_fits_inside_the_total_timeout()
    {
        var settings = new ResilienceSettings();

        var (_, attemptsMade) = SimulateHungUpstream(settings);

        attemptsMade
            .Should()
            .Be(
                settings.MaxRetries + 1,
                "an attempt the total timeout is guaranteed to cut short is latency "
                    + "paid for a request that can never finish");
    }

    [Fact]
    public void A_hung_upstream_reaches_the_breaker_threshold_inside_the_sampling_window()
    {
        var settings = new ResilienceSettings();

        var (waited, attemptsMade) = SimulateHungUpstream(settings);
        var callsPerWindow = settings.SamplingDuration.Ticks / waited.Ticks;

        (callsPerWindow * attemptsMade)
            .Should()
            .BeGreaterThanOrEqualTo(
                settings.MinimumThroughput,
                "MinimumThroughput gates the failure ratio, so a breaker that cannot "
                    + "reach it under sequential traffic never opens at all");
    }

    /// <summary>
    /// Walks the retry timeline the way the pipeline does and reports what a
    /// hung upstream costs: how long the caller waits, and how many attempts
    /// the circuit breaker gets to see.
    ///
    /// Only attempts that reach their own timeout are counted. One the outer
    /// total timeout cancels is a cancellation rather than a failure, and Polly
    /// does not record those — so the breaker never hears about it.
    /// </summary>
    private static (TimeSpan Waited, int AttemptsMade) SimulateHungUpstream(
        ResilienceSettings settings)
    {
        var elapsed = TimeSpan.Zero;
        var attemptsMade = 0;

        for (var attempt = 0; attempt <= settings.MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                // Exponential from the configured delay. Jitter can only push
                // this higher, so the nominal figure is the optimistic one.
                elapsed += settings.RetryDelay * Math.Pow(2, attempt - 1);
            }

            if (elapsed + settings.AttemptTimeout > settings.TotalTimeout)
            {
                return (settings.TotalTimeout, attemptsMade);
            }

            elapsed += settings.AttemptTimeout;
            attemptsMade++;
        }

        return (elapsed, attemptsMade);
    }
}
