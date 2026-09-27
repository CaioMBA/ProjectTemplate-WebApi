using System.Diagnostics;
using Confluent.Kafka;
using Data.Broker.Consumers;
using Domain.Abstractions;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Data.Broker.Providers.Kafka;

public sealed class KafkaEventConsumer(
    BrokerSettings settings,
    IIntegrationEventRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaEventConsumer> logger) : BackgroundService
{
    private static readonly ActivitySource _activitySource =
        new(TelemetryNames.IntegrationActivitySource);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Factory.StartNew(
            () => ConsumeLoop(stoppingToken),
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private async Task ConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = settings.Kafka.BootstrapServers,
            GroupId = settings.Kafka.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            config.SaslUsername = settings.Username;
            config.SaslPassword = settings.Password;
            config.SaslMechanism = SaslMechanism.ScramSha512;
            config.SecurityProtocol = settings.UseSsl
                ? SecurityProtocol.SaslSsl
                : SecurityProtocol.SaslPlaintext;
        }

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();

        var topics = registry.KnownEventTypes
            .Select(eventType => KafkaTopics.For(settings.Kafka.TopicPrefix, eventType))
            .ToList();

        if (topics.Count == 0)
        {
            logger.LogWarning("No integration events are registered; the Kafka consumer is idle.");

            return;
        }

        consumer.Subscribe(topics);

        logger.LogInformation(
            "Consuming Kafka topics {Topics} as group {ConsumerGroup}.",
            string.Join(", ", topics),
            settings.Kafka.ConsumerGroup);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, byte[]>? result = null;

            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException exception)
            {
                logger.LogError(exception, "Kafka consume failed.");

                continue;
            }

            if (result?.Message is null)
            {
                continue;
            }

            await HandleAsync(consumer, result, stoppingToken).ConfigureAwait(false);
        }

        consumer.Close();
    }

    private async Task HandleAsync(
        IConsumer<string, byte[]> consumer,
        ConsumeResult<string, byte[]> result,
        CancellationToken stoppingToken)
    {
        using var activity = _activitySource.StartActivity(
            $"Consume {result.Topic}",
            ActivityKind.Consumer);

        await using var scope = scopeFactory.CreateAsyncScope();

        try
        {
            var integrationEvent = scope.ServiceProvider
                .GetRequiredService<IntegrationEventDeserializer>()
                .Deserialize(result.Message.Value, KafkaTopics.EventTypeOf(settings.Kafka.TopicPrefix, result.Topic));

            if (integrationEvent is null)
            {
                logger.LogWarning(
                    "Message on topic {Topic} could not be resolved to a known integration "
                    + "event; committing past it.",
                    result.Topic);
            }
            else
            {
                await scope.ServiceProvider
                    .GetRequiredService<IIntegrationEventDispatcher>()
                    .DispatchAsync(integrationEvent, stoppingToken)
                    .ConfigureAwait(false);
            }

            consumer.Commit(result);
        }
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.LogError(
                exception,
                "Handling a message on topic {Topic} failed; the offset was not committed.",
                result.Topic);
        }
    }
}
