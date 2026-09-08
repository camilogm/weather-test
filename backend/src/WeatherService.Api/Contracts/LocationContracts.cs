using System.ComponentModel.DataAnnotations;
using WeatherService.Application.Model;

namespace WeatherService.Api.Contracts;

public sealed class LocationSearchQuery
{
    /// <summary>Whatever the person has typed so far.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(80, MinimumLength = 1)]
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Capped low on purpose. A suggestion list is scanned with the eye, and a
    /// list of forty is not scanned at all.
    /// </summary>
    [Range(1, 10)]
    public int Limit { get; init; } = 8;
}

/// <summary>
/// One candidate place. Region and country are what let a person tell San
/// Salvador in El Salvador from San Salvador de Jujuy, so they are part of the
/// contract rather than a nicety.
/// </summary>
public sealed record LocationSuggestionResponse(
    string Name,
    string? Region,
    string? Country,
    string? CountryCode,
    double Latitude,
    double Longitude);

public sealed record LocationSearchResponse(IReadOnlyList<LocationSuggestionResponse> Results);

public static class LocationContractMapper
{
    public static LocationSuggestionResponse ToResponse(this LocationMatch match) =>
        new(
            match.Name,
            match.Region,
            match.Country,
            match.CountryCode,
            match.Latitude,
            match.Longitude);
}
