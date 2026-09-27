using System.Data;
using System.Data.Common;
using Domain.Interfaces.Persistence;

namespace Data.Sql.DatabaseAccess;

public sealed class SqlTransactionScope(DbConnection connection, DbTransaction transaction)
    : ISqlTransactionScope
{
    private bool _completed;

    public IDbTransaction Transaction => transaction;

    public bool SupportsSavepoints => transaction.SupportsSavepoints;

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        _completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

        _completed = true;
    }

    public async Task CreateSavepointAsync(
        string savepointName,
        CancellationToken cancellationToken = default)
    {
        EnsureSavepointsAreSupported(savepointName);

        await transaction.SaveAsync(savepointName, cancellationToken).ConfigureAwait(false);
    }

    public async Task RollbackToSavepointAsync(
        string savepointName,
        CancellationToken cancellationToken = default)
    {
        EnsureSavepointsAreSupported(savepointName);

        await transaction.RollbackAsync(savepointName, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReleaseSavepointAsync(
        string savepointName,
        CancellationToken cancellationToken = default)
    {
        EnsureSavepointsAreSupported(savepointName);

        await transaction.ReleaseAsync(savepointName, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completed)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                _completed = true;
            }
        }

        await transaction.DisposeAsync().ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void EnsureSavepointsAreSupported(string savepointName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savepointName);

        if (!transaction.SupportsSavepoints)
        {
            throw new NotSupportedException(
                $"The configured database provider does not support savepoints, so '{savepointName}' "
                + "cannot be created. Check ISqlTransactionScope.SupportsSavepoints first.");
        }
    }
}
