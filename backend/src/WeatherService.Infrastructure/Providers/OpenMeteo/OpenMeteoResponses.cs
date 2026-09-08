using System.Text.Json.Serialization;

namespace WeatherService.Infrastructure.Providers.OpenMeteo;

/// <summary>
/// Wire shapes for Open-Meteo. Internal on purpose: nothing outside this adapter
/// should be able to take a dependency on the provider's payload.
///
/// Note the forecast comes back COLUMNAR — parallel arrays rather than a list of
/// days — which is why the adapter validates their lengths before zipping them.
/// </summary>
internal sealed record OpenMeteoForecastResponse(
    [property: JsonPropertyName("daily")] OpenMeteoDailyBlock? Daily);

internal sealed record OpenMeteoDailyBlock(
    [property: JsonPropertyName("time")] IReadOnlyList<string>? Time,
    [property: JsonPropertyName("weather_code")] IReadOnlyList<int>? WeatherCode,
    [property: JsonPropertyName("temperature_2m_max")] IReadOnlyList<double>? MaxTemperature,
    [property: JsonPropertyName("temperature_2m_min")] IReadOnlyList<double>? MinTemperature);

internal sealed record OpenMeteoCurrentResponse(
    [property: JsonPropertyName("current")] OpenMeteoCurrentBlock? Current);

internal sealed record OpenMeteoCurrentBlock(
    [property: JsonPropertyName("time")] string? Time,
    [property: JsonPropertyName("temperature_2m")] double? Temperature,
    [property: JsonPropertyName("apparent_temperature")] double? ApparentTemperature,
    [property: JsonPropertyName("relative_humidity_2m")] int? RelativeHumidity,
    [property: JsonPropertyName("wind_speed_10m")] double? WindSpeed,
    [property: JsonPropertyName("weather_code")] int? WeatherCode);
