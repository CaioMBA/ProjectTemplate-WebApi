using System.Collections.Concurrent;
using Data.Sql.EntityFrameworkContexts;
using Data.Sql.Outbox;
using Domain.Events.Integration;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Integration;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Requests.Products;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OutboxPublisherRoutingTests(ApiFactory factory)
{
    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ThePublisherSendsToTheBrokerItsDatabaseEntryNamesAndFollowsAChangeWithoutARestart()
    {
        var events = new RecordingBus();
        var audit = new RecordingBus();

        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddKeyedSingleton<IEventBus>("EVENTS", events);
            services.AddKeyedSingleton<IEventBus>("AUDIT", audit);
        }));

        var settings = new MutableMonitor(Settings(brokerId: "EVENTS"));

        using var publisher = new OutboxPublisher(
            app.Services.GetRequiredService<IServiceScopeFactory>(),
            settings,
            ProductsStore.DatabaseId,
            NullLogger<OutboxPublisher>.Instance);

        await publisher.StartAsync(CancellationToken.None);

        try
        {
            var first = await EnqueueAsync(app.Services);

            (await events.WaitForAsync(first)).ShouldBeTrue("the first event never reached the EVENTS broker");
            audit.Received.ShouldNotContain(first);

            settings.Current = Settings(brokerId: "AUDIT");

            var second = await EnqueueAsync(app.Services);

            (await audit.WaitForAsync(second)).ShouldBeTrue("after the change, the event never reached AUDIT");
            events.Received.ShouldNotContain(second);
        }
        finally
        {
            await publisher.StopAsync(CancellationToken.None);
        }
    }

    private static AppSettings Settings(string brokerId)
    {
        var database = new DatabaseSettings { Id = ProductsStore.DatabaseId, Type = Domain.Enums.DatabaseType.Postgresql };

        database.Sql.Outbox.Enabled = true;
        database.Sql.Outbox.BrokerId = brokerId;
        database.Sql.Outbox.PollIntervalSeconds = 1;

        return new AppSettings { Databases = [database] };
    }

    private static async Task<Guid> EnqueueAsync(IServiceProvider services)
    {
        var integrationEvent = new ProductCreatedIntegrationEvent
        {
            ProductId = Guid.NewGuid(),
            Sku = $"ROUTE-{Guid.NewGuid():N}"[..20],
            ProductName = "Routing probe",
        };

        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredKeyedService<IOutboxWriter>(ProductsStore.DatabaseId)
            .EnqueueAsync(integrationEvent);

        await scope.ServiceProvider
            .GetRequiredKeyedService<AppDbContext>(ProductsStore.DatabaseId)
            .SaveChangesAsync();

        return integrationEvent.EventId;
    }

    private sealed class RecordingBus : IEventBus
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource> _arrivals = new();

        public ConcurrentBag<Guid> Received { get; } = [];

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : IIntegrationEvent
        {
            Received.Add(integrationEvent.EventId);

            Arrival(integrationEvent.EventId).TrySetResult();

            return Task.CompletedTask;
        }

        public async Task PublishManyAsync(
            IEnumerable<IIntegrationEvent> integrationEvents,
            CancellationToken cancellationToken = default)
        {
            foreach (var integrationEvent in integrationEvents)
            {
                await PublishAsync(integrationEvent, cancellationToken);
            }
        }

        public async Task<bool> WaitForAsync(Guid eventId)
        {
            var arrival = Arrival(eventId).Task;

            return await Task.WhenAny(arrival, Task.Delay(_deadline)) == arrival;
        }

        private TaskCompletionSource Arrival(Guid eventId) =>
            _arrivals.GetOrAdd(eventId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private sealed class MutableMonitor(AppSettings initial) : IOptionsMonitor<AppSettings>
    {
        public AppSettings Current { get; set; } = initial;

        public AppSettings CurrentValue => Current;

        public AppSettings Get(string? name) => Current;

        public IDisposable? OnChange(Action<AppSettings, string?> listener) => null;
    }
}
