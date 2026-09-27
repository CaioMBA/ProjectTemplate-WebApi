using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Domain.Abstractions;
using Domain.Extensions;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace Data.Broker.Providers.Kafka;

public sealed class KafkaEventBus(
    IProducer<string, byte[]> producer,
    BrokerSettings settings,
    ILogger<KafkaEventBus> logger) : IEventBus
{
    private static readonly ActivitySource _activitySource = new(TelemetryNames.IntegrationActivitySource);

    public async Task PublishAsync<TEvent>(
        TEvent integrationEvent,
        CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        await PublishCoreAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishManyAsync(
        IEnumerable<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvents);

        foreach (var integrationEvent in integrationEvents)
        {
            await PublishCoreAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishCoreAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity(
            $"Publish {integrationEvent.EventType}",
            ActivityKind.Producer);

        var message = new Message<string, byte[]>
        {
            Key = integrationEvent.EventId.ToString("N"),
            Value = Encoding.UTF8.GetBytes(integrationEvent.ToJson()),
            Headers = new Headers
            {
                { "event-type", Encoding.UTF8.GetBytes(integrationEvent.EventType) },
                { "traceparent", Encoding.UTF8.GetBytes(Activity.Current?.Id ?? string.Empty) },
            },
        };

        var topic = KafkaTopics.For(settings.Kafka.TopicPrefix, integrationEvent.EventType);

        var result = await producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "Published {EventType} ({EventId}) to {Topic} partition {Partition} offset {Offset}.",
            integrationEvent.EventType,
            integrationEvent.EventId,
            topic,
            result.Partition.Value,
            result.Offset.Value);
    }
}
