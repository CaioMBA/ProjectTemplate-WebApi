namespace Domain.Interfaces.Messaging;

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);

public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

public interface ICacheRequest
{
    string CacheId { get; }
}

public interface ICacheableRequest : ICacheRequest
{
    string CacheKey { get; }

    TimeSpan? CacheDuration { get; }
}

public interface ICacheInvalidatingRequest : ICacheRequest
{
    IReadOnlyCollection<string> CacheKeysToEvict { get; }
}

public interface IIdempotentRequest : ICacheRequest
{
    Guid RequestId { get; }
}

public interface IDatabaseRequest
{
    string DatabaseId { get; }
}

public interface ITransactionalRequest
{
    bool UseTransaction { get; }
}
