using Data.Broker.Consumers;
using Domain.Abstractions;
using Domain.Connections;
using Domain.Enums;
using Domain.Interfaces.Broker;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Data.Broker.Providers;

public abstract class BrokerProviderBase<TConnection> : IBrokerProvider
    where TConnection : class
{
    public abstract BrokerType ProviderType { get; }

    public abstract Type EventBusType { get; }

    public abstract Type ConsumerType { get; }

    public abstract void Validate(BrokerSettings connection);

    public void Register(IServiceCollection services, BrokerSettings connection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);

        var brokerId = connection.Id;

        services.AddKeyedSingleton(brokerId, (provider, _) => new LazyConnection<TConnection>(() =>
        {
            Validate(connection);

            return CreateConnection(provider, connection);
        }));

        services.AddKeyedSingleton<TConnection>(brokerId, (provider, key) =>
            provider.GetRequiredKeyedService<LazyConnection<TConnection>>(key).Value);

        services.AddKeyedSingleton<IEventBus>(brokerId, (provider, key) =>
            CreateEventBus(provider, provider.GetRequiredKeyedService<TConnection>(key), connection));

        if (connection.EnableConsumer)
        {
            services.TryAddScoped<IntegrationEventDeserializer>();

            services.AddSingleton<IHostedService>(provider =>
                CreateConsumer(provider, provider.GetRequiredKeyedService<TConnection>(brokerId), connection));
        }

        services.AddHealthChecks().Add(HealthCheckPolicy.Registration(
            DependencyKind.Broker,
            $"broker:{brokerId}",
            provider => CreateHealthCheck(provider.GetRequiredKeyedService<TConnection>(brokerId))));
    }

    protected static void Require(bool present, BrokerSettings connection, string property, string engine)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!present)
        {
            throw new InvalidOperationException($"{engine} requires {connection.KeyOf(property)}.");
        }
    }

    protected abstract TConnection CreateConnection(IServiceProvider provider, BrokerSettings connection);

    protected abstract IEventBus CreateEventBus(
        IServiceProvider provider,
        TConnection brokerConnection,
        BrokerSettings connection);

    protected abstract IHostedService CreateConsumer(
        IServiceProvider provider,
        TConnection brokerConnection,
        BrokerSettings connection);

    protected abstract IHealthCheck CreateHealthCheck(TConnection brokerConnection);
}