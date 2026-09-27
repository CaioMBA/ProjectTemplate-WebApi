using Data.Cache.Providers;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Data.Cache.Setup;

public static class DataCacheSetup
{
    public static IServiceCollection AddDataCacheSetup(
        this IServiceCollection services,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        foreach (var connection in settings.Caches)
        {
            var provider = CacheProviderRegistry.Resolve(connection.Type);

            services.AddKeyedSingleton(connection.Id, provider);

            provider.Register(services, connection);
        }

        return services;
    }
}