using System.Net;

namespace WeatherService.UnitTests.TestDoubles;

/// <summary>
/// A hand-rolled <see cref="HttpMessageHandler"/> stub.
///
/// Deliberately not a mocking library: the seam HttpClient gives you IS the
/// message handler, and standing one up takes six lines. Reaching for a mock
/// here would hide that, and it would not be any shorter.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    private StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = respond;

    /// <summary>Requests this handler has seen, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    public static StubHttpMessageHandler Returning(HttpStatusCode status, string body) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        });

    public static StubHttpMessageHandler Throwing(Exception failure) =>
        new(_ => throw failure);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.FromResult(_respond(request));
    }
}
