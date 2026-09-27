using Data.Sql.EntityFrameworkContexts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Data.Sql.HealthChecks;

public sealed class SqlDatabaseHealthCheck(IServiceScopeFactory scopeFactory, string databaseId) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await using var scope = scopeFactory.CreateAsyncScope();

        var database = scope.ServiceProvider.GetRequiredKeyedService<AppDbContext>(databaseId).Database;

        return await database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
            ? HealthCheckResult.Healthy($"Database '{databaseId}' accepted a connection.")
            : new HealthCheckResult(context.Registration.FailureStatus, $"Database '{databaseId}' refused a connection.");
    }
}