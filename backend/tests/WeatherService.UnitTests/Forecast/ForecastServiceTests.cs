using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using WeatherService.Application.Forecast;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.UnitTests.Forecast;

/// <summary>
/// Drives the design of the forecast use case.
/// The service owns the degradation chain; the adapters own the I/O.
/// </summary>
public class ForecastServiceTests
{
    private static readonly GeoLocation SanSalvador = new("San Salvador", 13.6929, -89.2182);
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly IWeatherProvider _provider = Substitute.For<IWeatherProvider>();
    private readonly IWeatherCache _cache = Substitute.For<IWeatherCache>();
    private readonly FakeTimeProvider _clock = new(Now);

    private ForecastService CreateSut() =>
        new(_provider, _cache, _clock, NullLogger<ForecastService>.Instance);

    [Fact]
    public async Task Queries_the_provider_when_the_cache_is_empty()
    {
        _cache.Get<WeeklyForecast>(Arg.Any<string>()).Returns((CachedValue<WeeklyForecast>?)null);
        _provider
            .GetWeeklyForecastAsync(SanSalvador, Arg.Any<CancellationToken>())
            .Returns(AForecastFor(SanSalvador));

        var result = await CreateSut().GetWeeklyForecastAsync(SanSalvador, CancellationToken.None);

        result.Source.Should().Be(ForecastSource.Provider);
        result.Forecast.Days.Should().HaveCount(7);
        result.Forecast.Location.Name.Should().Be("San Salvador");
    }

    private static WeeklyForecast AForecastFor(GeoLocation location) =>
        new(
            location,
            Enumerable
                .Range(0, 7)
                .Select(offset => new DailyForecast(
                    DateOnly.FromDateTime(Now.UtcDateTime).AddDays(offset),
                    MinTemperatureC: 21.0 + offset,
                    MaxTemperatureC: 31.0 + offset,
                    Condition: WeatherCondition.PartlyCloudy))
                .ToArray(),
            RetrievedAt: Now);
}
