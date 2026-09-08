using WeatherService.Application.Model;

namespace WeatherService.Application.Ports;

/// <summary>
/// Turns a city name typed by a human into coordinates the provider understands.
/// Throws <see cref="UnknownLocationException"/> when there is no such place.
/// </summary>
public interface ILocationResolver
{
    Task<GeoLocation> ResolveAsync(string city, CancellationToken cancellationToken);
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
