using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Polly;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Infrastructure.Providers.OpenMeteo;

/// <summary>
/// Resolves and searches places through Open-Meteo's geocoding API.
///
/// Note there is no "list every city" endpoint anywhere, and there could not
/// reasonably be one — which is why the interface offers a search box backed by
/// this, rather than a fixed dropdown of whichever cities a developer happened
/// to hardcode.
///
/// A short built-in catalogue is consulted first for exact resolution. That is
/// not premature optimisation: San Salvador is the city this service exists for,
/// and its default request must not depend on a second network call that can
/// fail on its own.
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

    /// <summary>Below this, a query matches too much to be worth asking about.</summary>
    public const int MinimumQueryLength = 2;

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

        List<LocationMatch> matches;

        try
        {
            matches = await SearchOrCachedAsync(query, limit: 1, cancellationToken);
        }
        catch (WeatherProviderException failure)
        {
            // From the caller's seat, a geocoder that is down and a city that
            // does not exist are the same answer: we cannot serve this location.
            throw new UnknownLocationException(query, failure);
        }

        return matches.Count > 0
            ? matches[0].ToGeoLocation()
            : throw new UnknownLocationException(query);
    }

    public async Task<IReadOnlyList<LocationMatch>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var trimmed = query.Trim();

        if (trimmed.Length < MinimumQueryLength)
        {
            return [];
        }

        // Deliberately NOT swallowing a provider failure into an empty list:
        // "we could not search" and "there is nothing named that" are different
        // answers, and showing the second when the first is true is a lie the
        // person typing cannot detect.
        return await SearchOrCachedAsync(trimmed, limit, cancellationToken);
    }

    private async Task<List<LocationMatch>> SearchOrCachedAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"locations:{limit}:{query.ToLowerInvariant()}";

        var cached = _cache.Get<List<LocationMatch>>(cacheKey);
        if (cached is not null)
        {
            return cached.Value;
        }

        var matches = await FetchAsync(query, limit, cancellationToken);

        // Cities do not move, so a hit is good for a day. Misses are not cached:
        // a query that matches nothing today is usually a half-typed word that
        // will match something two keystrokes later.
        if (matches.Count > 0)
        {
            _cache.Set(cacheKey, matches, CacheFor, CacheFor);
        }

        return matches;
    }

    private async Task<List<LocationMatch>> FetchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var url =
                $"v1/search?name={Uri.EscapeDataString(query)}"
                + $"&count={limit}&language=es&format=json";

            using var response = await _client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new WeatherProviderException(
                    "open-meteo-geocoding",
                    $"Geocoding answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<GeocodingResponse>(
                JsonOptions,
                cancellationToken);

            // Open-Meteo omits "results" entirely when nothing matches, which is
            // a successful empty answer rather than a malformed one.
            return
            [
                .. (payload?.Results ?? []).Select(match => new LocationMatch(
                    match.Name,
                    match.Admin1,
                    match.Country,
                    match.CountryCode,
                    match.Latitude,
                    match.Longitude)),
            ];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WeatherProviderException)
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

            throw new WeatherProviderException(
                "open-meteo-geocoding",
                $"Geocoding could not be reached: {failure.Message}",
                failure);
        }
    }

    private sealed record GeocodingResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<GeocodingMatch>? Results);

    private sealed record GeocodingMatch(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("country_code")] string? CountryCode,
        [property: JsonPropertyName("admin1")] string? Admin1);
}
