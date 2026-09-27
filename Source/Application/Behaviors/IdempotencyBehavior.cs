using Application.Dispatching;
using Domain.Connections;
using Domain.Enums;
using Domain.Extensions;
using Domain.Interfaces.Messaging;
using Domain.Results;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

public sealed class IdempotencyBehavior<TRequest, TResponse>(
    IServiceProvider serviceProvider,
    ILogger<IdempotencyBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    private static readonly TimeSpan _retentionWindow = TimeSpan.FromHours(24);

    private static readonly string _requestName = typeof(TRequest).Name;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (request is not IIdempotentRequest idempotent)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        if (idempotent.RequestId == Guid.Empty)
        {
            return ResultFactory.Failure<TResponse>(Error.Validation(
                $"{typeof(TRequest).Name}.RequestIdRequired",
                "A non-empty RequestId is required for an idempotent request."));
        }

        var key = Domain.Extensions.DistributedCacheExtensions.BuildKey(
            "idempotency",
            typeof(TRequest).FullName,
            idempotent.RequestId.ToString("N"));

        var cache = serviceProvider.RequireKeyed<IDistributedCache>(idempotent.CacheId, KeyedConnections.Caches);

        var recorded = await cache.GetValueAsync<IdempotencyRecord>(key, cancellationToken).ConfigureAwait(false);

        if (recorded is not null)
        {
            var requestId = idempotent.RequestId;

            logger.LogInformation(
                "Replaying recorded outcome for idempotent {RequestName} {RequestId}.",
                _requestName,
                requestId);

            return recorded.Succeeded
                ? ResultFactory.Success<TResponse>(recorded.ResponsePayload)
                : ResultFactory.Failure<TResponse>(
                    new Error(recorded.ErrorCode ?? "General.Replayed", recorded.ErrorDescription ?? string.Empty, recorded.ErrorType));
        }

        var response = await next(cancellationToken).ConfigureAwait(false);

        var record = new IdempotencyRecord
        {
            Succeeded = response.IsSuccess,

            ResponsePayload = response.IsSuccess
                ? ResultFactory.GetValue(response)?.ToJson()
                : null,
            ErrorCode = response.IsFailure ? response.Error.Code : null,
            ErrorDescription = response.IsFailure ? response.Error.Description : null,
            ErrorType = response.IsFailure ? response.Error.Type : ErrorType.None,
            RecordedAtUtc = DateTime.UtcNow,
        };

        await cache.SetValueAsync(key, record, _retentionWindow, cancellationToken).ConfigureAwait(false);

        return response;
    }

    internal sealed record IdempotencyRecord
    {
        public bool Succeeded { get; init; }

        public string? ResponsePayload { get; init; }

        public string? ErrorCode { get; init; }

        public string? ErrorDescription { get; init; }

        public ErrorType ErrorType { get; init; }

        public DateTime RecordedAtUtc { get; init; }
    }
}
