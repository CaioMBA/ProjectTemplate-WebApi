using System.Diagnostics.CodeAnalysis;

namespace Domain.Interfaces.Integration;

public interface IIntegrationEventRegistry
{
    IReadOnlyCollection<string> KnownEventTypes { get; }

    bool TryResolve(string eventType, [NotNullWhen(true)] out Type? clrType);
}

public interface IIntegrationEventDispatcher
{
    Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
