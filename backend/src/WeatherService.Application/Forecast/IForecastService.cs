using WeatherService.Application.Model;

namespace WeatherService.Application.Forecast;

public interface IForecastService
{
    Task<ForecastResult> GetForecastAsync(GeoLocation location, CancellationToken cancellationToken);
}
