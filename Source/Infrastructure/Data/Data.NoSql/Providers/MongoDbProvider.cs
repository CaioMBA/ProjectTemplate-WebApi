using Data.NoSql.DynamicData;
using Data.NoSql.HealthChecks;
using Data.NoSql.Repositories;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;

namespace Data.NoSql.Providers;

public sealed class MongoDbProvider : NoSqlDatabaseProviderBase<IMongoClient>
{
    public override DatabaseType ProviderType => DatabaseType.MongoDb;

    public override Type RepositoryType => typeof(MongoDocumentRepository<>);

    public override void Validate(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.ConnectionString))
        {
            throw new InvalidOperationException(
                $"MongoDb requires {connection.KeyOf(nameof(connection.ConnectionString))}.");
        }

        if (string.IsNullOrWhiteSpace(connection.Database))
        {
            throw new InvalidOperationException(
                $"MongoDb requires {connection.KeyOf(nameof(connection.Database))}.");
        }
    }

    protected override IMongoClient CreateClient(DatabaseSettings connection)
    {
        var settings = MongoClientSettings.FromConnectionString(connection.ConnectionString);

        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(connection.TimeoutSeconds);

        return new MongoClient(settings);
    }

    protected override void RegisterServices(IServiceCollection services, DatabaseSettings connection) =>
        services.AddKeyedSingleton<IMongoDatabase>(connection.Id, (provider, key) =>
            provider.GetRequiredKeyedService<IMongoClient>(key).GetDatabase(connection.Database));

    protected override IDynamicDataSource CreateDynamicDataSource(
        IServiceProvider provider,
        DatabaseSettings connection,
        GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        return new MongoDynamicDataSource(connection.Id, provider.GetRequiredKeyedService<IMongoDatabase>(connection.Id), options.DocumentSampleSize);
    }

    protected override IHealthCheck CreateHealthCheck(IServiceProvider provider, string databaseId) =>
        new MongoDbHealthCheck(provider.GetRequiredKeyedService<IMongoDatabase>(databaseId));
}