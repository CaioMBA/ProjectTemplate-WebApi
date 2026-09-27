using Domain.Interfaces.Persistence;
using Domain.Interfaces.Scheduling;
using Domain.Interfaces.Services;
using Domain.Models.Configuration;
using Domain.Models.Requests.Products;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Jobs;

public sealed partial class OutboxCleanupJob(
    [FromKeyedServices(ProductsStore.DatabaseId)] IOutboxMaintenance outbox,
    IOptionsMonitor<AppSettings> settings,
    IDateTimeProvider clock,
    ILogger<OutboxCleanupJob> logger) : IRecurringJob
{
    public const string JobId = "outbox-cleanup";

    public string Id => JobId;

    public string Cron => "0 3 * * *";

    public async Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var retentionDays = settings.CurrentValue.GetDatabase(ProductsStore.DatabaseId).Sql.Outbox.RetentionDays;
        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        LogStarting(logger, ProductsStore.DatabaseId, cutoff);

        progress.Report(10);

        var deleted = await outbox.PurgeProcessedAsync(cutoff, cancellationToken).ConfigureAwait(false);

        progress.Report(100);

        LogFinished(logger, deleted, ProductsStore.DatabaseId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purging outbox messages on {DatabaseId} processed before {Cutoff:O}.")]
    private static partial void LogStarting(ILogger logger, string databaseId, DateTime cutoff);

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Deleted} processed outbox messages from {DatabaseId}.")]
    private static partial void LogFinished(ILogger logger, int deleted, string databaseId);
}