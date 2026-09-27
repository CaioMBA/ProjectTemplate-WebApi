using Domain.Enums;
using Domain.Models.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Data.Cache.Providers;

public sealed class MemoryCacheStoreProvider : CacheStoreProviderBase
{
    public override CacheType ProviderType => CacheType.Memory;

    public override bool IsDistributed => false;

    protected override void RegisterStore(
        IServiceCollection services,
        CacheSettings connection) =>
        services.AddKeyedSingleton<IDistributedCache>(
            connection.Id,
            (_, _) => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

    protected override IHealthCheck CreateHealthCheck(IServiceProvider provider, string cacheId) =>
        new InProcessHealthCheck();

    private sealed class InProcessHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy("In-process memory cache."));
    }
}