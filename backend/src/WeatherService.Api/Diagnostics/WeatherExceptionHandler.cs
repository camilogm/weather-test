using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using WeatherService.Application.Current;
using WeatherService.Application.Forecast;
using WeatherService.Application.Ports;

namespace WeatherService.Api.Diagnostics;

/// <summary>
/// Turns domain failures into RFC 7807 responses.
///
/// The distinction that matters is 503 versus 500. A 500 says "this service is
/// broken"; a 503 says "this service is fine, its upstream is not, try again".
/// Reporting an upstream outage as a 500 sends whoever is on call to read the
/// wrong logs, and tells clients not to bother retrying when retrying is exactly
/// what they should do.
/// </summary>
public sealed class WeatherExceptionHandler : IExceptionHandler
{
    /// <summary>Nginx's non-standard "client closed request"; ASP.NET ships no constant for it.</summary>
    private const int ClientClosedRequest = 499;

    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<WeatherExceptionHandler> _logger;

    public WeatherExceptionHandler(
        IProblemDetailsService problemDetails,
        ILogger<WeatherExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            UnknownLocationException unknown => (
                StatusCodes.Status404NotFound,
                "Location not found",
                unknown.Message),

            ForecastUnavailableException or CurrentWeatherUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                "Weather data is temporarily unavailable",
                exception.Message),

            // A search has no cache or history to fall back on, so an upstream
            // failure reaches here directly. It is still an outage, not a fault.
            WeatherProviderException => (
                StatusCodes.Status503ServiceUnavailable,
                "Weather data is temporarily unavailable",
                exception.Message),

            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (
                ClientClosedRequest,
                "Client closed the request",
                "The caller went away before the response was ready."),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Unexpected error",
                "The request could not be completed."),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled failure on {Path}", httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Request to {Path} failed with {Status}",
                httpContext.Request.Path,
                status);
        }

        httpContext.Response.StatusCode = status;

        // A caller that has already hung up cannot be written to.
        if (status == ClientClosedRequest)
        {
            return true;
        }

        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "30";
        }

        return await _problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = status,
                    Title = title,
                    Detail = detail,
                    Instance = httpContext.Request.Path,
                },
            });
    }
}
