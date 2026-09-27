using Domain.Abstractions;
using Domain.Connections;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Data.NoSql.Providers;

public abstract class NoSqlDatabaseProviderBase<TClient> : INoSqlDatabaseProvider
    where TClient : class
{
    public abstract DatabaseType ProviderType { get; }

    public abstract Type RepositoryType { get; }

    public abstract void Validate(DatabaseSettings connection);

    public void Register(IServiceCollection services, DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);

        var databaseId = connection.Id;

        services.AddKeyedSingleton(databaseId, (_, _) => new LazyConnection<TClient>(() =>
        {
            Validate(connection);

            return CreateClient(connection);
        }));

        services.AddKeyedSingleton<TClient>(databaseId, (provider, key) =>
            provider.GetRequiredKeyedService<LazyConnection<TClient>>(key).Value);

        RegisterServices(services, connection);

        services.AddKeyedScoped(typeof(IDocumentRepository<>), databaseId, RepositoryType);

        services.AddKeyedScoped<IDynamicDataSource>(databaseId, (provider, _) => CreateDynamicDataSource(
            provider,
            connection,
            provider.GetService<StartupSettings>()?.Api.GraphQlServer.AutoSchema ?? new GraphQlAutoSchemaOptions()));

        services.AddHealthChecks().Add(HealthCheckPolicy.Registration(
            DependencyKind.DocumentDatabase,
            $"db:{databaseId}",
            provider => CreateHealthCheck(provider, databaseId)));
    }

    protected abstract TClient CreateClient(DatabaseSettings connection);

    protected abstract void RegisterServices(IServiceCollection services, DatabaseSettings connection);

    protected abstract IHealthCheck CreateHealthCheck(IServiceProvider provider, string databaseId);

    protected abstract IDynamicDataSource CreateDynamicDataSource(
        IServiceProvider provider,
        DatabaseSettings connection,
        GraphQlAutoSchemaOptions options);
}