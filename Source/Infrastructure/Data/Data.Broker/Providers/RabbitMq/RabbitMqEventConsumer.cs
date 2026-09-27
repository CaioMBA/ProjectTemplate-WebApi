using System.Diagnostics;
using Data.Broker.Consumers;
using Domain.Abstractions;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Data.Broker.Providers.RabbitMq;

public sealed class RabbitMqEventConsumer(
    RabbitMqConnectionProvider connectionProvider,
    BrokerSettings settings,
    IIntegrationEventRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<RabbitMqEventConsumer> logger) : BackgroundService
{
    private static readonly ActivitySource _activitySource =
        new(TelemetryNames.IntegrationActivitySource);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channel = await connectionProvider.GetChannelAsync(stoppingToken).ConfigureAwait(false);

        await channel.QueueDeclareAsync(
                queue: settings.RabbitMq.Queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: stoppingToken)
            .ConfigureAwait(false);

        foreach (var eventType in registry.KnownEventTypes)
        {
            await channel.QueueBindAsync(
                    queue: settings.RabbitMq.Queue,
                    exchange: settings.RabbitMq.Exchange,
                    routingKey: eventType,
                    cancellationToken: stoppingToken)
                .ConfigureAwait(false);
        }

        await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: 16,
                global: false,
                cancellationToken: stoppingToken)
            .ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += (_, args) => HandleAsync(channel, args, stoppingToken);

        await channel.BasicConsumeAsync(
                queue: settings.RabbitMq.Queue,
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Consuming {Queue} bound to {Exchange} for {EventTypeCount} event types.",
            settings.RabbitMq.Queue,
            settings.RabbitMq.Exchange,
            registry.KnownEventTypes.Count);

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    private async Task HandleAsync(
        IChannel channel,
        BasicDeliverEventArgs args,
        CancellationToken stoppingToken)
    {
        using var activity = _activitySource.StartActivity(
            $"Consume {args.BasicProperties.Type}",
            ActivityKind.Consumer);

        await using var scope = scopeFactory.CreateAsyncScope();

        try
        {
            var deserializer = scope.ServiceProvider.GetRequiredService<IntegrationEventDeserializer>();

            var integrationEvent = deserializer.Deserialize(
                args.Body.Span,
                args.BasicProperties.Type);

            if (integrationEvent is null)
            {
                logger.LogWarning(
                    "Message {MessageId} declares unknown event type {EventType}; dropping it "
                    + "instead of requeueing to avoid a poison-message loop.",
                    args.BasicProperties.MessageId,
                    args.BasicProperties.Type);

                await channel.BasicRejectAsync(args.DeliveryTag, requeue: false, stoppingToken)
                    .ConfigureAwait(false);

                return;
            }

            await scope.ServiceProvider
                .GetRequiredService<IIntegrationEventDispatcher>()
                .DispatchAsync(integrationEvent, stoppingToken)
                .ConfigureAwait(false);

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false, stoppingToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogError(
                exception,
                "Handling message {MessageId} of type {EventType} failed; requeueing once.",
                args.BasicProperties.MessageId,
                args.BasicProperties.Type);

            await channel.BasicNackAsync(
                    args.DeliveryTag,
                    multiple: false,
                    requeue: !args.Redelivered,
                    stoppingToken)
                .ConfigureAwait(false);
        }
    }
}
