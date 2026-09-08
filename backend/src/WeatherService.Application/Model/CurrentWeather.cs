namespace WeatherService.Application.Model;

/// <summary>Current conditions for a location.</summary>
public sealed record CurrentWeather(
    GeoLocation Location,
    double TemperatureC,
    double ApparentTemperatureC,
    double WindSpeedKph,
    int RelativeHumidityPercent,
    WeatherCondition Condition,
    DateTimeOffset ObservedAt);
