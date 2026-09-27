using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Caching;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Data.Cache.Providers;

public abstract class CacheStoreProviderBase : ICacheStoreProvider
{
    public abstract CacheType ProviderType { get; }

    public abstract bool IsDistributed { get; }

    public virtual void Validate(CacheSettings connection) =>
        ArgumentNullException.ThrowIfNull(connection);

    public void Register(IServiceCollection services, CacheSettings connection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);

        RegisterStore(services, connection);

        services.AddHealthChecks().Add(HealthCheckPolicy.Registration(
            DependencyKind.Cache,
            $"cache:{connection.Id}",
            provider => CreateHealthCheck(provider, connection.Id)));
    }

    protected abstract void RegisterStore(IServiceCollection services, CacheSettings connection);

    protected abstract IHealthCheck CreateHealthCheck(IServiceProvider provider, string cacheId);
}