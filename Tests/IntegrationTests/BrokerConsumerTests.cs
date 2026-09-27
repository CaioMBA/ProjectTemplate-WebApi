using Data.Broker.Consumers;
using Data.Broker.Providers;
using Domain.Enums;
using Domain.Events.Integration;
using Domain.Integration;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Integration;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.RabbitMq;

namespace IntegrationTests;

public sealed class BrokerConsumerTests : IAsyncLifetime, IDisposable
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4.1-management").Build();

    private readonly CancellationTokenSource _cancellation = new(TimeSpan.FromMinutes(3));

    private ServiceProvider _services = null!;

    private IBrokerProvider _provider = null!;

    private CancellationToken Token => _cancellation.Token;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _container.StartAsync(Token).ConfigureAwait(false);

        var uri = new Uri(_container.GetConnectionString());

        var connection = new BrokerSettings
        {
            Id = "EVENTS",
            Type = BrokerType.RabbitMq,
            Host = uri.Host,
            Port = uri.Port,
            Username = Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[0]),
            Password = Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[1]),
            EnableConsumer = true,
            RabbitMq = new RabbitMqBrokerOptions { Exchange = "tests.events", Queue = "tests.inbox" },
        };

        var collection = new ServiceCollection();

        collection.AddLogging(builder => builder.AddDebug());

        collection.AddSingleton<IIntegrationEventRegistry>(
            _ => IntegrationEventRegistry.FromAssemblies(typeof(ProductCreatedIntegrationEvent).Assembly));
        collection.AddScoped<IntegrationEventDeserializer>();
        collection.AddSingleton<RecordingDispatcher>();
        collection.AddScoped<IIntegrationEventDispatcher>(
            provider => provider.GetRequiredService<RecordingDispatcher>());

        _provider = BrokerProviderRegistry.Resolve(BrokerType.RabbitMq);

        _provider.Register(collection, connection);

        _services = collection.BuildServiceProvider();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _services.DisposeAsync().ConfigureAwait(false);
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose() => _cancellation.Dispose();

    [Fact]
    public async Task APublishedEventIsConsumedAndDispatchedToItsHandler()
    {
        var consumer = _services.GetServices<IHostedService>()
            .Single(service => service.GetType() == _provider.ConsumerType);

        await consumer.StartAsync(Token);

        var published = new ProductCreatedIntegrationEvent
        {
            ProductId = Guid.CreateVersion7(),
            Sku = "SKU-CONSUMED",
            ProductName = "Consumed Widget",
        };

        var bus = _services.GetRequiredKeyedService<IEventBus>("EVENTS");
        var recorder = _services.GetRequiredService<RecordingDispatcher>();

        IIntegrationEvent? received = null;

        for (var attempt = 0; attempt < 10 && received is null; attempt++)
        {
            await bus.PublishAsync(published, Token);

            received = await recorder.TryWaitForNextAsync(TimeSpan.FromSeconds(3), Token);
        }

        await consumer.StopAsync(Token);

        received.ShouldNotBeNull();
        received.ShouldBeOfType<ProductCreatedIntegrationEvent>();

        var typed = (ProductCreatedIntegrationEvent)received;

        typed.ProductId.ShouldBe(published.ProductId);
        typed.Sku.ShouldBe("SKU-CONSUMED");
        typed.ProductName.ShouldBe("Consumed Widget");
        typed.EventId.ShouldBe(published.EventId);
    }

    [Fact]
    public void TheRegistryKnowsEveryIntegrationEventDeclaredInDomain()
    {
        var registry = _services.GetRequiredService<IIntegrationEventRegistry>();

        registry.KnownEventTypes.ShouldContain(ProductCreatedIntegrationEvent.Name);
        registry.KnownEventTypes.ShouldContain(ProductPriceChangedIntegrationEvent.Name);

        registry.TryResolve(ProductCreatedIntegrationEvent.Name, out var clrType).ShouldBeTrue();
        clrType.ShouldBe(typeof(ProductCreatedIntegrationEvent));

        registry.TryResolve("not.a.real.event", out _).ShouldBeFalse();
    }
}

public sealed class RecordingDispatcher : IIntegrationEventDispatcher, IDisposable
{
    private readonly List<IIntegrationEvent> _received = [];

    private readonly SemaphoreSlim _signal = new(0);

    public Task DispatchAsync(
        IIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        lock (_received)
        {
            _received.Add(integrationEvent);
        }

        _signal.Release();

        return Task.CompletedTask;
    }

    public async Task<IIntegrationEvent?> TryWaitForNextAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!await _signal.WaitAsync(timeout, cancellationToken))
        {
            return null;
        }

        lock (_received)
        {
            return _received[^1];
        }
    }

    public void Dispose() => _signal.Dispose();
}
