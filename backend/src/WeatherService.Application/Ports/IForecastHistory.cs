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
    Task SaveAsync(ForecastSeries forecast, CancellationToken cancellationToken);

    /// <summary>
    /// The most recently stored forecast for a location that is still recent
    /// enough to be worth serving, or null if there is none.
    ///
    /// "Still worth serving" is part of the contract, not an implementation
    /// detail: a stored forecast describes the days that followed the moment it
    /// was taken, so once it ages past that span it no longer describes the
    /// future at all. Returning it would end the degradation chain with a
    /// confident answer about days that have already happened.
    /// </summary>
    Task<ForecastSeries?> GetLatestAsync(GeoLocation location, CancellationToken cancellationToken);
}
