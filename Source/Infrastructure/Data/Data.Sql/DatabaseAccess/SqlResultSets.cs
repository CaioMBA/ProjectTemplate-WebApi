using System.Data.Common;
using Dapper;
using Domain.Interfaces.Persistence;

namespace Data.Sql.DatabaseAccess;

public sealed class SqlResultSets(
    SqlMapper.GridReader reader,
    DbConnection? ownedConnection) : ISqlResultSets
{
    public bool IsConsumed => reader.IsConsumed;

    public async Task<IReadOnlyList<T>> ReadAsync<T>(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsureNotConsumed();

        var rows = await reader.ReadAsync<T>().ConfigureAwait(false);

        return rows.AsList();
    }

    public async Task<T?> ReadFirstOrDefaultAsync<T>(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EnsureNotConsumed();

        return await reader.ReadFirstOrDefaultAsync<T>().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await reader.DisposeAsync().ConfigureAwait(false);

        if (ownedConnection is not null)
        {
            await ownedConnection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void EnsureNotConsumed()
    {
        if (reader.IsConsumed)
        {
            throw new InvalidOperationException(
                "Every result set has already been read. Each ReadAsync call advances to the next "
                + "set, so read them in the order the query produces them.");
        }
    }
}
