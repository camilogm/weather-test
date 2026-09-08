using WeatherService.Application.Model;

namespace WeatherService.Application.Forecast;

/// <summary>
/// Every source in the degradation chain has been tried and none could answer.
/// This is the one honest outcome left, and it maps to 503 rather than 500:
/// the service is fine, its upstream is not, and retrying later may well work.
/// </summary>
public sealed class ForecastUnavailableException : Exception
{
    public ForecastUnavailableException(GeoLocation location, Exception? innerException = null)
        : base($"No forecast could be produced for {location.Name} from any source.", innerException)
    {
        Location = location;
    }

    public GeoLocation Location { get; }
}
