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

    /// <summary>
    /// Open-Meteo pads the tail of the horizon with nulls whenever the location's
    /// local calendar runs past the model's data window — a live payload for
    /// Atlantis, ZA came back with `weather_code[15]`, `temperature_2m_max[15]`
    /// and `temperature_2m_min[15]` all null while the first fifteen days were
    /// perfectly good. Whether it happens depends on the location's UTC offset
    /// and the hour you ask, so it is not reproducible on demand and is very much
    /// real: London and Nairobi were both answering 503 while San Salvador,
    /// Madrid and Tokyo answered 200.
    ///
    /// The columns still agree in length, so nothing can be misaligned by it. A
    /// day carrying no code and no temperatures carries no information either, so
    /// it is dropped and the rest is served. Throwing away fifteen good days to
    /// protest about an empty sixteenth is the opposite of robust.
    /// </summary>
    [Fact]
    public async Task Drops_a_day_whose_columns_came_back_null_and_serves_the_rest()
    {
        var paddedTail = """
            {
              "daily": {
                "time": ["2026-09-07","2026-09-08","2026-09-09"],
                "weather_code": [0, 2, null],
                "temperature_2m_max": [31.2, 32.0, null],
                "temperature_2m_min": [21.4, 21.9, null]
              }
            }
            """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, paddedTail);

        var forecast = await CreateSut(handler).GetForecastAsync(SanSalvador, CancellationToken.None);

        forecast.Days.Should().HaveCount(2);
        forecast.Days.Select(day => day.Date)
            .Should()
            .Equal(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 8));
    }

    /// <summary>
    /// A gap is dropped on its own terms rather than truncating everything after
    /// it. Each day carries its own date, so the series stays correct with a hole
    /// in it — and the days past the hole are still a forecast someone wants.
    /// </summary>
    [Fact]
    public async Task Drops_only_the_incomplete_day_when_the_gap_is_in_the_middle()
    {
        var gapped = """
            {
              "daily": {
                "time": ["2026-09-07","2026-09-08","2026-09-09"],
                "weather_code": [0, null, 3],
                "temperature_2m_max": [31.2, 32.0, 30.4],
                "temperature_2m_min": [21.4, 21.9, 22.0]
              }
            }
            """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, gapped);

        var forecast = await CreateSut(handler).GetForecastAsync(SanSalvador, CancellationToken.None);

        forecast.Days.Select(day => day.Date)
            .Should()
            .Equal(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 9));
    }

    /// <summary>
    /// No usable day at all is an unusable answer, and it has to leave here as a
    /// provider failure rather than as an empty series. An empty series would be
    /// cached for ten minutes and written to history as a snapshot, poisoning the
    /// two fallbacks that exist precisely for this moment; a provider failure
    /// sends the use case down the degradation chain instead.
    /// </summary>
    [Fact]
    public async Task Reports_a_provider_failure_when_no_day_survives()
    {
        var empty = """
            {
              "daily": {
                "time": ["2026-09-07","2026-09-08"],
                "weather_code": [null, null],
                "temperature_2m_max": [null, null],
                "temperature_2m_min": [null, null]
              }
            }
            """;
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.OK, empty);

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
