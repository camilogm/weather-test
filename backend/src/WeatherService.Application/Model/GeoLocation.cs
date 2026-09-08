using System.Globalization;

namespace WeatherService.Application.Model;

/// <summary>
/// A point on the map the forecast is requested for.
/// </summary>
public sealed record GeoLocation(string Name, double Latitude, double Longitude)
{
    /// <summary>The city this service is built around; used as the default target.</summary>
    public static GeoLocation SanSalvador { get; } = new("San Salvador", 13.6929, -89.2182);

    /// <summary>
    /// Canonical identity of the point, used as both the cache key and the
    /// persistence lookup key so the two can never drift apart.
    ///
    /// Rounded to four decimals — roughly 11 metres, far below any weather grid
    /// resolution — so near-identical requests collapse onto one entry. Formatted
    /// with the invariant culture on purpose: under a culture that writes decimals
    /// with a comma the key would change shape and silently miss every lookup.
    /// </summary>
    public string Key { get; } =
        string.Concat(
            Latitude.ToString("F4", CultureInfo.InvariantCulture),
            ",",
            Longitude.ToString("F4", CultureInfo.InvariantCulture));
}
