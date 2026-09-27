using Data.Cache.Setup;
using Domain.Interfaces.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Data.Cache.Modules;

public sealed class CacheModule : InfrastructureModuleBase
{
    public override string Name => "Cache";

    protected override IEnumerable<string> Entries(InfrastructureModuleContext context) =>
        context.Settings.Caches.Select(cache => $"{cache.Type}@{cache.Id}");

    protected override void RegisterModule(
        IServiceCollection services,
        InfrastructureModuleContext context) =>
        services.AddDataCacheSetup(context.Settings);
}