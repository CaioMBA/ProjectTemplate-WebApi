using System.Data;

namespace Domain.Interfaces.Persistence;

public interface ISqlTransactionScope : IAsyncDisposable
{
    IDbTransaction Transaction { get; }

    bool SupportsSavepoints { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);

    Task CreateSavepointAsync(string savepointName, CancellationToken cancellationToken = default);

    Task RollbackToSavepointAsync(string savepointName, CancellationToken cancellationToken = default);

    Task ReleaseSavepointAsync(string savepointName, CancellationToken cancellationToken = default);
}
