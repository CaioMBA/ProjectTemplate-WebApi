using Amazon.DynamoDBv2;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using Raven.Client.Documents;

namespace Data.NoSql.HealthChecks;

public sealed class MongoDbHealthCheck(IMongoDatabase database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await database
                .RunCommandAsync<BsonDocument>(
                    new BsonDocument("ping", 1),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy();
        }
        catch (MongoException exception)
        {
            return HealthCheckResult.Unhealthy("MongoDB did not respond to ping.", exception);
        }
        catch (TimeoutException exception)
        {
            return HealthCheckResult.Unhealthy("MongoDB ping timed out.", exception);
        }
    }
}

public sealed class CosmosDbHealthCheck(Database database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await database.ReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

            return HealthCheckResult.Healthy();
        }
        catch (CosmosException exception)
        {
            return HealthCheckResult.Unhealthy("Cosmos DB database read failed.", exception);
        }
        catch (HttpRequestException exception)
        {
            return HealthCheckResult.Unhealthy("Cosmos DB endpoint is unreachable.", exception);
        }
    }
}

public sealed class DynamoDbHealthCheck(IAmazonDynamoDB client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await client
                .ListTablesAsync(limit: 1, cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy();
        }
        catch (AmazonDynamoDBException exception)
        {
            return HealthCheckResult.Unhealthy("DynamoDB ListTables failed.", exception);
        }
        catch (HttpRequestException exception)
        {
            return HealthCheckResult.Unhealthy("DynamoDB endpoint is unreachable.", exception);
        }
    }
}

public sealed class RavenDbHealthCheck(IDocumentStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var session = store.OpenAsyncSession();

            await session
                .Advanced
                .ExistsAsync("health-probe", cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy();
        }
        catch (HttpRequestException exception)
        {
            return HealthCheckResult.Unhealthy("RavenDB endpoint is unreachable.", exception);
        }
        catch (InvalidOperationException exception)
        {
            return HealthCheckResult.Unhealthy("RavenDB session could not be opened.", exception);
        }
    }
}
