namespace WeatherService.Application.Ports;

/// <summary>
/// Raised by a provider adapter when the external service could not answer:
/// network failure, non-success status, unparseable payload, or an open circuit
/// breaker. It is the single failure type the use case has to reason about, so
/// the domain never sees <c>HttpRequestException</c> or Polly types.
/// </summary>
public sealed class WeatherProviderException : Exception
{
    public WeatherProviderException(string providerName, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderName = providerName;
    }

    public string ProviderName { get; }
}
