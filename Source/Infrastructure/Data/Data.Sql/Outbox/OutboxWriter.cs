using System.Diagnostics;
using Data.Sql.EntityFrameworkContexts;
using Domain.Entities;
using Domain.Extensions;
using Domain.Interfaces.Integration;
using Domain.Interfaces.Persistence;
using Domain.Interfaces.Services;

namespace Data.Sql.Outbox;

public sealed class OutboxWriter(
    AppDbContext context,
    IDateTimeProvider dateTimeProvider) : IOutboxWriter
{
    public async Task EnqueueAsync(
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await context.OutboxMessages
            .AddAsync(ToMessage(integrationEvent, dateTimeProvider.UtcNow), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task EnqueueManyAsync(
        IEnumerable<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvents);

        var now = dateTimeProvider.UtcNow;
        var messages = integrationEvents.Select(integrationEvent => ToMessage(integrationEvent, now));

        await context.OutboxMessages.AddRangeAsync(messages, cancellationToken).ConfigureAwait(false);
    }

    private static OutboxMessageEntity ToMessage(IIntegrationEvent integrationEvent, DateTime now)
    {
        var type = integrationEvent.GetType();

        return new OutboxMessageEntity
        {
            Id = integrationEvent.EventId == Guid.Empty ? Guid.CreateVersion7() : integrationEvent.EventId,
            EventType = integrationEvent.EventType,

            ContentType = $"{type.FullName}, {type.Assembly.GetName().Name}",
            Payload = integrationEvent.ToJson(),
            OccurredOnUtc = integrationEvent.OccurredOnUtc == default ? now : integrationEvent.OccurredOnUtc,
            NextAttemptUtc = now,

            TraceParent = Activity.Current?.Id,
        };
    }
}
