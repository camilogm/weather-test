using WeatherService.Application.Model;

namespace WeatherService.Application.Ports;

/// <summary>
/// The port to persisted forecast history.
///
/// The brief calls storing history optional. It is not, in this design: it is
/// the last rung of the degradation ladder, the only source left once both the
/// provider and the cache are gone. Persistence earns its place by doing work.
/// </summary>
public interface IForecastHistory
{
    /// <summary>Records a live forecast so it can be replayed during an outage.</summary>
    Task SaveAsync(WeeklyForecast forecast, CancellationToken cancellationToken);

    /// <summary>The most recently stored forecast for a location, or null if there is none.</summary>
    Task<WeeklyForecast?> GetLatestAsync(GeoLocation location, CancellationToken cancellationToken);
}
