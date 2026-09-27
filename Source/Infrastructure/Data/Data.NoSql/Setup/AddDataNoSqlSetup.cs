using Data.NoSql.Providers;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Data.NoSql.Setup;

public static class DataNoSqlSetup
{
    public static IServiceCollection AddDataNoSqlSetup(
        this IServiceCollection services,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        foreach (var connection in settings.Databases.Where(database => database.Type.Family() == DatabaseFamily.Document))
        {
            var provider = NoSqlProviderRegistry.Resolve(connection.Type);

            services.AddKeyedSingleton<INoSqlDatabaseProvider>(connection.Id, provider);

            provider.Register(services, connection);
        }

        return services;
    }
}