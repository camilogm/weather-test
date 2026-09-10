using WeatherService.Application.Model;

namespace WeatherService.Application.Ports;

/// <summary>
/// The port to whatever external weather service is plugged in.
///
/// Implementations are expected to be wrapped in their own resilience pipeline
/// (timeout, retry, circuit breaker) by the composition root — the pipeline is
/// an adapter concern, not a domain one. When a provider is unreachable, or its
/// breaker is open, implementations throw <see cref="WeatherProviderException"/>.
/// </summary>
public interface IWeatherProvider
{
    /// <summary>Human-readable id used in logs and diagnostics.</summary>
    string Name { get; }

    Task<ForecastSeries> GetForecastAsync(GeoLocation location, CancellationToken cancellationToken);

    Task<CurrentWeather> GetCurrentWeatherAsync(GeoLocation location, CancellationToken cancellationToken);
}
