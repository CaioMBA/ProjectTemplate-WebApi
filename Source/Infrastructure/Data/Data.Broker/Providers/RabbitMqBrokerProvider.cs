using Data.Broker.Providers.RabbitMq;
using Domain.Enums;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Data.Broker.Providers;

public sealed class RabbitMqBrokerProvider : BrokerProviderBase<RabbitMqConnectionProvider>
{
    public const int DefaultPort = 5672;

    public override BrokerType ProviderType => BrokerType.RabbitMq;

    public override Type EventBusType => typeof(RabbitMqEventBus);

    public override Type ConsumerType => typeof(RabbitMqEventConsumer);

    public override void Validate(BrokerSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Require(!string.IsNullOrWhiteSpace(connection.Host), connection, nameof(connection.Host), "RabbitMq");

        Require(
            !string.IsNullOrWhiteSpace(connection.RabbitMq.Exchange),
            connection,
            "RabbitMq:Exchange",
            "RabbitMq");

        if (connection.EnableConsumer)
        {
            Require(
                !string.IsNullOrWhiteSpace(connection.RabbitMq.Queue),
                connection,
                "RabbitMq:Queue",
                "The RabbitMq consumer");
        }
    }

    protected override RabbitMqConnectionProvider CreateConnection(
        IServiceProvider provider,
        BrokerSettings connection) =>
        new(connection, provider.GetRequiredService<ILogger<RabbitMqConnectionProvider>>());

    protected override IEventBus CreateEventBus(
        IServiceProvider provider,
        RabbitMqConnectionProvider brokerConnection,
        BrokerSettings connection) =>
        new RabbitMqEventBus(brokerConnection, connection, provider.GetRequiredService<ILogger<RabbitMqEventBus>>());

    protected override IHostedService CreateConsumer(
        IServiceProvider provider,
        RabbitMqConnectionProvider brokerConnection,
        BrokerSettings connection) =>
        new RabbitMqEventConsumer(
            brokerConnection,
            connection,
            provider.GetRequiredService<IIntegrationEventRegistry>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<ILogger<RabbitMqEventConsumer>>());

    protected override IHealthCheck CreateHealthCheck(RabbitMqConnectionProvider brokerConnection) =>
        new RabbitMqHealthCheck(brokerConnection);
}