using System.Diagnostics;
using Domain.Abstractions;
using Domain.Interfaces.Messaging;
using Domain.Results;

namespace Application.Behaviors;

internal static class ApplicationDiagnostics
{
    public static readonly ActivitySource ActivitySource = new(TelemetryNames.ApplicationActivitySource);
}

public sealed class TelemetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var requestName = typeof(TRequest).Name;

        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            $"Dispatch {requestName}",
            ActivityKind.Internal);

        activity?.SetTag(TelemetryNames.RequestNameTag, requestName);

        try
        {
            var response = await next(cancellationToken).ConfigureAwait(false);

            if (response is Result result)
            {
                activity?.SetTag(TelemetryNames.RequestSuccessTag, result.IsSuccess);

                if (result.IsFailure)
                {
                    activity?.SetTag(TelemetryNames.RequestErrorCodeTag, result.Error.Code);
                }
            }

            return response;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.AddException(exception);

            throw;
        }
    }
}
