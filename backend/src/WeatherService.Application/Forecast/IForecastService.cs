using WeatherService.Application.Model;

namespace WeatherService.Application.Forecast;

public interface IForecastService
{
    Task<ForecastResult> GetWeeklyForecastAsync(GeoLocation location, CancellationToken cancellationToken);
}
