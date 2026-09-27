using Data.Broker.Providers;
using Data.Broker.Providers.Kafka;
using Data.Broker.Providers.RabbitMq;
using Data.Cache.Providers;
using Domain.Enums;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace UnitTests.Providers;

public sealed class CacheProviderRegistryTests
{
    [Fact]
    public void EveryCacheTypeResolvesToExactlyOneProvider()
    {
        foreach (var value in Enum.GetValues<CacheType>())
        {
            CacheProviderRegistry.All.Count(provider => provider.ProviderType == value).ShouldBe(1);

            CacheProviderRegistry.Resolve(value).ProviderType.ShouldBe(value);
        }
    }

    [Fact]
    public void MemoryProviderIsNotDistributed() =>
        CacheProviderRegistry.Resolve(CacheType.Memory).IsDistributed.ShouldBeFalse();

    [Fact]
    public void RedisProviderIsDistributed() =>
        CacheProviderRegistry.Resolve(CacheType.Redis).IsDistributed.ShouldBeTrue();

    [Fact]
    public void RedisValidationNamesTheEntryHostKey() =>
        Should.Throw<InvalidOperationException>(() =>
                CacheProviderRegistry.Resolve(CacheType.Redis).Validate(Cache(CacheType.Redis)))
            .Message.ShouldContain("Settings:Caches[Id=REDIS]:Host");

    [Fact]
    public void MemoryProviderIgnoresConnectionFieldsAndRegistersTheNativeDistributedCache()
    {
        var services = new ServiceCollection();

        CacheProviderRegistry.Resolve(CacheType.Memory).Register(services, Cache(CacheType.Memory));

        services.ShouldContain(descriptor => descriptor.ServiceType == typeof(IDistributedCache)
            && descriptor.IsKeyedService);
    }

    [Fact]
    public void RedisConfigurationCarriesTheEndpointDatabaseTimeoutAndClientName()
    {
        var connection = Cache(CacheType.Redis);
        connection.Host = "redis_cache";
        connection.Port = 6380;
        connection.TimeoutSeconds = 7;
        connection.Redis.InstanceName = "template:";
        connection.Redis.Database = 3;

        var configuration = RedisCacheStoreProvider.BuildConfiguration(connection);

        configuration.EndPoints.Count.ShouldBe(1);
        configuration.EndPoints[0].ToString()!.ShouldContain("6380");
        configuration.ClientName.ShouldBe("template");
        configuration.DefaultDatabase.ShouldBe(3);
        configuration.ConnectTimeout.ShouldBe(7000);
        configuration.AbortOnConnectFail.ShouldBeFalse();
    }

    [Fact]
    public void RedisConfigurationDefaultsThePortTo6379()
    {
        var connection = Cache(CacheType.Redis);
        connection.Host = "redis_cache";

        RedisCacheStoreProvider.BuildConfiguration(connection)
            .EndPoints[0].ToString()!.ShouldContain("6379");
    }

    private static CacheSettings Cache(CacheType type) =>
        new() { Id = type == CacheType.Redis ? "REDIS" : "MEMORY", Type = type };
}

public sealed class BrokerProviderRegistryTests
{
    [Fact]
    public void EveryBrokerTypeResolvesToExactlyOneProvider()
    {
        foreach (var value in Enum.GetValues<BrokerType>())
        {
            BrokerProviderRegistry.All.Count(provider => provider.ProviderType == value).ShouldBe(1);

            BrokerProviderRegistry.Resolve(value).ProviderType.ShouldBe(value);
        }
    }

    [Theory]
    [InlineData(BrokerType.RabbitMq, typeof(RabbitMqEventBus))]
    [InlineData(BrokerType.Kafka, typeof(KafkaEventBus))]
    public void ProviderMapsToItsEventBus(BrokerType providerType, Type expected) =>
        BrokerProviderRegistry.Resolve(providerType).EventBusType.ShouldBe(expected);

    [Fact]
    public void RabbitMqValidationNamesTheEntryHostKey() =>
        Should.Throw<InvalidOperationException>(() =>
                BrokerProviderRegistry.Resolve(BrokerType.RabbitMq)
                    .Validate(Broker(BrokerType.RabbitMq)))
            .Message.ShouldContain("Settings:Brokers[Id=EVENTS]:Host");

    [Fact]
    public void RabbitMqValidationRequiresAnExchange()
    {
        var connection = Broker(BrokerType.RabbitMq);
        connection.Host = "rabbitmq";
        connection.RabbitMq.Exchange = " ";

        Should.Throw<InvalidOperationException>(() =>
                BrokerProviderRegistry.Resolve(BrokerType.RabbitMq).Validate(connection))
            .Message.ShouldContain("Settings:Brokers[Id=EVENTS]:RabbitMq:Exchange");
    }

