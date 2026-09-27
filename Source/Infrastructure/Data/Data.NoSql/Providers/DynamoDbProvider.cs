using Data.NoSql.DynamicData;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Data.NoSql.HealthChecks;
using Data.NoSql.Repositories;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Data.NoSql.Providers;

public sealed class DynamoDbProvider : NoSqlDatabaseProviderBase<IAmazonDynamoDB>
{
    public override DatabaseType ProviderType => DatabaseType.DynamoDb;

    public override Type RepositoryType => typeof(DynamoDocumentRepository<>);

    public override void Validate(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (string.IsNullOrWhiteSpace(connection.Cloud.ServiceUrl)
            && string.IsNullOrWhiteSpace(connection.Cloud.Region))
        {
            throw new InvalidOperationException(
                $"DynamoDb requires {connection.KeyOf("Cloud:Region")} or "
                + $"{connection.KeyOf("Cloud:ServiceUrl")}. Set Region for AWS or ServiceUrl for "
                + "DynamoDB Local.");
        }
    }

    protected override IAmazonDynamoDB CreateClient(DatabaseSettings connection) => CreateDynamoClient(connection);

    protected override void RegisterServices(IServiceCollection services, DatabaseSettings connection) =>
        services.AddKeyedSingleton(connection.Id, (_, _) => new DynamoTableOptions(
            connection.Database,
            connection.Cloud.MaxScanPageSize));

    protected override IDynamicDataSource CreateDynamicDataSource(
        IServiceProvider provider,
        DatabaseSettings connection,
        GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        return new DynamoDynamicDataSource(connection.Id, provider.GetRequiredKeyedService<IAmazonDynamoDB>(connection.Id), options.DocumentSampleSize, options.MaxScanItems);
    }

    protected override IHealthCheck CreateHealthCheck(IServiceProvider provider, string databaseId) =>
        new DynamoDbHealthCheck(provider.GetRequiredKeyedService<IAmazonDynamoDB>(databaseId));
    private static AmazonDynamoDBClient CreateDynamoClient(DatabaseSettings connection)
    {
        var config = new AmazonDynamoDBConfig
        {
            Timeout = TimeSpan.FromSeconds(connection.TimeoutSeconds),
        };

        var cloud = connection.Cloud;

        if (!string.IsNullOrWhiteSpace(cloud.ServiceUrl))
        {
            config.ServiceURL = cloud.ServiceUrl;
            config.AuthenticationRegion = cloud.Region ?? "us-east-1";
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(cloud.Region);
        }

        var hasStaticCredentials = !string.IsNullOrWhiteSpace(cloud.AccessKey)
            && !string.IsNullOrWhiteSpace(cloud.SecretKey);

        return hasStaticCredentials
            ? new AmazonDynamoDBClient(
                new BasicAWSCredentials(cloud.AccessKey, cloud.SecretKey),
                config)
            : new AmazonDynamoDBClient(config);
    }
}
