namespace WeatherService.Application.Model;

/// <summary>
/// A point on the map the forecast is requested for.
/// </summary>
public sealed record GeoLocation(string Name, double Latitude, double Longitude)
{
    /// <summary>The city this service is built around; used as the default target.</summary>
    public static GeoLocation SanSalvador { get; } = new("San Salvador", 13.6929, -89.2182);
}
