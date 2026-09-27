using Application.Features.Products;
using CrossCutting.Setup;
using Data.Broker.Providers.RabbitMq;
using Data.Sql.DatabaseAccess;
using Data.Sql.EntityFrameworkContexts;
using Data.Sql.EntityFrameworkContexts.Contexts;
using Data.Sql.Outbox;
using Domain.Abstractions;
using Domain.DTOs;
using Domain.Entities;
using Domain.Enums;
using Domain.Interfaces.Broker;
using Domain.Interfaces.Caching;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Requests.Products;
using Domain.Models.Responses;
using Domain.Results;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Shouldly;
using StackExchange.Redis;
using UnitTests.TestSupport;

namespace UnitTests.Modules;

internal sealed class StubApplicationLifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    public void StopApplication()
    {
    }
}

public sealed class ContainerProbeDocument
{
    public Guid Id { get; set; }
}

public sealed class ContainerResolutionTests
{
    private static readonly Type[] _connectionBoundServices =
    [
        typeof(AppDbContext),
        typeof(IRepository<,>),
        typeof(IUnitOfWork),
        typeof(IOutboxWriter),
        typeof(IOutboxMaintenance),
        typeof(ISqlDatabaseAccess),
        typeof(IDynamicDataSource),
        typeof(ISqlSyntax),
        typeof(ISqlDialect),
        typeof(ISqlDatabaseProvider),
        typeof(CachedPageStreamer),
        typeof(IDocumentRepository<>),
        typeof(IMongoDatabase),
        typeof(IMongoClient),
        typeof(IDistributedCache),
        typeof(IConnectionMultiplexer),
        typeof(ICacheStoreProvider),
        typeof(IEventBus),
        typeof(IBrokerProvider),
        typeof(RabbitMqConnectionProvider),
    ];

    private static Dictionary<string, string?> Products() => new()
    {
        ["Settings:Databases:0:Id"] = ProductsStore.DatabaseId,
        ["Settings:Databases:0:Type"] = "Postgresql",
        ["Settings:Databases:0:Host"] = "localhost",
        ["Settings:Databases:0:Database"] = "container_resolution_probe",
        ["Settings:Databases:0:Username"] = "probe",
        ["Settings:Databases:0:Password"] = "plain:probe",
        ["Settings:Databases:0:Sql:PagedCache:CacheId"] = ProductsStore.CacheId,
        ["Settings:Caches:0:Id"] = ProductsStore.CacheId,
        ["Settings:Caches:0:Type"] = "Memory",
    };

    private static Dictionary<string, string?> Everything() => new(Products())
    {
        ["Settings:Databases:1:Id"] = "DOCUMENTS",
        ["Settings:Databases:1:Type"] = "MongoDb",
        ["Settings:Databases:2:Id"] = "REPORTS",
        ["Settings:Databases:2:Type"] = "Sqlite",
        ["Settings:Databases:2:Database"] = "reports_probe.db",
        ["Settings:Caches:1:Id"] = "SHARED",
        ["Settings:Caches:1:Type"] = "Redis",
        ["Settings:Caches:2:Id"] = "SCRATCH",
        ["Settings:Caches:2:Type"] = "Memory",
        ["Settings:Brokers:0:Id"] = "EVENTS",
        ["Settings:Brokers:0:Type"] = "RabbitMq",
        ["Settings:Brokers:0:Host"] = "localhost",
        ["Settings:Brokers:0:RabbitMq:Exchange"] = "probe.events",
        ["Settings:Brokers:1:Id"] = "STREAM",
        ["Settings:Brokers:1:Type"] = "Kafka",
    };

    private static ServiceCollection BuildServices(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddSingleton<IHostApplicationLifetime, StubApplicationLifetime>();

        services.AddCrossCuttingSetup(
            configuration,
            TestStartup.From(configuration),
            NullLogger.Instance);

        return services;
    }

