using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Handlers;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "The request {Method} {Path} was aborted by the client.",
                httpContext.Request.Method,
                httpContext.Request.Path);

            httpContext.Response.StatusCode = 499;

            return true;
        }

        logger.LogError(
            exception,
            "Unhandled exception processing {Method} {Path}.",
            httpContext.Request.Method,
            httpContext.Request.Path);

        var statusCode = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            NotSupportedException => StatusCodes.Status501NotImplemented,
            TimeoutException => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status500InternalServerError,
        };

        httpContext.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : exception.GetType().Name,

            Detail = environment.IsDevelopment()
                ? exception.ToString()
                : "The request could not be completed. Quote the traceId when reporting this.",
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
        };

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        return await problemDetailsService
            .TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
                Exception = exception,
            })
            .ConfigureAwait(false);
    }
}
