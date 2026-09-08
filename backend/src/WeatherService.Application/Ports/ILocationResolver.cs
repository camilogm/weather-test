using WeatherService.Application.Model;

namespace WeatherService.Application.Ports;

/// <summary>
/// Turns what a person typed into somewhere on the map.
/// </summary>
public interface ILocationResolver
{
    /// <summary>
    /// Resolves one city name to coordinates, for callers that already know
    /// exactly which place they mean. Throws <see cref="UnknownLocationException"/>
    /// when there is no such place.
    /// </summary>
    Task<GeoLocation> ResolveAsync(string city, CancellationToken cancellationToken);

    /// <summary>
    /// Returns candidates for a partial name, so an interface can let someone
    /// SEE which place they are choosing instead of guessing on their behalf.
    ///
    /// A query too short to be meaningful, or one that matches nothing, comes
    /// back empty rather than throwing: an empty result list is a normal answer
    /// to "no suggestions yet", not a failure.
    /// </summary>
    Task<IReadOnlyList<LocationMatch>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// The caller asked for a place that could not be found. This is a 404, not a
/// 500: the request was well formed, the answer is simply "no such city".
/// </summary>
public sealed class UnknownLocationException : Exception
{
    public UnknownLocationException(string query, Exception? innerException = null)
        : base($"No location matching '{query}' could be found.", innerException) => Query = query;

    public string Query { get; }
}
