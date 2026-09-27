using Data.NoSql.DynamicData;
using System.Security.Cryptography.X509Certificates;
using Data.NoSql.HealthChecks;
using Data.NoSql.Repositories;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Raven.Client.Documents;

namespace Data.NoSql.Providers;

public sealed class RavenDbProvider : NoSqlDatabaseProviderBase<IDocumentStore>
{
    public override DatabaseType ProviderType => DatabaseType.RavenDb;

    public override Type RepositoryType => typeof(RavenDocumentRepository<>);

    public override void Validate(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.Cluster.Urls.Count == 0)
        {
            throw new InvalidOperationException(
                $"RavenDb requires at least one entry in {connection.KeyOf("Cluster:Urls")}.");
        }

        if (string.IsNullOrWhiteSpace(connection.Database))
        {
            throw new InvalidOperationException(
                $"RavenDb requires {connection.KeyOf(nameof(connection.Database))}.");
        }
    }

    protected override IDocumentStore CreateClient(DatabaseSettings connection)
    {
        var store = new DocumentStore
        {
            Urls = [.. connection.Cluster.Urls],
            Database = connection.Database,
        };

        if (!string.IsNullOrWhiteSpace(connection.Cluster.CertificatePath))
        {
            store.Certificate = X509CertificateLoader.LoadPkcs12FromFile(
                connection.Cluster.CertificatePath,
                connection.Cluster.CertificatePassword);
        }

        store.Initialize();

        return store;
    }

    protected override void RegisterServices(IServiceCollection services, DatabaseSettings connection)
    {
    }

    protected override IDynamicDataSource CreateDynamicDataSource(
        IServiceProvider provider,
        DatabaseSettings connection,
        GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        return new RavenDynamicDataSource(connection.Id, provider.GetRequiredKeyedService<IDocumentStore>(connection.Id), options.DocumentSampleSize);
    }

    protected override IHealthCheck CreateHealthCheck(IServiceProvider provider, string databaseId) =>
        new RavenDbHealthCheck(provider.GetRequiredKeyedService<IDocumentStore>(databaseId));
}