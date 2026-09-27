using System.Globalization;
using System.Threading.RateLimiting;
using Domain.Models.Configuration;

namespace WebApi.Setup;

public static class RateLimitSetup
{
    public static IServiceCollection AddRateLimitSetup(this IServiceCollection services, ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var limits = options.RateLimit;

        if (!limits.Enabled)
        {
            return services;
        }

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = limits.RejectionStatusCode;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext, string>(
                context =>
                {
                    var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.PermitLimit,
                            Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,

                            QueueLimit = limits.QueueLimit,
                        });
                });

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        limits.WindowSeconds.ToString(CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response
                    .WriteAsJsonAsync(
                        new
                        {
                            status = limits.RejectionStatusCode,
                            title = "TooManyRequests",
                            detail = $"Rate limit exceeded. Retry in {limits.WindowSeconds}s.",
                            traceId = context.HttpContext.TraceIdentifier,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            };
        });

        return services;
    }
}
