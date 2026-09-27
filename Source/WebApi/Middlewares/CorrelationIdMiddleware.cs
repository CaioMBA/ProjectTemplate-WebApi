using System.Diagnostics;

namespace WebApi.Middlewares;

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = ResolveCorrelationId(context);

        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;

            return Task.CompletedTask;
        });

        Activity.Current?.SetTag("correlation.id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
        }))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var supplied))
        {
            var candidate = supplied.ToString();

            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length <= 128)
            {
                return candidate;
            }
        }

        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }
}
