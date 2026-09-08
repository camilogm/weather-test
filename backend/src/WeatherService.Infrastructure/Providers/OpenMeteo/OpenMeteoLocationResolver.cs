using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Polly;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Infrastructure.Providers.OpenMeteo;

/// <summary>
/// Resolves a city name to coordinates through Open-Meteo's geocoding API.
///
/// A short built-in catalogue is consulted first. That is not premature
/// optimisation: San Salvador is the city this service exists for and its
/// default request must not depend on a second network call that can fail on
/// its own. Everything else goes to the network and is cached for a day, since
/// cities do not move.
/// </summary>
public sealed class OpenMeteoLocationResolver : ILocationResolver
{
    /// <summary>Places we refuse to be unable to answer for.</summary>
    private static readonly IReadOnlyDictionary<string, GeoLocation> KnownCities =
        new Dictionary<string, GeoLocation>(StringComparer.OrdinalIgnoreCase)
        {
            ["san salvador"] = GeoLocation.SanSalvador,
            ["santa ana"] = new("Santa Ana", 13.9942, -89.5597),
            ["san miguel"] = new("San Miguel", 13.4833, -88.1833),
            ["guatemala city"] = new("Guatemala City", 14.6349, -90.5069),
            ["tegucigalpa"] = new("Tegucigalpa", 14.0723, -87.1921),
            ["managua"] = new("Managua", 12.1364, -86.2514),
            ["san jose"] = new("San José", 9.9281, -84.0907),
            ["panama city"] = new("Panama City", 8.9824, -79.5199),
        };

    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly IWeatherCache _cache;
    private readonly ILogger<OpenMeteoLocationResolver> _logger;

    public OpenMeteoLocationResolver(
        HttpClient client,
        IWeatherCache cache,
        ILogger<OpenMeteoLocationResolver> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
    }

    public async Task<GeoLocation> ResolveAsync(string city, CancellationToken cancellationToken)
    {
        var query = city.Trim();
        if (query.Length == 0)
        {
            throw new UnknownLocationException(city);
        }

        if (KnownCities.TryGetValue(query, out var known))
        {
            return known;
        }

        var cacheKey = $"location:{query.ToLowerInvariant()}";
        var cached = _cache.Get<GeoLocation>(cacheKey);
        if (cached is not null)
        {
            return cached.Value;
        }

        var resolved = await SearchAsync(query, cancellationToken);
        _cache.Set(cacheKey, resolved, CacheFor, CacheFor);

        return resolved;
    }

    private async Task<GeoLocation> SearchAsync(string query, CancellationToken cancellationToken)
    {
        GeocodingResponse? payload;

        try
        {
            var url = $"v1/search?name={Uri.EscapeDataString(query)}&count=1&language=en&format=json";
            using var response = await _client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new UnknownLocationException(query);
            }

            payload = await response.Content.ReadFromJsonAsync<GeocodingResponse>(
                JsonOptions,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (failure
            is HttpRequestException
            or JsonException
            or NotSupportedException
            or OperationCanceledException
            or ExecutionRejectedException)
        {
            _logger.LogWarning(failure, "Geocoding lookup for {Query} failed", query);

            // The geocoder being down is indistinguishable, from the caller's
            // seat, from the city not existing. Both mean "we cannot serve this
            // location", and neither is a server fault worth a 500.
            throw new UnknownLocationException(query, failure);
        }

        var match = payload?.Results?.FirstOrDefault() ?? throw new UnknownLocationException(query);

        return new GeoLocation(match.Name, match.Latitude, match.Longitude);
    }

    private sealed record GeocodingResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<GeocodingMatch>? Results);

    private sealed record GeocodingMatch(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude);
}
