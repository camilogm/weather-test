using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WeatherService.Application.Forecast;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.UnitTests.Forecast;

/// <summary>
/// Drives the design of the forecast use case.
///
/// The service owns the degradation chain and nothing else; every outbound call
/// goes through a port, so these tests need no network, no clock and no database:
///
///     fresh cache -> provider -> stale cache -> history -> give up
/// </summary>
public class ForecastServiceTests
{
    private static readonly GeoLocation SanSalvador = new("San Salvador", 13.6929, -89.2182);
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly IWeatherProvider _provider = Substitute.For<IWeatherProvider>();
    private readonly IWeatherCache _cache = Substitute.For<IWeatherCache>();
    private readonly IForecastHistory _history = Substitute.For<IForecastHistory>();
    private readonly FakeTimeProvider _clock = new(Now);

    public ForecastServiceTests() => _provider.Name.Returns("open-meteo");

    private ForecastService CreateSut() =>
        new(_provider, _cache, _history, _clock, NullLogger<ForecastService>.Instance);

    // ---------------------------------------------------------------- happy path

    [Fact]
    public async Task Queries_the_provider_when_the_cache_is_empty()
    {
        GivenNothingCached();
        _provider
            .GetForecastAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(AForecastFor(SanSalvador));

        var result = await CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.Provider);
        result.IsDegraded.Should().BeFalse();
        result.Forecast.Days.Should().HaveCount(ForecastHorizon.DefaultDays);
        result.Forecast.Location.Name.Should().Be("San Salvador");
    }

    [Fact]
    public async Task Serves_the_cache_without_touching_the_provider_while_it_is_fresh()
    {
        GivenCached(AForecastFor(SanSalvador), freshFor: TimeSpan.FromMinutes(10));

        var result = await CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.Cache);
        await _provider
            .DidNotReceiveWithAnyArgs()
            .GetForecastAsync(default!, default);
    }

    [Fact]
    public async Task Persists_every_live_forecast_so_it_can_serve_as_a_later_fallback()
    {
        GivenNothingCached();
        var forecast = AForecastFor(SanSalvador);
        _provider
            .GetForecastAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(forecast);

        await CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        await _history.Received(1).SaveAsync(forecast, Arg.Any<CancellationToken>());
    }

    // ------------------------------------------------------------- degraded paths

    [Fact]
    public async Task Falls_back_to_the_stale_cache_when_the_provider_fails()
    {
        var stale = AForecastFor(SanSalvador);
        GivenCached(stale, freshFor: TimeSpan.FromMinutes(10));
        _clock.Advance(TimeSpan.FromMinutes(30)); // the entry is kept, but no longer fresh
        GivenTheProviderIsDown();

        var result = await CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.StaleCache);
        result.IsDegraded.Should().BeTrue();
        result.Forecast.RetrievedAt.Should().Be(stale.RetrievedAt);
        result.Forecast.Days.Should().BeSubsetOf(stale.Days);
    }

    [Fact]
    public async Task Falls_back_to_history_when_the_provider_fails_and_nothing_is_cached()
    {
        GivenNothingCached();
        GivenTheProviderIsDown();
        var persisted = AForecastFor(SanSalvador);
        _history
            .GetLatestAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(persisted);

        var result = await CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.Historical);
        result.IsDegraded.Should().BeTrue();
        result.Forecast.RetrievedAt.Should().Be(persisted.RetrievedAt);
        result.Forecast.Days.Should().BeSubsetOf(persisted.Days);
    }

    [Fact]
    public async Task Prefers_the_stale_cache_over_history_because_it_is_more_recent()
    {
        var stale = AForecastFor(SanSalvador);
        GivenCached(stale, freshFor: TimeSpan.FromMinutes(10));
        _clock.Advance(TimeSpan.FromMinutes(30));
        GivenTheProviderIsDown();
        _history
            .GetLatestAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(AForecastFor(SanSalvador));

        var result = await CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        result.Forecast.Days.Should().BeSubsetOf(stale.Days);
        await _history.DidNotReceiveWithAnyArgs().GetLatestAsync(default!, default);
    }

    [Fact]
    public async Task Throws_when_every_source_is_exhausted()
    {
        GivenNothingCached();
        GivenTheProviderIsDown();
        _history
            .GetLatestAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns((ForecastSeries?)null);

        var act = () => CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        await act.Should()
            .ThrowAsync<ForecastUnavailableException>()
            .Where(e => e.Location == SanSalvador);
    }

    // ------------------------------------------------------------------ robustness

    [Fact]
    public async Task Still_answers_when_persisting_the_history_fails()
    {
        // A broken database must not turn a perfectly good forecast into a 500.
        // Persisting history is a side effect, not part of the caller's request.
        GivenNothingCached();
        _provider
            .GetForecastAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(AForecastFor(SanSalvador));
        _history
            .SaveAsync(Arg.Any<ForecastSeries>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unreachable"));

        var result = await CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.Provider);
    }

    [Fact]
    public async Task Reports_the_forecast_as_unavailable_when_the_history_lookup_itself_fails()
    {
        // A database failure while degrading must not leak as an unrelated
        // exception type: that would surface as a 500 instead of an honest 503.
        GivenNothingCached();
        GivenTheProviderIsDown();
        _history
            .GetLatestAsync(Arg.Any<GeoLocation>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unreachable"));

        var act = () => CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        await act.Should().ThrowAsync<ForecastUnavailableException>();
    }

    // ------------------------------------------------------------------ ranges

    /// <summary>
    /// The whole point of the projection: one fetch, one cache entry, one stored
    /// snapshot, and every range answered from it.
    /// </summary>
    [Fact]
    public async Task Fetches_the_whole_horizon_however_few_days_the_caller_wants()
    {
        GivenNothingCached();
        _provider
            .GetForecastAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(AForecastFor(SanSalvador));

        var result = await CreateSut().GetForecastAsync(SanSalvador, 3, CancellationToken.None);

        result.Forecast.Days.Should().HaveCount(3);
        await _history
            .Received(1)
            .SaveAsync(
                Arg.Is<ForecastSeries>(f => f.Days.Count == ForecastHorizon.MaximumDays),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Serves_a_longer_range_than_the_old_contract_offered()
    {
        GivenNothingCached();
        _provider
            .GetForecastAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(AForecastFor(SanSalvador));

        var result = await CreateSut()
            .GetForecastAsync(SanSalvador, ForecastHorizon.MaximumDays, CancellationToken.None);

        result.Forecast.Days.Should().HaveCount(ForecastHorizon.MaximumDays);
    }

    [Fact]
    public async Task Answers_a_shorter_range_from_the_cache_without_going_to_the_network()
    {
        GivenCached(AForecastFor(SanSalvador), freshFor: TimeSpan.FromMinutes(10));

        var result = await CreateSut().GetForecastAsync(SanSalvador, 5, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.Cache);
        result.Forecast.Days.Should().HaveCount(5);
        await _provider.DidNotReceiveWithAnyArgs().GetForecastAsync(default!, default);
    }

    /// <summary>
    /// A snapshot recorded three days ago still opens on the day it was taken.
    /// Handing back its first seven entries would serve three days that have
    /// already happened — a degraded answer that is worse than the outage.
    /// </summary>
    [Fact]
    public async Task Skips_the_days_a_stored_snapshot_has_already_outlived()
    {
        GivenNothingCached();
        GivenTheProviderIsDown();
        _history
            .GetLatestAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(AForecastFor(SanSalvador, ForecastHorizon.MaximumDays, startingDaysAgo: 3));

        var result = await CreateSut()
            .GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.Historical);
        result.Forecast.Days.Should().HaveCount(ForecastHorizon.DefaultDays);
        result.Forecast.Days[0]
            .Date.Should()
            .Be(DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-1));
    }

    /// <summary>
    /// Unreachable while the history's own usability window is narrower than the
    /// horizon, and kept anyway: an empty list rendered as a forecast is a
    /// confident answer about nothing, and a 503 is the honest reply.
    /// </summary>
    [Fact]
    public async Task Reports_the_forecast_as_unavailable_when_every_day_left_is_in_the_past()
    {
        GivenNothingCached();
        GivenTheProviderIsDown();
        _history
            .GetLatestAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(AForecastFor(SanSalvador, days: 7, startingDaysAgo: 30));

        var act = () =>
            CreateSut().GetForecastAsync(SanSalvador, ForecastHorizon.DefaultDays, CancellationToken.None);

        await act.Should().ThrowAsync<ForecastUnavailableException>();
    }

    // ----------------------------------------------------------------------- arrange

    private void GivenNothingCached() =>
        _cache.Get<ForecastSeries>(Arg.Any<string>()).Returns((CachedValue<ForecastSeries>?)null);

    private void GivenCached(ForecastSeries forecast, TimeSpan freshFor) =>
        _cache
            .Get<ForecastSeries>(Arg.Any<string>())
            .Returns(new CachedValue<ForecastSeries>(forecast, Now, Now.Add(freshFor)));

    private void GivenTheProviderIsDown() =>
        _provider
            .GetForecastAsync(Arg.Any<GeoLocation>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new WeatherProviderException("open-meteo", "circuit breaker is open"));

    private static ForecastSeries AForecastFor(GeoLocation location) =>
        AForecastFor(location, ForecastHorizon.MaximumDays);

    private static ForecastSeries AForecastFor(GeoLocation location, int days, int startingDaysAgo = 0) =>
        new(
            location,
            Enumerable
                .Range(-startingDaysAgo, days)
                .Select(offset => new DailyForecast(
                    DateOnly.FromDateTime(Now.UtcDateTime).AddDays(offset),
                    MinTemperatureC: 21.0 + offset,
                    MaxTemperatureC: 31.0 + offset,
                    Condition: WeatherCondition.PartlyCloudy))
                .ToArray(),
            RetrievedAt: Now);
}
