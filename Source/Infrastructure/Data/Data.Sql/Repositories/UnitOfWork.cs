using Data.Sql.EntityFrameworkContexts;
using Domain.Interfaces.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Data.Sql.Repositories;

public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork, IAsyncDisposable
{
    private IDbContextTransaction? _transaction;

    public bool HasActiveTransaction => _transaction is not null;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "A transaction is already active on this unit of work. Check HasActiveTransaction " +
                "before beginning one, or let TransactionBehavior manage the boundary.");
        }

        _transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
        }
    }

    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        Func<TResult, bool> shouldCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(shouldCommit);

        var strategy = context.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(
            async token =>
            {
                await BeginTransactionAsync(token).ConfigureAwait(false);

                try
                {
                    var result = await operation(token).ConfigureAwait(false);

                    if (shouldCommit(result))
                    {
                        await CommitTransactionAsync(token).ConfigureAwait(false);
                    }
                    else
                    {
                        await RollbackTransactionAsync(token).ConfigureAwait(false);
                    }

                    return result;
                }
                catch
                {
                    await RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);

                    throw;
                }
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeTransactionAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeTransactionAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }
}
