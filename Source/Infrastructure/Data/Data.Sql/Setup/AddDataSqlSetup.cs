using Data.Sql.DynamicData;
using Data.Sql.EntityFrameworkContexts;
using Data.Sql.EntityFrameworkContexts.Interceptors;
using Data.Sql.HealthChecks;
using Data.Sql.Outbox;
using Domain.Abstractions;
using Domain.Connections;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.Sql.Setup;

public static class DataSqlSetup
{
    public static IServiceCollection AddDataSqlSetup(
        this IServiceCollection services,
        AppSettings settings,
        bool recordStatements = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        services.AddDapperSetup();

        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();

        foreach (var connection in RelationalDatabases(settings))
        {
            services.AddSqlDatabase(connection, recordStatements);
        }

        return services;
    }

    public static IServiceCollection AddSqlDatabase(
        this IServiceCollection services,
        DatabaseSettings connection,
        bool recordStatements = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);

        var databaseId = connection.Id;

        services.AddKeyedSingleton(databaseId, (_, _) =>
            new LazyConnection<SqlDatabaseHandle>(() => SqlDatabaseHandle.Create(connection, recordStatements)));

        services.AddKeyedSingleton(databaseId, (provider, key) =>
            provider.GetRequiredKeyedService<LazyConnection<SqlDatabaseHandle>>(key).Value);

        services.AddKeyedScoped<AppDbContext>(databaseId, (provider, key) =>
        {
            var handle = provider.GetRequiredKeyedService<SqlDatabaseHandle>(key);

            return handle.Provider.CreateContext(provider, handle.Registration);
        });

        services
            .AddDatabaseAccessSetup(connection)
            .AddRepositoriesSetup(databaseId);

        services.AddKeyedScoped<IDynamicDataSource>(databaseId, (provider, key) => new SqlDynamicDataSource(
            databaseId,
            provider.GetRequiredKeyedService<SqlDatabaseHandle>(key),
            provider.GetRequiredKeyedService<ISqlDatabaseAccess>(key),
            provider.GetService<StartupSettings>()?.Api.GraphQlServer.AutoSchema.MaxInValues
                ?? new GraphQlAutoSchemaOptions().MaxInValues));

        services.AddHealthChecks().Add(HealthCheckPolicy.Registration(
            DependencyKind.Database,
            $"db:{databaseId}",
            provider => new SqlDatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>(), databaseId)));

        if (connection.Sql.Outbox.Enabled)
        {
            services.AddSingleton<IHostedService>(provider => new OutboxPublisher(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IOptionsMonitor<AppSettings>>(),
                databaseId,
                provider.GetRequiredService<ILogger<OutboxPublisher>>()));
        }

        return services;
    }

    public static async Task UseDataSqlSetupAsync(
        this IServiceProvider serviceProvider,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(settings);

        foreach (var connection in RelationalDatabases(settings).Where(database => database.Sql.MigrateOnStartup))
        {
            await MigrateAsync(serviceProvider, connection.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<DatabaseSettings> RelationalDatabases(AppSettings settings) =>
        settings.Databases.Where(database => database.Type.Family() == DatabaseFamily.Relational);

    private static async Task MigrateAsync(
        IServiceProvider serviceProvider,
        string databaseId,
        CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();

        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DataSqlSetup).FullName!);

        var context = scope.ServiceProvider.GetRequiredKeyedService<AppDbContext>(databaseId);

        var pending = (await context.Database
            .GetPendingMigrationsAsync(cancellationToken)
            .ConfigureAwait(false)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Database {DatabaseId} is up to date; no migrations to apply.", databaseId);

            return;
        }

        logger.LogWarning(
            "Applying {Count} pending migration(s) to database {DatabaseId} at startup: {Migrations}. "
            + "This is unsafe with more than one replica - prefer `dotnet ef database update` in CD.",
            pending.Count,
            databaseId,
            string.Join(", ", pending));

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Migrations applied to database {DatabaseId}.", databaseId);
    }
}