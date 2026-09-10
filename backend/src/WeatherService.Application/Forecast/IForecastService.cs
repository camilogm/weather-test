using WeatherService.Application.Model;

namespace WeatherService.Application.Forecast;

public interface IForecastService
{
    /// <summary>
    /// The next <paramref name="days"/> days for a location.
    ///
    /// The range is a projection, not a request: whatever is asked for, the
    /// service works from the one horizon it fetches, caches and stores. Callers
    /// are expected to have already bounded the value with
    /// <see cref="ForecastHorizon"/>; anything wider simply yields what there is.
    /// </summary>
    Task<ForecastResult> GetForecastAsync(
        GeoLocation location,
        int days,
        CancellationToken cancellationToken);
}
