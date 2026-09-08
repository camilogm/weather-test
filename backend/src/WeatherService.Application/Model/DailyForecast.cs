namespace WeatherService.Application.Model;

/// <summary>
/// One day of the forecast. Temperatures are always Celsius inside the domain;
/// unit conversion, if ever needed, belongs at the edge.
/// </summary>
public sealed record DailyForecast(
    DateOnly Date,
    double MinTemperatureC,
    double MaxTemperatureC,
    WeatherCondition Condition);
