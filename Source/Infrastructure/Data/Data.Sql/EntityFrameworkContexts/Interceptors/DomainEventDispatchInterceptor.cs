using Domain.Abstractions;
using Domain.Interfaces.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Data.Sql.EntityFrameworkContexts.Interceptors;

public sealed class DomainEventDispatchInterceptor(IServiceProvider serviceProvider) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var events = CollectAndClear(eventData.Context);

        if (events.Count > 0)
        {
            var publisher = (IDomainEventPublisher?)serviceProvider.GetService(typeof(IDomainEventPublisher));

            if (publisher is not null)
            {
                foreach (var domainEvent in events)
                {
                    await publisher.Publish(domainEvent, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        CollectAndClear(eventData.Context);

        return base.SavedChanges(eventData, result);
    }

    private static List<IDomainEvent> CollectAndClear(DbContext? context)
    {
        if (context is null)
        {
            return [];
        }

        var aggregates = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        var events = aggregates
            .SelectMany(aggregate => aggregate.DomainEvents)
            .ToList();

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        return events;
    }
}
