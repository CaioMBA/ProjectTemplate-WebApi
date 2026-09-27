using Data.Sql.EntityFrameworkContexts;
using Domain.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Data.Sql.Outbox;

public sealed class OutboxMaintenance(AppDbContext context) : IOutboxMaintenance
{
    public Task<int> PurgeProcessedAsync(DateTime processedBeforeUtc, CancellationToken cancellationToken = default) =>
        context.OutboxMessages
            .Where(message => message.ProcessedOnUtc != null && message.ProcessedOnUtc < processedBeforeUtc)
            .ExecuteDeleteAsync(cancellationToken);
}