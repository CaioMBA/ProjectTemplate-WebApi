using System.Diagnostics;
using System.Text;
using Domain.Abstractions;
using Domain.Extensions;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Data.Broker.Providers.RabbitMq;

public sealed class RabbitMqEventBus(
    RabbitMqConnectionProvider connectionProvider,
    BrokerSettings connectionSettings,
    ILogger<RabbitMqEventBus> logger) : IEventBus
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

        var channel = await connectionProvider.GetChannelAsync(cancellationToken).ConfigureAwait(false);

        var body = Encoding.UTF8.GetBytes(integrationEvent.ToJson());

        var properties = new BasicProperties
        {
            MessageId = integrationEvent.EventId.ToString("N"),
            Type = integrationEvent.EventType,
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            Timestamp = new AmqpTimestamp(
                new DateTimeOffset(integrationEvent.OccurredOnUtc, TimeSpan.Zero).ToUnixTimeSeconds()),

            Persistent = true,

            Headers = new Dictionary<string, object?>
            {
                ["traceparent"] = Activity.Current?.Id,
            },
        };

        await channel.BasicPublishAsync(
                exchange: connectionSettings.RabbitMq.Exchange,
                routingKey: integrationEvent.EventType,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        logger.LogDebug(
            "Published {EventType} ({EventId}) to exchange {Exchange}.",
            integrationEvent.EventType,
            integrationEvent.EventId,
            connectionSettings.RabbitMq.Exchange);
    }
}
