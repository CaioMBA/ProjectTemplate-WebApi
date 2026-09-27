using System.Collections.Concurrent;
using Domain.Abstractions;
using Domain.Interfaces.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Dispatching;

public sealed class DomainEventPublisher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventPublisher> logger) : IDomainEventPublisher
{
    private static readonly ConcurrentDictionary<Type, Type> _handlerTypes = new();

    public async Task Publish(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();

        var handlerType = _handlerTypes.GetOrAdd(
            eventType,
            static type => typeof(IDomainEventHandler<>).MakeGenericType(type));

        var handlers = serviceProvider.GetServices(handlerType);

        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            try
            {
                await InvokeHandler(handler, handlerType, domainEvent, cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031
            catch (Exception exception)
#pragma warning restore CA1031
            {
                logger.LogError(
                    exception,
                    "Domain event handler {HandlerType} failed for {EventType} ({EventId}).",
                    handler.GetType().Name,
                    eventType.Name,
                    domainEvent.EventId);
            }
        }
    }

    private static Task InvokeHandler(
        object handler,
        Type handlerType,
        IDomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))
                     ?? throw new InvalidOperationException(
                         $"Handler type '{handlerType}' does not expose a Handle method.");

        return (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
    }
}
