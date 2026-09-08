namespace WeatherService.Application.Model;

/// <summary>
/// Provider-agnostic weather condition.
/// Open-Meteo speaks WMO codes, OpenWeatherMap speaks its own ids — neither
/// vocabulary reaches the domain. Adapters translate into this enum.
/// </summary>
public enum WeatherCondition
{
    Unknown = 0,
    Clear,
    MainlyClear,
    PartlyCloudy,
    Overcast,
    Fog,
    Drizzle,
    Rain,
    FreezingRain,
    Snow,
    RainShowers,
    SnowShowers,
    Thunderstorm,
}
