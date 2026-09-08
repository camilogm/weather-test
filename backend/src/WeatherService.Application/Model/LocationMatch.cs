namespace WeatherService.Application.Model;

/// <summary>
/// A candidate returned by a location search, carrying enough context for a
/// person to tell two places with the same name apart.
///
/// That context is the entire point: "San Salvador" alone is ambiguous between
/// El Salvador's capital, San Salvador de Jujuy and a handful of others. A name
/// on its own makes the caller guess; a name with its region and country does
/// not.
/// </summary>
public sealed record LocationMatch(
    string Name,
    string? Region,
    string? Country,
    string? CountryCode,
    double Latitude,
    double Longitude)
{
    public GeoLocation ToGeoLocation() => new(Name, Latitude, Longitude);
}
