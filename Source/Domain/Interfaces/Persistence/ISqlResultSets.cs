namespace Domain.Interfaces.Persistence;

public interface ISqlResultSets : IAsyncDisposable
{
    bool IsConsumed { get; }

    Task<IReadOnlyList<T>> ReadAsync<T>(CancellationToken cancellationToken = default);

    Task<T?> ReadFirstOrDefaultAsync<T>(CancellationToken cancellationToken = default);
}
