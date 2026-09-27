using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Scheduling;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Hangfire;
using Hangfire.Console;
using Hangfire.Console.Extensions;
using Hangfire.InMemory;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using Hangfire.PostgreSql;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using OpenTelemetry.Trace;
using Scheduling.Jobs;

namespace Scheduling.Setup;

public static class SchedulingSetup
{
    public const string HealthCheckName = "scheduler";

    public static IServiceCollection AddSchedulingSetup(
        this IServiceCollection services,
        AppSettings settings,
        ObservabilityOptions observability)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(observability);

        var scheduling = settings.Scheduling;

        services.AddHangfire((provider, configuration) =>
        {
            configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseConsole()
                .UseFilter(new AutomaticRetryAttribute { Attempts = scheduling.RetryAttempts });

            UseStorage(configuration, provider, settings);
        });

        services.AddHangfireServer(server =>
        {
            server.WorkerCount = scheduling.Workers;
            server.Queues = [.. scheduling.Queues];
            server.ServerName = $"{settings.AppName}:{Environment.MachineName}";
        });

        services.AddHangfireConsoleExtensions();

        services.AddSingleton<RecurringJobCatalog>();
        services.AddScoped<IJobProgress, ConsoleJobProgress>();
        services.AddTransient<RecurringJobRunner>();
        services.AddTransient<BackgroundJobRunner>();
        services.AddSingleton<IJobScheduler, HangfireJobScheduler>();
        services.AddSingleton<RecurringJobRegistrar>();
        services.AddHostedService(provider => provider.GetRequiredService<RecurringJobRegistrar>());

        services.AddHealthChecks().Add(HealthCheckPolicy.Registration(
            DependencyKind.Scheduler,
            HealthCheckName,
            provider => new SchedulerHealthCheck(
                provider.GetRequiredService<JobStorage>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System)));

        if (observability.Enabled && observability.EnableTracing)
        {
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddHangfireInstrumentation());
        }

        return services;
    }

    private static void UseStorage(IGlobalConfiguration configuration, IServiceProvider provider, AppSettings settings)
    {
        var storage = settings.Scheduling.Storage;

        if (storage.Type == SchedulingStorageType.Memory)
        {
            configuration.UseInMemoryStorage(new InMemoryStorageOptions());

            return;
        }

        var databaseId = storage.DatabaseId!;
        var database = settings.GetDatabase(databaseId);

        if (database.Type == DatabaseType.MongoDb)
        {
            configuration.UseMongoStorage(
                provider.GetRequiredKeyedService<IMongoClient>(databaseId),
                database.Database,
                new MongoStorageOptions
                {
                    Prefix = storage.Schema,
                    CheckConnection = true,
                    CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.TailNotificationsCollection,
                    MigrationOptions = new MongoMigrationOptions
                    {
                        MigrationStrategy = new MigrateMongoMigrationStrategy(),
                        BackupStrategy = new CollectionMongoBackupStrategy(),
                    },
                });

            return;
        }

        var connectionString = provider.GetRequiredKeyedService<SqlConnectionString>(databaseId).Value;

        switch (database.Type)
        {
            case DatabaseType.Postgresql:
                configuration.UsePostgreSqlStorage(
                    bootstrap => bootstrap.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions { SchemaName = storage.Schema, PrepareSchemaIfNecessary = true });
                break;

            case DatabaseType.SqlServer:
                configuration.UseSqlServerStorage(
                    () => new SqlConnection(connectionString),
                    new SqlServerStorageOptions { SchemaName = storage.Schema, PrepareSchemaIfNecessary = true });
                break;

            default:
                throw new InvalidOperationException(
                    $"{AppSettings.SectionName}:Scheduling:Storage:DatabaseId '{databaseId}' is a {database.Type} database; "
                    + $"job storage supports {string.Join(", ", AppSettings.SupportedSchedulingEngines.Keys)}.");
        }
    }
}