    private static ServiceProvider BuildContainer(Dictionary<string, string?> values, bool validateOnBuild = false) =>
        BuildServices(values).BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = validateOnBuild,
        });

    [Fact]
    public void SenderResolvesWithNoEntries()
    {
        using var provider = BuildContainer([]);

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<ISender>().ShouldNotBeNull();
    }

    [Fact]
    public async Task ProductHandlersAndPipelineResolveAgainstTheEntriesTheyName()
    {
        var provider = BuildContainer(Products(), validateOnBuild: true);

        await using (provider.ConfigureAwait(true))
        {
            var scope = provider.CreateAsyncScope();

            await using (scope.ConfigureAwait(true))
            {
                scope.ServiceProvider
                    .GetService<IRequestHandler<GetProductByIdQuery, Result<ProductDto>>>()
                    .ShouldNotBeNull();

                scope.ServiceProvider
                    .GetService<IRequestHandler<ListProductsQuery, Result<PagedResult<ProductDto>>>>()
                    .ShouldNotBeNull();

                var behaviors = scope.ServiceProvider
                    .GetServices<IPipelineBehavior<GetProductByIdQuery, Result<ProductDto>>>()
                    .ToList();

                behaviors.Count.ShouldBe(6);

                scope.ServiceProvider.GetKeyedService<IDistributedCache>(ProductsStore.CacheId).ShouldNotBeNull();

                scope.ServiceProvider
                    .GetKeyedService<CachedPageStreamer>(ProductsStore.DatabaseId)
                    .ShouldNotBeNull();
            }
        }
    }

    [Fact]
    public void NoConnectionBoundServiceIsRegisteredWithoutAnId()
    {
        var services = BuildServices(Everything());

        var unkeyed = services
            .Where(descriptor => !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => _connectionBoundServices.Contains(type.IsGenericType && !type.IsGenericTypeDefinition
                ? type.GetGenericTypeDefinition()
                : type))
            .Select(type => type.Name)
            .ToList();

        unkeyed.ShouldBeEmpty(
            "features must name the entry they use, so a connection-bound service may only be "
            + $"registered under an entry Id. Unkeyed: {string.Join(", ", unkeyed)}");
    }

    [Fact]
    public async Task UnkeyedResolutionFindsNothing()
    {
        var provider = BuildContainer(Everything());

        await using (provider.ConfigureAwait(true))
        {
            var scope = provider.CreateAsyncScope();

            await using (scope.ConfigureAwait(true))
            {
                scope.ServiceProvider.GetService<IDistributedCache>().ShouldBeNull();
                scope.ServiceProvider.GetService<IUnitOfWork>().ShouldBeNull();
                scope.ServiceProvider.GetService<IEventBus>().ShouldBeNull();
                scope.ServiceProvider.GetService<IDocumentRepository<ContainerProbeDocument>>().ShouldBeNull();
            }
        }
    }

    [Fact]
    public async Task TwoSqlEnginesResolveSideBySide()
    {
        var provider = BuildContainer(Everything(), validateOnBuild: true);

        await using (provider.ConfigureAwait(true))
        {
            var scope = provider.CreateAsyncScope();

            await using (scope.ConfigureAwait(true))
            {
                var services = scope.ServiceProvider;

                services.GetRequiredKeyedService<AppDbContext>(ProductsStore.DatabaseId)
                    .ShouldBeOfType<PostgresqlAppDbContext>();

                services.GetRequiredKeyedService<AppDbContext>("REPORTS")
                    .ShouldBeOfType<SqliteAppDbContext>();

                services.GetRequiredKeyedService<ISqlSyntax>(ProductsStore.DatabaseId).BooleanLiteral(true)
                    .ShouldBe("TRUE");

                services.GetRequiredKeyedService<ISqlSyntax>("REPORTS").BooleanLiteral(true)
                    .ShouldBe("1");

                services.GetRequiredKeyedService<IRepository<ProductEntity, Guid>>("REPORTS").ShouldNotBeNull();

                services.GetRequiredKeyedService<IUnitOfWork>(ProductsStore.DatabaseId)
                    .ShouldNotBeSameAs(services.GetRequiredKeyedService<IUnitOfWork>("REPORTS"));
            }
        }
    }

    [Fact]
    public async Task ABrokenEntryNothingUsesDoesNotBlockStartupButFailsOnFirstUseWithItsKey()
    {
        var provider = BuildContainer(Everything(), validateOnBuild: true);

        await using (provider.ConfigureAwait(true))
        {
            Should.Throw<InvalidOperationException>(() =>
                    provider.GetRequiredKeyedService<IMongoDatabase>("DOCUMENTS"))
                .Message.ShouldContain("Settings:Databases[Id=DOCUMENTS]:ConnectionString");

            Should.Throw<InvalidOperationException>(() =>
                    provider.GetRequiredKeyedService<IDistributedCache>("SHARED"))
                .Message.ShouldContain("Settings:Caches[Id=SHARED]:Host");

            Should.Throw<InvalidOperationException>(() =>
                    provider.GetRequiredKeyedService<IEventBus>("STREAM"))
                .Message.ShouldContain("Settings:Brokers[Id=STREAM]:Kafka:BootstrapServers");
        }
    }

    [Fact]
    public async Task TwoMemoryCachesAreSeparateStores()
    {
        var provider = BuildContainer(Everything());

        await using (provider.ConfigureAwait(true))
        {
            var main = provider.GetRequiredKeyedService<IDistributedCache>(ProductsStore.CacheId);
            var scratch = provider.GetRequiredKeyedService<IDistributedCache>("SCRATCH");

            main.ShouldNotBeSameAs(scratch);

            await main.SetStringAsync("probe", "main");

            (await scratch.GetStringAsync("probe")).ShouldBeNull();
        }
    }

    [Fact]
    public async Task EachBrokerAndCacheResolvesItsOwnEngine()
    {
        var provider = BuildContainer(Everything());

        await using (provider.ConfigureAwait(true))
        {
            provider.GetRequiredKeyedService<IEventBus>("EVENTS").GetType().Name.ShouldBe("RabbitMqEventBus");

            provider.GetRequiredKeyedService<ICacheStoreProvider>("SHARED").ProviderType.ShouldBe(CacheType.Redis);

            provider.GetRequiredKeyedService<ICacheStoreProvider>(ProductsStore.CacheId)
                .ProviderType.ShouldBe(CacheType.Memory);
        }
    }

    [Fact]
    public async Task ConsumersAndOutboxPublishersRunOnlyWhereAnEntryAsksForThem()
    {
        var quiet = BuildContainer(Everything());

        await using (quiet.ConfigureAwait(true))
        {
            quiet.GetServices<IHostedService>().ShouldNotContain(service =>
                service is RabbitMqEventConsumer || service is OutboxPublisher);
        }

        var busy = BuildContainer(new Dictionary<string, string?>(Everything())
        {
            ["Settings:Brokers:0:EnableConsumer"] = "true",
            ["Settings:Databases:0:Sql:Outbox:Enabled"] = "true",
            ["Settings:Databases:0:Sql:Outbox:BrokerId"] = "EVENTS",
        });

        await using (busy.ConfigureAwait(true))
        {
            var hosted = busy.GetServices<IHostedService>().ToList();

            hosted.Count(service => service is RabbitMqEventConsumer).ShouldBe(1);
            hosted.Count(service => service is OutboxPublisher).ShouldBe(1);
        }
    }

    [Fact]
    public async Task EveryEntryGetsTheHealthRuleOfItsDependencyKind()
    {
        var provider = BuildContainer(Everything());

        await using (provider.ConfigureAwait(true))
        {
            var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
                .Value.Registrations
                .ToDictionary(registration => registration.Name);

            registrations.Keys.ShouldBe(
                ["db:DEFAULT", "db:REPORTS", "db:DOCUMENTS", "cache:DEFAULT", "cache:SHARED", "cache:SCRATCH", "broker:EVENTS", "broker:STREAM"],
                ignoreOrder: true);

            var expected = new Dictionary<string, DependencyKind>
            {
                ["db:DEFAULT"] = DependencyKind.Database,
                ["db:REPORTS"] = DependencyKind.Database,
                ["db:DOCUMENTS"] = DependencyKind.DocumentDatabase,
                ["cache:DEFAULT"] = DependencyKind.Cache,
                ["cache:SHARED"] = DependencyKind.Cache,
                ["cache:SCRATCH"] = DependencyKind.Cache,
                ["broker:EVENTS"] = DependencyKind.Broker,
                ["broker:STREAM"] = DependencyKind.Broker,
            };

            foreach (var (name, kind) in expected)
            {
                var rule = HealthCheckPolicy.For(kind);

                registrations[name].FailureStatus.ShouldBe(rule.FailureStatus, name);
                registrations[name].Tags.ShouldBe(rule.Tags, ignoreOrder: true, customMessage: name);
            }
        }
    }

    [Fact]
    public async Task ABrokenEntryIsProbedAndReportsItsKeyWithTheStatusOfItsKind()
    {
        var provider = BuildContainer(Everything());

        await using (provider.ConfigureAwait(true))
        {
            string[] broken = ["db:DOCUMENTS", "cache:SHARED", "broker:STREAM"];

            var report = await provider.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(registration => broken.Contains(registration.Name));

            report.Entries["db:DOCUMENTS"].Status.ShouldBe(HealthStatus.Unhealthy);
            report.Entries["db:DOCUMENTS"].Exception!.Message
                .ShouldContain("Settings:Databases[Id=DOCUMENTS]:ConnectionString");

            report.Entries["cache:SHARED"].Status.ShouldBe(HealthStatus.Degraded);
            report.Entries["cache:SHARED"].Exception!.Message.ShouldContain("Settings:Caches[Id=SHARED]:Host");

            report.Entries["broker:STREAM"].Status.ShouldBe(HealthStatus.Unhealthy);
            report.Entries["broker:STREAM"].Exception!.Message
                .ShouldContain("Settings:Brokers[Id=STREAM]:Kafka:BootstrapServers");
        }
    }
    [Fact]
    public void DuplicateDatabaseIdsFailWhenSettingsAreBound()
    {
        var values = new Dictionary<string, string?>(Products())
        {
            ["Settings:Databases:1:Id"] = "default",
            ["Settings:Databases:1:Type"] = "MongoDb",
        };

        Should.Throw<InvalidOperationException>(() => BuildContainer(values))
            .Message.ShouldContain("duplicate");
    }

    [Fact]
    public void AnOutboxPointingAtAMissingBrokerFailsWhenSettingsAreBound()
    {
        var values = new Dictionary<string, string?>(Products())
        {
            ["Settings:Databases:0:Sql:Outbox:Enabled"] = "true",
            ["Settings:Databases:0:Sql:Outbox:BrokerId"] = "NOPE",
        };

        Should.Throw<InvalidOperationException>(() => BuildContainer(values))
            .Message.ShouldContain("Settings:Databases[Id=DEFAULT]:Sql:Outbox:BrokerId");
    }
}
