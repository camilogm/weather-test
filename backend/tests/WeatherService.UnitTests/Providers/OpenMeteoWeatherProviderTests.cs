using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WeatherService.Application.Forecast;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;
using WeatherService.Infrastructure.Providers.OpenMeteo;
using WeatherService.UnitTests.TestDoubles;

namespace WeatherService.UnitTests.Providers;

/// <summary>
/// The adapter has two jobs and these tests hold it to both:
///
/// 1. Translate Open-Meteo's columnar payload and WMO codes into the domain.
/// 2. Translate every possible transport failure into WeatherProviderException,
///    so nothing above this layer ever sees HttpRequestException or JsonException.
/// </summary>
public class OpenMeteoWeatherProviderTests
{
    private static readonly GeoLocation SanSalvador = new("San Salvador", 13.6929, -89.2182);

    private const string ValidForecastPayload = """
        {
          "latitude": 13.6875,
          "longitude": -89.25,
          "timezone": "America/El_Salvador",
          "daily": {
            "time": ["2026-09-07","2026-09-08","2026-09-09","2026-09-10","2026-09-11","2026-09-12","2026-09-13"],
            "weather_code": [0, 2, 3, 61, 95, 45, 80],
            "temperature_2m_max": [31.2, 32.0, 30.4, 28.9, 27.5, 30.1, 29.8],
            "temperature_2m_min": [21.4, 21.9, 22.0, 21.1, 20.8, 21.5, 21.2]
          }
        }
        """;

    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static OpenMeteoWeatherProvider CreateSut(StubHttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.open-meteo.test/") },
            new FakeTimeProvider(Now),
            NullLogger<OpenMeteoWeatherProvider>.Instance);

    // ------------------------------------------------------------------- mapping

    [Fact]
    public async Task Maps_every_column_entry_into_a_domain_day()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, ValidForecastPayload);

        var forecast = await CreateSut(handler)
            .GetForecastAsync(SanSalvador, CancellationToken.None);

        forecast.Days.Should().HaveCount(7);
        forecast.Location.Name.Should().Be("San Salvador");
        forecast.Days[0].Date.Should().Be(new DateOnly(2026, 9, 7));
        forecast.Days[0].MaxTemperatureC.Should().Be(31.2);
        forecast.Days[0].MinTemperatureC.Should().Be(21.4);
        forecast.Days[6].Date.Should().Be(new DateOnly(2026, 9, 13));
        forecast.RetrievedAt.Should().Be(Now);
    }

    /// <summary>
    /// The adapter always asks for the whole horizon, never for the range a
    /// caller happened to want. Trimming is a projection done above this layer,
    /// which is what keeps one cache entry and one stored snapshot per location
    /// no matter how many different ranges get requested.
    /// </summary>
    [Fact]
    public async Task Asks_the_provider_for_the_whole_horizon_at_the_requested_coordinates()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, ValidForecastPayload);

        await CreateSut(handler).GetForecastAsync(SanSalvador, CancellationToken.None);

        var query = handler.Requests.Single().RequestUri!.Query;
        query.Should().Contain("latitude=13.6929");
        query.Should().Contain("longitude=-89.2182");
        query.Should().Contain($"forecast_days={ForecastHorizon.MaximumDays}");
    }

    /// <summary>
    /// Open-Meteo answers "Allowed range 0 to 16" to anything larger, so a
    /// horizon above that ceiling would make every single forecast call fail.
    /// </summary>
    [Fact]
    public void Never_asks_for_more_days_than_open_meteo_serves()
    {
        ForecastHorizon.MaximumDays.Should().BeLessThanOrEqualTo(16);
    }

    [Theory]
    [InlineData(0, WeatherCondition.Clear)]
    [InlineData(1, WeatherCondition.MainlyClear)]
    [InlineData(2, WeatherCondition.PartlyCloudy)]
    [InlineData(3, WeatherCondition.Overcast)]
    [InlineData(45, WeatherCondition.Fog)]
    [InlineData(48, WeatherCondition.Fog)]
    [InlineData(53, WeatherCondition.Drizzle)]
    [InlineData(57, WeatherCondition.FreezingRain)]
    [InlineData(63, WeatherCondition.Rain)]
    [InlineData(67, WeatherCondition.FreezingRain)]
    [InlineData(73, WeatherCondition.Snow)]
    [InlineData(81, WeatherCondition.RainShowers)]
    [InlineData(86, WeatherCondition.SnowShowers)]
    [InlineData(95, WeatherCondition.Thunderstorm)]
    [InlineData(99, WeatherCondition.Thunderstorm)]
    [InlineData(7777, WeatherCondition.Unknown)]
    public void Translates_wmo_codes_into_domain_conditions(int wmoCode, WeatherCondition expected)
    {
        WmoWeatherCode.ToCondition(wmoCode).Should().Be(expected);
    }

    // ------------------------------------------------------------------ failures

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Reports_a_provider_failure_when_the_response_is_not_successful(HttpStatusCode status)
    {
        var handler = StubHttpMessageHandler.Returning(status, "upstream is unhappy");

        var act = () => CreateSut(handler).GetForecastAsync(SanSalvador, CancellationToken.None);

        await act.Should()
            .ThrowAsync<WeatherProviderException>()
            .Where(e => e.ProviderName == "open-meteo");
    }

    [Fact]
    public async Task Reports_a_provider_failure_when_the_payload_is_not_json()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, "<html>maintenance</html>");

        var act = () => CreateSut(handler).GetForecastAsync(SanSalvador, CancellationToken.None);

        await act.Should().ThrowAsync<WeatherProviderException>();
    }

    [Fact]
    public async Task Reports_a_provider_failure_when_the_daily_block_is_missing()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, """{"latitude":13.7}""");

        var act = () => CreateSut(handler).GetForecastAsync(SanSalvador, CancellationToken.None);

        await act.Should().ThrowAsync<WeatherProviderException>();
    }

    [Fact]
    public async Task Reports_a_provider_failure_when_the_parallel_arrays_disagree_in_length()
    {
        // Open-Meteo answers in columns, not rows. If one column comes back short,
        // zipping them blindly would silently pair the wrong temperature with the
        // wrong day — a corrupt forecast is worse than no forecast.
        var truncated = """
            {
              "daily": {
                "time": ["2026-09-07","2026-09-08","2026-09-09"],
                "weather_code": [0, 2, 3],
                "temperature_2m_max": [31.2, 32.0],
                "temperature_2m_min": [21.4, 21.9, 22.0]
              }
            }
            """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, truncated);

        var act = () => CreateSut(handler).GetForecastAsync(SanSalvador, CancellationToken.None);

        await act.Should().ThrowAsync<WeatherProviderException>();
    }

    [Fact]
    public async Task Reports_a_provider_failure_when_the_transport_breaks()
    {
        var handler = StubHttpMessageHandler.Throwing(new HttpRequestException("connection reset"));

        var act = () => CreateSut(handler).GetForecastAsync(SanSalvador, CancellationToken.None);

        await act.Should().ThrowAsync<WeatherProviderException>();
    }

    [Fact]
    public async Task Lets_caller_cancellation_through_untouched()
    {
        // A caller walking away is not a provider outage, and must not be
        // recorded as one — otherwise a cancelled request would help trip the
        // circuit breaker against a service that is perfectly healthy.
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, ValidForecastPayload);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => CreateSut(handler).GetForecastAsync(SanSalvador, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
