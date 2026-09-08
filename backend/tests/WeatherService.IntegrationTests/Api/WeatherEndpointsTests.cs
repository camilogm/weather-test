using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using WeatherService.Api.Contracts;

namespace WeatherService.IntegrationTests.Api;

/// <summary>
/// End-to-end over HTTP against the real application.
///
/// A fresh factory per test: the cache is a singleton, so sharing one fixture
/// would let a forecast cached by one test decide the outcome of the next.
/// </summary>
public sealed class WeatherEndpointsTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WeatherApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WeatherApiFactory();
        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ------------------------------------------------------------------- forecast

    [Fact]
    public async Task Forecast_defaults_to_San_Salvador_and_returns_a_full_week()
    {
        var response = await _client.GetAsync("/weather/forecast");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<WeeklyForecastResponse>(Json);
        body!.Location.Name.Should().Be("San Salvador");
        body.Days.Should().HaveCount(7);
        body.Days[0].Condition.Should().Be("PartlyCloudy");
        body.Days[0].Icon.Should().Be("partly-cloudy");
        body.Provenance.Source.Should().Be("Provider");
        body.Provenance.Degraded.Should().BeFalse();
    }

    [Fact]
    public async Task Forecast_announces_its_provenance_in_the_response_headers()
    {
        var response = await _client.GetAsync("/weather/forecast");

        response.Headers.GetValues("X-Weather-Data-Source").Single().Should().Be("Provider");
        response.Headers.GetValues("X-Weather-Degraded").Single().Should().Be("false");
        response.Headers.Should().ContainSingle(header => header.Key == "X-Weather-Retrieved-At");
    }

    [Fact]
    public async Task Forecast_accepts_another_city()
    {
        var response = await _client.GetAsync("/weather/forecast?city=Guatemala%20City");

        var body = await response.Content.ReadFromJsonAsync<WeeklyForecastResponse>(Json);
        body!.Location.Name.Should().Be("Guatemala City");
        body.Location.Latitude.Should().BeApproximately(14.6349, 0.0001);
    }

    [Fact]
    public async Task Forecast_accepts_explicit_coordinates()
    {
        var response = await _client.GetAsync("/weather/forecast?latitude=13.6929&longitude=-89.2182");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WeeklyForecastResponse>(Json);
        body!.Location.Latitude.Should().BeApproximately(13.6929, 0.0001);
    }

    [Fact]
    public async Task Forecast_serves_the_second_request_from_cache_without_calling_the_provider_again()
    {
        await _client.GetAsync("/weather/forecast");
        var response = await _client.GetAsync("/weather/forecast");

        response.Headers.GetValues("X-Weather-Data-Source").Single().Should().Be("Cache");
        _factory.Provider.ForecastCalls.Should().Be(1);
    }

    [Fact]
    public async Task Forecast_records_every_live_answer_in_the_database()
    {
        await _client.GetAsync("/weather/forecast");

        var database = await _factory.OpenDatabaseAsync();
        var stored = await database.Forecasts.Include(snapshot => snapshot.Days).SingleAsync();

        stored.LocationName.Should().Be("San Salvador");
        stored.Days.Should().HaveCount(7);
    }

    // ------------------------------------------------------------------ degrading

    [Fact]
    public async Task Forecast_serves_a_stale_reading_and_says_so_when_the_provider_goes_down()
    {
        await _client.GetAsync("/weather/forecast"); // warms the cache
        _factory.Clock.Advance(TimeSpan.FromMinutes(30)); // the entry is kept, not fresh
        _factory.Provider.IsDown = true;

        var response = await _client.GetAsync("/weather/forecast");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Weather-Data-Source").Single().Should().Be("StaleCache");
        response.Headers.GetValues("X-Weather-Degraded").Single().Should().Be("true");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = await response.Content.ReadFromJsonAsync<WeeklyForecastResponse>(Json);
        body!.Provenance.Degraded.Should().BeTrue();
    }

    [Fact]
    public async Task Forecast_falls_back_to_stored_history_when_the_cache_is_gone_too()
    {
        await _client.GetAsync("/weather/forecast"); // writes a snapshot to the database

        // A brand new process: same database, empty cache.
        using var restarted = _factory.CreateClient();
        _factory.Clock.Advance(TimeSpan.FromHours(48)); // past the cache retention window
        _factory.Provider.IsDown = true;

        var response = await restarted.GetAsync("/weather/forecast");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Weather-Data-Source").Single().Should().Be("Historical");
    }

    [Fact]
    public async Task Forecast_answers_503_with_a_retry_hint_when_nothing_is_left()
    {
        _factory.Provider.IsDown = true;

        var response = await _client.GetAsync("/weather/forecast");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull();

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Contain("temporarily unavailable");
        problem.GetProperty("status").GetInt32().Should().Be(503);
    }

    // ----------------------------------------------------------------- bad input

    [Fact]
    public async Task Unknown_city_is_a_404_and_not_a_500()
    {
        var response = await _client.GetAsync("/weather/forecast?city=Atlantis");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("title").GetString().Should().Be("Location not found");
    }

    [Fact]
    public async Task Half_a_coordinate_pair_is_rejected_before_any_work_happens()
    {
        var response = await _client.GetAsync("/weather/forecast?latitude=13.6929");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Provider.ForecastCalls.Should().Be(0);
    }

    [Fact]
    public async Task Coordinates_outside_the_globe_are_rejected()
    {
        var response = await _client.GetAsync("/weather/forecast?latitude=910&longitude=-89.2182");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------- current

    [Fact]
    public async Task Current_returns_conditions_for_the_default_city()
    {
        var response = await _client.GetAsync("/weather/current");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CurrentWeatherResponse>(Json);
        body!.Location.Name.Should().Be("San Salvador");
        body.TemperatureC.Should().Be(28.5);
        body.RelativeHumidityPercent.Should().Be(74);
        body.Icon.Should().Be("partly-cloudy");
        body.Provenance.Source.Should().Be("Provider");
    }

    [Fact]
    public async Task Current_answers_503_when_the_provider_is_down_and_nothing_is_cached()
    {
        _factory.Provider.IsDown = true;

        var response = await _client.GetAsync("/weather/current");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Health_endpoint_answers()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
