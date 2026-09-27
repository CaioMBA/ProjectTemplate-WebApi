using Confluent.Kafka;
using Data.Broker.Providers.Kafka;
using Domain.Enums;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Data.Broker.Providers;

public sealed class KafkaBrokerProvider : BrokerProviderBase<IProducer<string, byte[]>>
{
    public override BrokerType ProviderType => BrokerType.Kafka;

    public override Type EventBusType => typeof(KafkaEventBus);

    public override void Validate(BrokerSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Require(
            !string.IsNullOrWhiteSpace(connection.Kafka.BootstrapServers),
            connection,
            "Kafka:BootstrapServers",
            "Kafka");

        Require(
            !string.IsNullOrWhiteSpace(connection.Kafka.TopicPrefix),
            connection,
            "Kafka:TopicPrefix",
            "Kafka");

        if (connection.EnableConsumer)
        {
            Require(
                !string.IsNullOrWhiteSpace(connection.Kafka.ConsumerGroup),
                connection,
                "Kafka:ConsumerGroup",
                "The Kafka consumer");
        }
    }

    public static ProducerConfig BuildProducerConfig(BrokerSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var config = new ProducerConfig
        {
            BootstrapServers = connection.Kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 3,
            LingerMs = 5,
        };

        if (!string.IsNullOrWhiteSpace(connection.Username))
        {
            config.SaslUsername = connection.Username;
            config.SaslPassword = connection.Password;
            config.SaslMechanism = SaslMechanism.ScramSha512;
            config.SecurityProtocol = connection.UseSsl
                ? SecurityProtocol.SaslSsl
                : SecurityProtocol.SaslPlaintext;
        }

        return config;
    }

    public override Type ConsumerType => typeof(KafkaEventConsumer);

    protected override IProducer<string, byte[]> CreateConnection(
        IServiceProvider provider,
        BrokerSettings connection) =>
        new ProducerBuilder<string, byte[]>(BuildProducerConfig(connection)).Build();

    protected override IEventBus CreateEventBus(
        IServiceProvider provider,
        IProducer<string, byte[]> brokerConnection,
        BrokerSettings connection) =>
        new KafkaEventBus(brokerConnection, connection, provider.GetRequiredService<ILogger<KafkaEventBus>>());

    protected override IHostedService CreateConsumer(
        IServiceProvider provider,
        IProducer<string, byte[]> brokerConnection,
        BrokerSettings connection) =>
        new KafkaEventConsumer(
            connection,
            provider.GetRequiredService<IIntegrationEventRegistry>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<KafkaEventConsumer>>());

    protected override IHealthCheck CreateHealthCheck(IProducer<string, byte[]> brokerConnection) =>
        new KafkaHealthCheck(brokerConnection);
}