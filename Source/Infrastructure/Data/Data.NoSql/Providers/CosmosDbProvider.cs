using Data.NoSql.DynamicData;
using Data.NoSql.HealthChecks;
using Data.NoSql.Repositories;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Data.NoSql.Providers;

public sealed class CosmosDbProvider : NoSqlDatabaseProviderBase<CosmosClient>
{
    public override DatabaseType ProviderType => DatabaseType.CosmosDb;

    public override Type RepositoryType => typeof(CosmosDocumentRepository<>);

    public override void Validate(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.ConnectionString))
        {
            throw new InvalidOperationException(
                $"CosmosDb requires {connection.KeyOf(nameof(connection.ConnectionString))}.");
        }

        if (string.IsNullOrWhiteSpace(connection.Database))
        {
            throw new InvalidOperationException(
                $"CosmosDb requires {connection.KeyOf(nameof(connection.Database))}.");
        }
    }

    protected override CosmosClient CreateClient(DatabaseSettings connection) =>
        new(
            connection.ConnectionString,
            new CosmosClientOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(connection.TimeoutSeconds),
                UseSystemTextJsonSerializerWithOptions = Domain.Extensions.JsonDefaults.Standard,
                ConnectionMode = ConnectionMode.Gateway,
            });

    protected override void RegisterServices(IServiceCollection services, DatabaseSettings connection) =>
        services.AddKeyedSingleton<Database>(connection.Id, (provider, key) =>
            provider.GetRequiredKeyedService<CosmosClient>(key).GetDatabase(connection.Database));

    protected override IDynamicDataSource CreateDynamicDataSource(
        IServiceProvider provider,
        DatabaseSettings connection,
        GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        return new CosmosDynamicDataSource(connection.Id, provider.GetRequiredKeyedService<Database>(connection.Id), options.DocumentSampleSize);
    }

    protected override IHealthCheck CreateHealthCheck(IServiceProvider provider, string databaseId) =>
        new CosmosDbHealthCheck(provider.GetRequiredKeyedService<Database>(databaseId));
}