    [Fact]
    public void RabbitMqValidationRequiresAQueueOnlyWhenTheConsumerIsEnabled()
    {
        var connection = Broker(BrokerType.RabbitMq);
        connection.Host = "rabbitmq";
        connection.RabbitMq.Queue = string.Empty;

        var provider = BrokerProviderRegistry.Resolve(BrokerType.RabbitMq);

        Should.NotThrow(() => provider.Validate(connection));

        connection.EnableConsumer = true;

        Should.Throw<InvalidOperationException>(() => provider.Validate(connection))
            .Message.ShouldContain("RabbitMq:Queue");
    }

    [Fact]
    public void KafkaValidationRequiresBootstrapServers() =>
        Should.Throw<InvalidOperationException>(() =>
                BrokerProviderRegistry.Resolve(BrokerType.Kafka)
                    .Validate(Broker(BrokerType.Kafka)))
            .Message.ShouldContain("Settings:Brokers[Id=STREAM]:Kafka:BootstrapServers");

    [Fact]
    public void KafkaValidationRequiresATopicPrefixAndAConsumerGroupWhenConsuming()
    {
        var provider = BrokerProviderRegistry.Resolve(BrokerType.Kafka);

        var noPrefix = Broker(BrokerType.Kafka);
        noPrefix.Kafka.BootstrapServers = "kafka:9092";
        noPrefix.Kafka.TopicPrefix = string.Empty;

        Should.Throw<InvalidOperationException>(() => provider.Validate(noPrefix))
            .Message.ShouldContain("Kafka:TopicPrefix");

        var noGroup = Broker(BrokerType.Kafka);
        noGroup.Kafka.BootstrapServers = "kafka:9092";
        noGroup.Kafka.ConsumerGroup = string.Empty;

        Should.NotThrow(() => provider.Validate(noGroup));

        noGroup.EnableConsumer = true;

        Should.Throw<InvalidOperationException>(() => provider.Validate(noGroup))
            .Message.ShouldContain("Kafka:ConsumerGroup");
    }

    [Fact]
    public void BothProvidersRegisterAKeyedEventBusAndAHealthCheckUnderTheEntryId()
    {
        var rabbit = Broker(BrokerType.RabbitMq);
        rabbit.Host = "rabbitmq";

        var kafka = Broker(BrokerType.Kafka);
        kafka.Kafka.BootstrapServers = "kafka:9092";

        foreach (var connection in new[] { rabbit, kafka })
        {
            var services = new ServiceCollection();

            BrokerProviderRegistry.Resolve(connection.Type).Register(services, connection);

            services.ShouldContain(
                descriptor => descriptor.ServiceType == typeof(IEventBus)
                    && descriptor.IsKeyedService
                    && Equals(descriptor.ServiceKey, connection.Id),
                $"{connection.Type} did not register an IEventBus keyed by '{connection.Id}'.");

            services.ShouldNotContain(
                descriptor => descriptor.ServiceType == typeof(IEventBus) && !descriptor.IsKeyedService,
                $"{connection.Type} registered an unkeyed IEventBus.");

            services.ShouldContain(
                descriptor => descriptor.ServiceType.Name.Contains("IConfigureOptions", StringComparison.Ordinal),
                $"{connection.Type} did not register a health check.");
        }
    }
    [Fact]
    public void KafkaProducerConfigEnablesIdempotenceAndFullAcks()
    {
        var connection = Broker(BrokerType.Kafka);
        connection.Kafka.BootstrapServers = "kafka:9092";

        var config = KafkaBrokerProvider.BuildProducerConfig(connection);

        config.BootstrapServers.ShouldBe("kafka:9092");
        config.EnableIdempotence.ShouldBe(true);
        config.Acks.ShouldBe(Confluent.Kafka.Acks.All);
    }

    [Fact]
    public void KafkaProducerConfigUsesScramWhenCredentialsArePresent()
    {
        var connection = Broker(BrokerType.Kafka);
        connection.Kafka.BootstrapServers = "kafka:9092";
        connection.Username = "app";
        connection.Password = "secret";
        connection.UseSsl = true;

        var config = KafkaBrokerProvider.BuildProducerConfig(connection);

        config.SaslMechanism.ShouldBe(Confluent.Kafka.SaslMechanism.ScramSha512);
        config.SecurityProtocol.ShouldBe(Confluent.Kafka.SecurityProtocol.SaslSsl);
    }

    [Theory]
    [InlineData("app.events", "ProductCreated", "app.events.ProductCreated")]
    [InlineData("", "ProductCreated", "ProductCreated")]
    public void KafkaTopicsRoundTripBetweenEventTypeAndTopic(string prefix, string eventType, string topic)
    {
        KafkaTopics.For(prefix, eventType).ShouldBe(topic);

        KafkaTopics.EventTypeOf(prefix, topic).ShouldBe(eventType);
    }

    private static BrokerSettings Broker(BrokerType type) =>
        new() { Id = type == BrokerType.Kafka ? "STREAM" : "EVENTS", Type = type };
}
