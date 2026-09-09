using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace WeatherService.Api.RateLimiting;

/// <summary>
/// Rate limiting for the endpoints that stand in front of a third party.
///
/// This service put itself between the browser and Open-Meteo on purpose, so
/// that a circuit breaker, a cache and real error handling sit in that path.
/// The other half of that bargain is that the service now owns the quota: an
/// unauthenticated proxy with no ceiling lets one caller in a loop spend an
/// allowance that belongs to everybody.
/// </summary>
public static class RateLimitingExtensions
{
    /// <summary>
    /// Where a request with no discernible source is counted.
    ///
    /// A single shared bucket rather than a free pass: behind a misconfigured
    /// proxy every caller looks like this, and "we cannot tell who you are" is a
    /// reason to be more careful, not less.
    /// </summary>
    private const string UnknownCaller = "unknown";

    public static IServiceCollection AddWeatherRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            limiter.AddPolicy(
                RateLimitPolicies.Weather,
                context => PartitionFor(context, options => options.WeatherPermits));

            limiter.AddPolicy(
                RateLimitPolicies.Search,
                context => PartitionFor(context, options => options.SearchPermits));

            limiter.OnRejected = WriteProblemDetailsAsync;
        });

        return services;
    }

    /// <summary>
    /// One sliding window per caller.
    ///
    /// Sliding rather than fixed because a fixed window lets a caller spend a
    /// full budget at 11:59:59 and another at 12:00:00 — double the intended
    /// rate across the boundary, which is exactly the burst this is meant to
    /// stop. Nothing queues: making a throttled caller wait holds a connection
    /// open to tell them something a 429 says immediately.
    /// </summary>
    private static RateLimitPartition<string> PartitionFor(
        HttpContext context,
        Func<RateLimitOptions, int> permits)
    {
        var options = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

        if (!options.Enabled)
        {
            return RateLimitPartition.GetNoLimiter(UnknownCaller);
        }

        var caller = context.Connection.RemoteIpAddress?.ToString() ?? UnknownCaller;

        return RateLimitPartition.GetSlidingWindowLimiter(
            caller,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = permits(options),
                Window = options.Window,
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
    }

    /// <summary>
    /// Refuses in the same shape as every other failure this API produces.
    ///
    /// A bare 429 with an empty body would be the one error in the service that
    /// is not an RFC 7807 document, and a client parsing problem documents would
    /// hit a surprise precisely when it is already having a bad time.
    /// </summary>
    private static async ValueTask WriteProblemDetailsAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        var options = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

        // The limiter knows how long until a permit frees up; the configured
        // window is the honest upper bound when it does not say.
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var hint)
            ? hint
            : options.Window;

        http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);

        var problemDetails = http.RequestServices.GetRequiredService<IProblemDetailsService>();

        await problemDetails.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = http,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = "This endpoint is rate limited. Try again shortly.",
                    Instance = http.Request.Path,
                },
            });
    }
}
