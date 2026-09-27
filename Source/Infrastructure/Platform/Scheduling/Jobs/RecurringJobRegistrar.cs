using Domain.Enums;
using Domain.Models.Configuration;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Scheduling.Jobs;

public sealed partial class RecurringJobRegistrar(
    RecurringJobCatalog catalog,
    IRecurringJobManager manager,
    JobStorage storage,
    IOptionsMonitor<AppSettings> settings,
    IHostEnvironment environment,
    ILogger<RecurringJobRegistrar> logger) : IHostedService, IDisposable
{
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var scheduling = settings.CurrentValue.Scheduling;

        if (scheduling.Storage.Type == SchedulingStorageType.Memory && environment.IsProduction())
        {
            LogMemoryStorageInProduction(logger);
        }

        Apply(scheduling);

        _subscription = settings.OnChange((current, _) => Apply(current.Scheduling));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _subscription?.Dispose();

    public void Apply(SchedulingSettings scheduling)
    {
        ArgumentNullException.ThrowIfNull(scheduling);

        foreach (var unknown in scheduling.Jobs.Where(job => !catalog.Contains(job.Id)))
        {
            LogUnknownJob(logger, unknown.Id, string.Join(", ", catalog.Ids));
        }

        foreach (var jobId in catalog.Ids)
        {
            var configured = scheduling.Jobs.Find(job => string.Equals(job.Id, jobId, StringComparison.OrdinalIgnoreCase));

            if (configured is { Enabled: false })
            {
                manager.RemoveIfExists(jobId);
                LogDisabled(logger, jobId);
                continue;
            }

            var cron = string.IsNullOrWhiteSpace(configured?.Cron) ? catalog.DefaultCron(jobId) : configured.Cron;
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(configured?.TimeZone ?? "UTC");
            var id = jobId;

            manager.AddOrUpdate<RecurringJobRunner>(
                id,
                runner => runner.RunAsync(id, CancellationToken.None),
                cron,
                new RecurringJobOptions { TimeZone = timeZone });

            LogScheduled(logger, jobId, cron, timeZone.Id);
        }

        using var connection = storage.GetConnection();

        foreach (var stale in connection.GetRecurringJobs().Select(job => job.Id).Where(id => !catalog.Contains(id)))
        {
            manager.RemoveIfExists(stale);
            LogRemovedStale(logger, stale);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scheduling uses in-memory storage in Production: jobs are lost on restart and every replica runs every recurring job. Use Storage:Type Database for more than one replica.")]
    private static partial void LogMemoryStorageInProduction(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settings:Scheduling:Jobs names '{JobId}', which no IRecurringJob declares. Known ids: {Known}.")]
    private static partial void LogUnknownJob(ILogger logger, string jobId, string known);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recurring job {JobId} is disabled by configuration.")]
    private static partial void LogDisabled(ILogger logger, string jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recurring job {JobId} scheduled at '{Cron}' ({TimeZone}).")]
    private static partial void LogScheduled(ILogger logger, string jobId, string cron, string timeZone);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed recurring job {JobId}: no IRecurringJob declares it any more.")]
    private static partial void LogRemovedStale(ILogger logger, string jobId);
}