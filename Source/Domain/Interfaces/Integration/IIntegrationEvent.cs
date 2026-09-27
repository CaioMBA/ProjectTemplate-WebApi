namespace Domain.Interfaces.Integration;

public interface IIntegrationEvent
{
    Guid EventId { get; }

    DateTime OccurredOnUtc { get; }

    string EventType { get; }
}

public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}