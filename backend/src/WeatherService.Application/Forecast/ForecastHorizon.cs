namespace WeatherService.Application.Forecast;

/// <summary>
/// How far ahead this service is willing to look, and how much of that a caller
/// may ask for.
///
/// The numbers live in the Application layer rather than in the adapter on
/// purpose. <see cref="MaximumDays"/> is a promise the service makes to its
/// clients — the longest range it can answer — and a provider is chosen because
/// it can keep that promise, not the other way round. Open-Meteo currently caps
/// its free daily endpoint at sixteen days, which is why sixteen is the number;
/// a provider that reached further would let this rise without a single caller
/// changing.
///
/// Length is deliberately NOT part of what gets fetched or stored. The adapter
/// always retrieves <see cref="MaximumDays"/>, and a requested range is a
/// projection taken over that one answer. Fetching per range instead would key
/// the cache on the range too, and a service that already holds sixteen days
/// would still go to the network to be asked for seven of them.
/// </summary>
public static class ForecastHorizon
{
    /// <summary>Asking for nothing is not a range; one day is the floor.</summary>
    public const int MinimumDays = 1;

    /// <summary>
    /// What a caller gets when it names no range.
    ///
    /// Seven, because that is what the original contract answered and what every
    /// existing client still expects. Extending is opt-in.
    /// </summary>
    public const int DefaultDays = 7;

    /// <summary>
    /// The whole horizon: what is fetched, cached and stored, every time.
    ///
    /// Raising this past what the provider serves would not degrade the service,
    /// it would break it outright — Open-Meteo rejects the request rather than
    /// clamping it. OpenMeteoWeatherProviderTests holds the ceiling.
    /// </summary>
    public const int MaximumDays = 16;

    /// <summary>True when a requested range is one this service can answer.</summary>
    public static bool Allows(int days) => days is >= MinimumDays and <= MaximumDays;
}
