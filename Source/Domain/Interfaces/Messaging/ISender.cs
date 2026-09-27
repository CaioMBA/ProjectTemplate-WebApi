namespace Domain.Interfaces.Messaging;

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

public interface IDomainEventPublisher
{
    Task Publish(Abstractions.IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
