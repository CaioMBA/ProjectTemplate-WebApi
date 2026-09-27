using Domain.Interfaces.Integration;

namespace Domain.Interfaces.Broker;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;

    Task PublishManyAsync(
        IEnumerable<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default);
}