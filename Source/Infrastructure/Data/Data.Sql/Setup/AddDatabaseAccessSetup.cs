using Data.Sql.DatabaseAccess;
using Data.Sql.EntityFrameworkContexts;
using Domain.Connections;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Data.Sql.Setup;

public static class DatabaseAccessSetup
{
    public static IServiceCollection AddDatabaseAccessSetup(
        this IServiceCollection services,
        DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);

        var databaseId = connection.Id;

        services.AddKeyedSingleton<ISqlDatabaseProvider>(databaseId, (provider, key) =>
            provider.GetRequiredKeyedService<SqlDatabaseHandle>(key).Provider);

        services.AddKeyedSingleton<ISqlDialect>(databaseId, (provider, key) =>
            provider.GetRequiredKeyedService<SqlDatabaseHandle>(key).Provider.Dialect);

        services.AddKeyedSingleton<ISqlSyntax>(databaseId, (provider, key) =>
            provider.GetRequiredKeyedService<ISqlDialect>(key));

        services.AddKeyedSingleton(databaseId, (provider, key) =>
            new SqlConnectionString(provider.GetRequiredKeyedService<SqlDatabaseHandle>(key).Registration.ConnectionString));

        services.AddKeyedScoped(databaseId, (provider, key) =>
            new SqlEntityMapFactory(provider.GetRequiredKeyedService<AppDbContext>(key).Model));

        services.AddKeyedSingleton(databaseId, (provider, key) =>
            new SqlCrudStatementFactory(provider.GetRequiredKeyedService<ISqlDialect>(key)));

        services.AddKeyedSingleton(databaseId, (provider, key) =>
            new SqlDebugFormatter(provider.GetRequiredKeyedService<ISqlDialect>(key), connection.Sql));

        services.AddKeyedSingleton(databaseId, (provider, _) => new CachedPageStreamer(
            provider.GetRequiredService<IServiceScopeFactory>(),
            () => ResolvePageCache(provider, connection),
            provider.GetRequiredService<IHostApplicationLifetime>(),
            connection.Sql.PagedCache,
            databaseId,
            $"{connection.Type}:{databaseId}",
            provider.GetRequiredService<ILogger<CachedPageStreamer>>()));

        services.AddKeyedScoped<ISqlDatabaseAccess>(databaseId, (provider, key) => new SqlDatabaseAccess(
            provider.GetRequiredKeyedService<ISqlDatabaseProvider>(key),
            provider.GetRequiredKeyedService<SqlConnectionString>(key),
            provider.GetRequiredKeyedService<SqlEntityMapFactory>(key),
            provider.GetRequiredKeyedService<SqlCrudStatementFactory>(key),
            provider.GetRequiredKeyedService<SqlDebugFormatter>(key),
            provider.GetRequiredKeyedService<CachedPageStreamer>(key),
            provider.GetRequiredService<ILogger<SqlDatabaseAccess>>()));

        return services;
    }

    private static IDistributedCache ResolvePageCache(IServiceProvider provider, DatabaseSettings connection)
    {
        var cacheId = connection.Sql.PagedCache.CacheId;

        if (string.IsNullOrWhiteSpace(cacheId))
        {
            throw new InvalidOperationException(
                $"Paged caching on database '{connection.Id}' needs a cache. Set "
                + $"{connection.KeyOf("Sql:PagedCache:CacheId")} to an Id in Settings:Caches.");
        }

        return provider.RequireKeyed<IDistributedCache>(cacheId, KeyedConnections.Caches);
    }
}