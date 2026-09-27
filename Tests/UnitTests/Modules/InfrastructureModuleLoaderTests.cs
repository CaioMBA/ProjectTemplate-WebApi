using CrossCutting.Modules;
using Domain.Enums;
using Domain.Interfaces.Modules;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using CacheModule = Data.Cache.Modules.CacheModule;

namespace UnitTests.Modules;

public sealed class InfrastructureModuleLoaderTests
{
    private static InfrastructureModuleContext Context(Action<AppSettings>? configure = null)
    {
        var settings = new AppSettings();

        configure?.Invoke(settings);

        return new InfrastructureModuleContext(settings, new ObservabilityOptions());
    }

    [Fact]
    public void DefaultLoaderExposesEveryModuleExactlyOnce()
    {
        var loader = new InfrastructureModuleLoader();

        loader.Modules.Count.ShouldBe(8);

        loader.Modules.Select(module => module.Name).OrderBy(name => name, StringComparer.Ordinal).ShouldBe(
            ["Broker", "Cache", "GraphqlApi", "GrpcApi", "NoSql", "RestApi", "Scheduling", "Sql"]);
    }

    [Fact]
    public void NothingIsRegisteredWhenSettingsListNoEntries()
    {
        var services = new ServiceCollection();

        var loaded = new InfrastructureModuleLoader().Load(services, Context());

        loaded.ShouldBeEmpty();
    }

    [Fact]
    public void AModuleTurnsOnWhenItsListHasAMatchingEntry()
    {
        var services = new ServiceCollection();

        var loaded = new InfrastructureModuleLoader().Load(
            services,
            Context(settings =>
            {
                settings.Caches.Add(new CacheSettings { Id = "LOCAL", Type = CacheType.Memory });
                settings.Apis.Add(new ApiSettings { Id = "PARTNER", Protocol = ApiProtocolType.Rest });
            }));

        loaded.ShouldBe(["Cache(Memory@LOCAL)", "RestApi(PARTNER)"]);
    }

    [Fact]
    public void DatabaseModulesSplitTheListByFamily()
    {
        var services = new ServiceCollection();

        var loaded = new InfrastructureModuleLoader().Load(
            services,
            Context(settings =>
            {
                settings.Databases.Add(new DatabaseSettings { Id = "MAIN", Type = DatabaseType.Sqlite });
                settings.Databases.Add(new DatabaseSettings { Id = "DOCS", Type = DatabaseType.MongoDb });
            }));

        loaded.ShouldBe(["Sql(Sqlite@MAIN)", "NoSql(MongoDb@DOCS)"]);
    }

    [Fact]
    public void DuplicateModuleNamesAreRejected()
    {
        var modules = new IInfrastructureModule[]
        {
            new CacheModule(),
            new CacheModule(),
        };

        Should.Throw<InvalidOperationException>(() => new InfrastructureModuleLoader(modules))
            .Message.ShouldContain("Cache");
    }

    [Fact]
    public void RegisteringAModuleWithNoEntriesDirectlyThrows()
    {
        var module = new CacheModule();

        Should.Throw<InvalidOperationException>(() =>
                module.Register(new ServiceCollection(), Context()))
            .Message.ShouldContain("no matching entry");
    }

    [Fact]
    public void LoaderRegistersItselfSoModuleStateIsQueryableAtRuntime()
    {
        var services = new ServiceCollection();

        new InfrastructureModuleLoader().Load(services, Context());

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(InfrastructureModuleLoader));
    }
}