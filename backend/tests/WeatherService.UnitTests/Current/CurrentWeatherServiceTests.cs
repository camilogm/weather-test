using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WeatherService.Application.Current;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.UnitTests.Current;

public class CurrentWeatherServiceTests
{
    private static readonly GeoLocation SanSalvador = new("San Salvador", 13.6929, -89.2182);
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly IWeatherProvider _provider = Substitute.For<IWeatherProvider>();
    private readonly IWeatherCache _cache = Substitute.For<IWeatherCache>();
    private readonly FakeTimeProvider _clock = new(Now);

    public CurrentWeatherServiceTests() => _provider.Name.Returns("open-meteo");

    private CurrentWeatherService CreateSut() =>
        new(_provider, _cache, _clock, NullLogger<CurrentWeatherService>.Instance);

    [Fact]
    public async Task Queries_the_provider_when_the_cache_is_empty()
    {
        GivenNothingCached();
        _provider
            .GetCurrentWeatherAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(AReadingFor(SanSalvador));

        var result = await CreateSut().GetCurrentWeatherAsync(SanSalvador, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.Provider);
        result.Weather.TemperatureC.Should().Be(28.5);
    }

    [Fact]
    public async Task Serves_the_cache_without_touching_the_provider_while_it_is_fresh()
    {
        GivenCached(AReadingFor(SanSalvador), freshFor: TimeSpan.FromMinutes(10));

        var result = await CreateSut().GetCurrentWeatherAsync(SanSalvador, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.Cache);
        await _provider.DidNotReceiveWithAnyArgs().GetCurrentWeatherAsync(default!, default);
    }

    [Fact]
    public async Task Caches_a_live_reading_so_the_next_call_is_free()
    {
        GivenNothingCached();
        var reading = AReadingFor(SanSalvador);
        _provider
            .GetCurrentWeatherAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(reading);

        await CreateSut().GetCurrentWeatherAsync(SanSalvador, CancellationToken.None);

        _cache
            .Received(1)
            .Set("weather:current:13.6929,-89.2182", reading, Arg.Any<TimeSpan>(), Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task Falls_back_to_the_stale_cache_when_the_provider_fails()
    {
        var stale = AReadingFor(SanSalvador);
        GivenCached(stale, freshFor: TimeSpan.FromMinutes(10));
        _clock.Advance(TimeSpan.FromMinutes(30));
        GivenTheProviderIsDown();

        var result = await CreateSut().GetCurrentWeatherAsync(SanSalvador, CancellationToken.None);

        result.Source.Should().Be(WeatherDataSource.StaleCache);
        result.IsDegraded.Should().BeTrue();
        result.Weather.Should().BeSameAs(stale);
    }

    [Fact]
    public async Task Reports_current_weather_as_unavailable_when_nothing_is_left()
    {
        GivenNothingCached();
        GivenTheProviderIsDown();

        var act = () => CreateSut().GetCurrentWeatherAsync(SanSalvador, CancellationToken.None);

        await act.Should()
            .ThrowAsync<CurrentWeatherUnavailableException>()
            .Where(e => e.Location == SanSalvador);
    }

    private void GivenNothingCached() =>
        _cache.Get<CurrentWeather>(Arg.Any<string>()).Returns((CachedValue<CurrentWeather>?)null);

    private void GivenCached(CurrentWeather reading, TimeSpan freshFor) =>
        _cache
            .Get<CurrentWeather>(Arg.Any<string>())
            .Returns(new CachedValue<CurrentWeather>(reading, Now, Now.Add(freshFor)));

    private void GivenTheProviderIsDown() =>
        _provider
            .GetCurrentWeatherAsync(Arg.Any<GeoLocation>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new WeatherProviderException("open-meteo", "circuit breaker is open"));

    private static CurrentWeather AReadingFor(GeoLocation location) =>
        new(location, 28.5, 32.1, 11.2, 74, WeatherCondition.PartlyCloudy, Now);
}
