using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly;
using WeatherService.Application.Forecast;
using WeatherService.Application.Model;
using WeatherService.Application.Ports;

namespace WeatherService.Infrastructure.Providers.OpenMeteo;

/// <summary>
/// Adapter for Open-Meteo, chosen because it needs no API key and covers the
/// whole horizon this service promises — sixteen daily entries, which is that
/// endpoint's own ceiling — so the reviewer can run this without registering
/// anywhere.
///
/// The class knows nothing about retries, timeouts or circuit breakers. Those
/// wrap this client from the outside, at the composition root, because they are
/// transport concerns. What this class DOES own is the boundary contract: every
/// transport failure — a dead socket, a 500, a truncated payload, an open
/// circuit — leaves here as <see cref="WeatherProviderException"/>, so nothing
/// above ever has to know that HTTP or Polly exist.
/// </summary>
public sealed class OpenMeteoWeatherProvider : IWeatherProvider
{
    public const string ProviderName = "open-meteo";

    /// <summary>
    /// Still not a tuning knob, but no longer a week either.
    ///
    /// The adapter fetches the service's whole horizon on every call and never
    /// the range a caller asked for. That is what lets one cached answer satisfy
    /// every range, and one stored snapshot back every range during an outage.
    /// The number itself belongs to <see cref="ForecastHorizon"/>, which is
    /// where the promise to callers is written down.
    /// </summary>
    private const int ForecastDays = ForecastHorizon.MaximumDays;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly TimeProvider _clock;
    private readonly ILogger<OpenMeteoWeatherProvider> _logger;

    public OpenMeteoWeatherProvider(
        HttpClient client,
        TimeProvider clock,
        ILogger<OpenMeteoWeatherProvider> logger)
    {
        _client = client;
        _clock = clock;
        _logger = logger;
    }

    public string Name => ProviderName;

    public async Task<ForecastSeries> GetForecastAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        var url =
            $"v1/forecast?latitude={Coordinate(location.Latitude)}"
            + $"&longitude={Coordinate(location.Longitude)}"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min"
            + "&timezone=auto"
            + $"&forecast_days={ForecastDays}";

        var payload = await GetAsync<OpenMeteoForecastResponse>(url, cancellationToken);
        var daily = payload.Daily ?? throw Malformed("the response carried no daily block");

        return new ForecastSeries(location, ToDays(daily), _clock.GetUtcNow());
    }

    public async Task<CurrentWeather> GetCurrentWeatherAsync(
        GeoLocation location,
        CancellationToken cancellationToken)
    {
        var url =
            $"v1/forecast?latitude={Coordinate(location.Latitude)}"
            + $"&longitude={Coordinate(location.Longitude)}"
            + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m"
            + "&timezone=auto";

        var payload = await GetAsync<OpenMeteoCurrentResponse>(url, cancellationToken);
        var current = payload.Current ?? throw Malformed("the response carried no current block");

        if (current.Temperature is null || current.WeatherCode is null)
        {
            throw Malformed("the current block was missing temperature or condition");
        }

        return new CurrentWeather(
            location,
            current.Temperature.Value,
            current.ApparentTemperature ?? current.Temperature.Value,
            current.WindSpeed ?? 0,
            current.RelativeHumidity ?? 0,
            WmoWeatherCode.ToCondition(current.WeatherCode.Value),
            _clock.GetUtcNow());
    }

    /// <summary>
    /// Open-Meteo answers in columns, not rows, and the two ways that can go
    /// wrong need opposite treatment.
    ///
    /// A column of the WRONG LENGTH is fatal. Zipping mismatched columns would
    /// pair the wrong temperature with the wrong day and produce a forecast that
    /// looks entirely plausible and is wrong — far worse than no forecast at all.
    ///
    /// A NULL ENTRY inside correctly-sized columns is not. Open-Meteo pads the
    /// tail of the horizon that way when the location's local calendar runs past
    /// the model's data window, so fifteen real days can arrive alongside an empty
    /// sixteenth. Nothing is misaligned by it, and a day with no condition and no
    /// temperatures carries nothing to show, so that day is dropped and the rest
    /// is served. Each <see cref="DailyForecast"/> carries its own date, so the
    /// series survives a hole in the middle just as well as one at the end.
    ///
    /// If NO day survives, the answer is unusable and leaves as a provider
    /// failure. It must not leave as an empty series: that would be cached for
    /// ten minutes and written to history as a snapshot, poisoning the two
    /// fallbacks that exist for exactly this moment.
    /// </summary>
    private static IReadOnlyList<DailyForecast> ToDays(OpenMeteoDailyBlock daily)
    {
        if (daily.Time is null
            || daily.WeatherCode is null
            || daily.MaxTemperature is null
            || daily.MinTemperature is null)
        {
            throw Malformed("the daily block was missing one of its columns");
        }

        var days = daily.Time.Count;
        if (daily.WeatherCode.Count != days
            || daily.MaxTemperature.Count != days
            || daily.MinTemperature.Count != days)
        {
            throw Malformed(
                $"the daily columns disagree in length "
                + $"(time={days}, code={daily.WeatherCode.Count}, "
                + $"max={daily.MaxTemperature.Count}, min={daily.MinTemperature.Count})");
        }

        var forecast = new List<DailyForecast>(days);
        for (var index = 0; index < days; index++)
        {
            var date = daily.Time[index];
            var code = daily.WeatherCode[index];
            var max = daily.MaxTemperature[index];
            var min = daily.MinTemperature[index];

            if (date is null || code is null || max is null || min is null)
            {
                continue;
            }

            forecast.Add(
                new DailyForecast(
                    ParseDate(date),
                    min.Value,
                    max.Value,
                    WmoWeatherCode.ToCondition(code.Value)));
        }

        if (forecast.Count == 0)
        {
            throw Malformed($"none of the {days} days it sent carried any data");
        }

        return forecast;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await _client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new WeatherProviderException(
                    ProviderName,
                    $"Open-Meteo answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                ?? throw Malformed("the response body was empty");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller walked away. That is not a provider outage and must not
            // be recorded as one, or a cancelled request would help trip the
            // breaker against a service that is perfectly healthy.
            throw;
        }
        catch (WeatherProviderException)
        {
            throw;
        }
        catch (Exception failure) when (failure
            is HttpRequestException // socket died, DNS failed, connection refused
            or JsonException // body was not the shape we expect
            or NotSupportedException // content type was not JSON at all
            or OperationCanceledException // a timeout from the resilience pipeline
            or ExecutionRejectedException) // Polly said no: open circuit, timeout, rate limit
        {
            _logger.LogWarning(failure, "Open-Meteo call to {Url} failed", url);

            throw new WeatherProviderException(
                ProviderName,
                $"Open-Meteo could not be reached: {failure.Message}",
                failure);
        }
    }

    /// <summary>
    /// Invariant culture is not optional here: under a locale that writes
    /// decimals with a comma, "13,6929" would reach the API as a malformed
    /// coordinate — or worse, as two query values.
    /// </summary>
    private static string Coordinate(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Malformed($"'{value}' is not a date Open-Meteo should have sent");

    private static WeatherProviderException Malformed(string what) =>
        new(ProviderName, $"Open-Meteo returned something unusable: {what}.");
}
