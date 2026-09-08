using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using WeatherService.Api.Contracts;

namespace WeatherService.IntegrationTests.Api;

/// <summary>
/// The city picker's backing endpoint. It exists so the browser never talks to
/// the geocoding provider directly, which would bypass the circuit breaker and
/// the cache this service already owns.
/// </summary>
public sealed class LocationEndpointsTests : IAsyncLifetime
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

    [Fact]
    public async Task Returns_candidates_for_a_partial_name()
    {
        var response = await _client.GetAsync("/locations?query=San");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LocationSearchResponse>(Json);
        body!.Results.Should().HaveCountGreaterThan(1);
        body.Results.Should().OnlyContain(match => match.Name.Contains("San"));
    }

    [Fact]
    public async Task Carries_the_context_that_makes_two_namesakes_distinguishable()
    {
        // This is the whole reason the endpoint exists: "San Salvador" alone
        // cannot be chosen with confidence, "San Salvador, El Salvador" can.
        var response = await _client.GetAsync("/locations?query=San%20Salvador");

        var body = await response.Content.ReadFromJsonAsync<LocationSearchResponse>(Json);

        body!.Results.Should().Contain(match => match.Country == "El Salvador");
        body.Results.Should().Contain(match => match.Country == "Argentina");
        body.Results.Should().OnlyContain(match => match.Region != null && match.Country != null);
    }

    [Fact]
    public async Task Every_candidate_carries_coordinates_so_no_second_lookup_is_needed()
    {
        var response = await _client.GetAsync("/locations?query=Guatemala");

        var body = await response.Content.ReadFromJsonAsync<LocationSearchResponse>(Json);
        var match = body!.Results.Single();

        match.Latitude.Should().BeApproximately(14.6349, 0.0001);
        match.Longitude.Should().BeApproximately(-90.5069, 0.0001);
    }

    [Fact]
    public async Task Honours_the_requested_limit()
    {
        var response = await _client.GetAsync("/locations?query=San&limit=1");

        var body = await response.Content.ReadFromJsonAsync<LocationSearchResponse>(Json);
        body!.Results.Should().HaveCount(1);
    }

    [Fact]
    public async Task Answers_an_empty_list_rather_than_404_when_nothing_matches()
    {
        // No suggestions is a normal answer to a half-typed word, not a failure.
        var response = await _client.GetAsync("/locations?query=Xqzzyvwtplk");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LocationSearchResponse>(Json);
        body!.Results.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/locations")]
    [InlineData("/locations?query=")]
    [InlineData("/locations?query=San&limit=0")]
    [InlineData("/locations?query=San&limit=99")]
    public async Task Rejects_a_malformed_query(string url)
    {
        var response = await _client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reports_503_when_the_geocoder_is_down()
    {
        // "We could not search" and "there is nothing named that" are different
        // answers, and an empty list would be the wrong one to give here.
        _factory.Locations.IsDown = true;

        var response = await _client.GetAsync("/locations?query=San");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
