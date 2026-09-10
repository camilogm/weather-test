using WeatherService.Application.Model;

namespace WeatherService.Application.Forecast;

/// <summary>The forecast plus the provenance of the data that produced it.</summary>
public sealed record ForecastResult(ForecastSeries Forecast, WeatherDataSource Source)
{
    /// <summary>True when the answer did not come from a live provider call.</summary>
    public bool IsDegraded => Source is WeatherDataSource.StaleCache or WeatherDataSource.Historical;
}
