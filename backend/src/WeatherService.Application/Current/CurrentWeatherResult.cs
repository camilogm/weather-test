using WeatherService.Application.Model;

namespace WeatherService.Application.Current;

/// <summary>Current conditions plus the provenance of the data behind them.</summary>
public sealed record CurrentWeatherResult(CurrentWeather Weather, WeatherDataSource Source)
{
    public bool IsDegraded => Source is WeatherDataSource.StaleCache or WeatherDataSource.Historical;
}

public interface ICurrentWeatherService
{
    Task<CurrentWeatherResult> GetCurrentWeatherAsync(
        GeoLocation location,
        CancellationToken cancellationToken);
}

/// <summary>
/// Nothing is left to serve for current conditions. Maps to 503: the service is
/// fine, its upstream is not, and a retry may well succeed.
/// </summary>
public sealed class CurrentWeatherUnavailableException : Exception
{
    public CurrentWeatherUnavailableException(GeoLocation location, Exception? innerException = null)
        : base($"No current weather could be produced for {location.Name}.", innerException) =>
        Location = location;

    public GeoLocation Location { get; }
}
