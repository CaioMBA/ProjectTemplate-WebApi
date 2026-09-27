namespace Domain.Interfaces.Persistence;

public interface IOutboxMaintenance
{
    Task<int> PurgeProcessedAsync(DateTime processedBeforeUtc, CancellationToken cancellationToken = default);
}