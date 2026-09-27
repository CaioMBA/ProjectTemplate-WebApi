using Domain.Interfaces.Integration;

namespace Domain.Interfaces.Persistence;

public interface IOutboxWriter
{
    Task EnqueueAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);

    Task EnqueueManyAsync(
        IEnumerable<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default);
}
