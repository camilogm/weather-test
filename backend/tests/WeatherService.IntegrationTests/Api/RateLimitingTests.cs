using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace WeatherService.IntegrationTests.Api;

/// <summary>
/// The service proxies a third party that has a quota, from endpoints that take
/// no credentials. That combination is what makes rate limiting a correctness
/// concern here rather than a nicety: without it a single caller in a loop
/// spends someone else's allowance.
///
/// Limits are turned down to single digits for these tests. The point is the
/// behaviour at the edge, not the production numbers.
/// </summary>
public sealed class RateLimitingTests : IAsyncLifetime
{
    private const int ForecastPermits = 2;
    private const int SearchPermits = 3;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WeatherApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WeatherApiFactory();

        // Set before the host is built: InitializeAsync resolves services, and
        // the limiter reads its configuration on the way up.
        _factory.RateLimits.Enabled = true;
        _factory.RateLimits.Window = TimeSpan.FromMinutes(5);
        _factory.RateLimits.WeatherPermits = ForecastPermits;
        _factory.RateLimits.SearchPermits = SearchPermits;

        await _factory.InitializeAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Serves_every_request_up_to_the_limit()
    {
        for (var attempt = 0; attempt < ForecastPermits; attempt++)
        {
            var allowed = await _client.GetAsync("/weather/forecast");

            allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Answers_429_once_the_budget_is_spent()
    {
        await SpendAsync("/weather/forecast", ForecastPermits);

        var rejected = await _client.GetAsync("/weather/forecast");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Tells_a_throttled_caller_when_to_come_back()
    {
        await SpendAsync("/weather/forecast", ForecastPermits);

        var rejected = await _client.GetAsync("/weather/forecast");

        // Same courtesy the 503 path already extends: a refusal without a
        // Retry-After invites the caller to retry immediately and make it worse.
        rejected.Headers.RetryAfter.Should().NotBeNull();
        rejected.Headers.RetryAfter!.Delta.Should().BePositive();
    }

    [Fact]
    public async Task Rejects_with_a_problem_document_like_every_other_error()
    {
        await SpendAsync("/weather/forecast", ForecastPermits);

        var rejected = await _client.GetAsync("/weather/forecast");

        rejected.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>(Json);
        problem!.Status.Should().Be((int)HttpStatusCode.TooManyRequests);
        problem.Title.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Keeps_search_and_forecast_on_separate_budgets()
    {
        // The city picker fires on a debounced keystroke while a forecast is one
        // call per chosen city. Sizing them together would either throttle
        // ordinary typing or leave the expensive endpoint wide open.
        await SpendAsync("/weather/forecast", ForecastPermits);

        var search = await _client.GetAsync("/locations?query=San");

        search.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Never_throttles_the_health_check()
    {
        // The container's health check hits this on a timer. Throttling it would
        // make a perfectly healthy service flap.
        for (var attempt = 0; attempt < SearchPermits + ForecastPermits + 2; attempt++)
        {
            var health = await _client.GetAsync("/health");

            health.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private async Task SpendAsync(string path, int permits)
    {
        for (var attempt = 0; attempt < permits; attempt++)
        {
            using var response = await _client.GetAsync(path);

            response.StatusCode.Should().Be(HttpStatusCode.OK, "the budget should not be spent yet");
        }
    }
}
