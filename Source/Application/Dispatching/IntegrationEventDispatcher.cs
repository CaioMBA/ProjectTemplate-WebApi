using System.Collections.Concurrent;
using Domain.Interfaces.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Dispatching;

public sealed class IntegrationEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<IntegrationEventDispatcher> logger) : IIntegrationEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Type> _handlerTypes = new();

    public async Task DispatchAsync(
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = integrationEvent.GetType();

        var handlerType = _handlerTypes.GetOrAdd(
            eventType,
            static type => typeof(IIntegrationEventHandler<>).MakeGenericType(type));

        var handlers = serviceProvider.GetServices(handlerType).OfType<object>().ToList();

        if (handlers.Count == 0)
        {
            logger.LogWarning(
                "No IIntegrationEventHandler is registered for {EventType} ({EventId}). "
                + "The message was consumed and discarded.",
                integrationEvent.EventType,
                integrationEvent.EventId);

            return;
        }

        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IIntegrationEvent>.Handle))
            ?? throw new InvalidOperationException(
                $"Handler type '{handlerType}' does not expose a Handle method.");

        foreach (var handler in handlers)
        {
            await ((Task)method.Invoke(handler, [integrationEvent, cancellationToken])!)
                .ConfigureAwait(false);
        }
    }
}
