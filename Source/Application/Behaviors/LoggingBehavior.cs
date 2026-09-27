using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Domain.Interfaces.Identity;
using Domain.Interfaces.Messaging;
using Domain.Results;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

[SuppressMessage(
    "Major Code Smell",
    "S2139:Exceptions should be either logged or rethrown but not both",
    Justification = "Logging and rethrowing is the entire purpose of a logging pipeline " +
                    "behaviour. It records the failure with request context that the global " +
                    "exception handler no longer has, then lets the exception continue " +
                    "unchanged so the transaction still rolls back and the handler still " +
                    "produces the response. Swallowing it here would hide the fault; not " +
                    "logging would lose the context.")]
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string _requestName = typeof(TRequest).Name;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var timestamp = Stopwatch.GetTimestamp();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Dispatching {RequestName} for user {UserId}.",
                _requestName,
                currentUser.UserId ?? "anonymous");
        }

        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);
            var elapsedMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            if (response is Result { IsFailure: true } failure)
            {
                var errorCode = failure.Error.Code;
                var errorDescription = failure.Error.Description;

                logger.LogWarning(
                    "{RequestName} failed in {ElapsedMs}ms with {ErrorCode}: {ErrorDescription}",
                    _requestName,
                    elapsedMs,
                    errorCode,
                    errorDescription);
            }
            else
            {
                logger.LogInformation(
                    "{RequestName} completed in {ElapsedMs}ms.",
                    _requestName,
                    elapsedMs);
            }

            return response;
        }
        catch (OperationCanceledException exception)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            logger.LogInformation(
                exception,
                "{RequestName} was cancelled after {ElapsedMs}ms.",
                _requestName,
                elapsedMs);

            throw;
        }
        catch (Exception exception)
        {
            var elapsedMs = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            logger.LogError(
                exception,
                "{RequestName} threw after {ElapsedMs}ms.",
                _requestName,
                elapsedMs);

            throw;
        }
    }
}
