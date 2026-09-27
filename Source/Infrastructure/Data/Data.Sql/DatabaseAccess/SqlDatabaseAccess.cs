using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Dapper;
using Domain.Interfaces.Persistence;
using Domain.Models.Persistence;
using Microsoft.Extensions.Logging;

namespace Data.Sql.DatabaseAccess;

public sealed class SqlDatabaseAccess(
    ISqlDatabaseProvider provider,
    SqlConnectionString connectionString,
    SqlEntityMapFactory maps,
    SqlCrudStatementFactory statements,
    SqlDebugFormatter formatter,
    CachedPageStreamer pageStreamer,
    ILogger<SqlDatabaseAccess> logger) : ISqlDatabaseAccess
{
    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(sql, parameters);

            var rows = await execution.Connection
                .QueryAsync<T>(Command(sql, parameters, transaction, null, cancellationToken))
                .ConfigureAwait(false);

            return rows.AsList();
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(sql, parameters);

            return await execution.Connection
                .QueryFirstOrDefaultAsync<T>(Command(sql, parameters, transaction, null, cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(sql, parameters);

            return await execution.Connection
                .QuerySingleOrDefaultAsync<T>(Command(sql, parameters, transaction, null, cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(sql, parameters);

            return await execution.Connection
                .ExecuteScalarAsync<T>(Command(sql, parameters, transaction, null, cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(sql, parameters);

            return await execution.Connection
                .ExecuteAsync(Command(sql, parameters, transaction, null, cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<T> StreamAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(sql, parameters);

            await using var reader = await execution.Connection
                .ExecuteReaderAsync(Command(sql, parameters, transaction, null, cancellationToken))
                .ConfigureAwait(false);

            var parser = SqlRowReader.For<T>(reader);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return parser(reader);
            }
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<T>> ProcedureAsync<T>(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default) =>
        QueryStoredProcedureAsync<T>(procedureName, parameters, transaction, cancellationToken);

    public async Task<T?> ProcedureFirstOrDefaultAsync<T>(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(procedureName, parameters);

            return await execution.Connection
                .QueryFirstOrDefaultAsync<T>(Command(
                    procedureName,
                    parameters,
                    transaction,
                    CommandType.StoredProcedure,
                    cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<int> ProcedureExecuteAsync(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(procedureName, parameters);

            return await execution.Connection
                .ExecuteAsync(Command(
                    procedureName,
                    parameters,
                    transaction,
                    CommandType.StoredProcedure,
                    cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<T> ProcedureStreamAsync<T>(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(procedureName, parameters);

            await using var reader = await execution.Connection
                .ExecuteReaderAsync(Command(
                    procedureName,
                    parameters,
                    transaction,
                    CommandType.StoredProcedure,
                    cancellationToken))
                .ConfigureAwait(false);

            var parser = SqlRowReader.For<T>(reader);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return parser(reader);
            }
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Task<ISqlResultSets> QueryMultipleAsync(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default) =>
        OpenResultSetsAsync(sql, parameters, transaction, commandType: null, cancellationToken);

    public Task<ISqlResultSets> ProcedureMultipleAsync(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default) =>
        OpenResultSetsAsync(
            procedureName,
            parameters,
            transaction,
            CommandType.StoredProcedure,
            cancellationToken);

    public Task<IReadOnlyList<T>> QueryPagedCachedAsync<T>(
        SqlPagedQuery query,
        CancellationToken cancellationToken = default) =>
        pageStreamer.GetPageAsync<T>(query, cancellationToken);

    public Task WarmPagedCacheAsync<T>(
        SqlPagedQuery query,
        CancellationToken cancellationToken = default) =>
        pageStreamer.WarmAsync<T>(query, cancellationToken);

    public Task<T?> GetAsync<T>(
        object id,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(id);

        var crud = statements.For(maps.For<T>());

        return QueryFirstOrDefaultAsync<T>(crud.Select, new { Id = id }, transaction, cancellationToken);
    }

    public Task<IReadOnlyList<T>> GetAllAsync<T>(
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class =>
        QueryAsync<T>(statements.For(maps.For<T>()).SelectAll, null, transaction, cancellationToken);

    public Task<int> InsertAsync<T>(
        T entity,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        var map = maps.For<T>();

        return ExecuteAsync(
            statements.For(map).Insert,
            ToParameters(map, entity, map.InsertableColumns),
            transaction,
            cancellationToken);
    }

    public async Task<bool> UpdateAsync<T>(
        T entity,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        var map = maps.For<T>();

        var affected = await ExecuteAsync(
            statements.For(map).Update,
            ToParameters(map, entity, [.. map.UpdatableColumns, .. map.KeyColumns]),
            transaction,
            cancellationToken).ConfigureAwait(false);

        return affected > 0;
    }

    public async Task<bool> DeleteAsync<T>(
        T entity,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(entity);

        var map = maps.For<T>();

        var affected = await ExecuteAsync(
            statements.For(map).Delete,
            ToParameters(map, entity, map.KeyColumns),
            transaction,
            cancellationToken).ConfigureAwait(false);

        return affected > 0;
    }

    public async Task<bool> DeleteByIdAsync<T>(
        object id,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(id);

        var affected = await ExecuteAsync(
            statements.For(maps.For<T>()).DeleteById,
            new { Id = id },
            transaction,
            cancellationToken).ConfigureAwait(false);

        return affected > 0;
    }

    public async Task<ISqlTransactionScope> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var transaction = await connection
                .BeginTransactionAsync(isolationLevel, cancellationToken)
                .ConfigureAwait(false);

            return new SqlTransactionScope(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private async Task<IReadOnlyList<T>> QueryStoredProcedureAsync<T>(
        string procedureName,
        object? parameters,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(procedureName, parameters);

            var rows = await execution.Connection
                .QueryAsync<T>(Command(
                    procedureName,
                    parameters,
                    transaction,
                    CommandType.StoredProcedure,
                    cancellationToken))
                .ConfigureAwait(false);

            return rows.AsList();
        }
        finally
        {
            await execution.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<ISqlResultSets> OpenResultSetsAsync(
        string sql,
        object? parameters,
        IDbTransaction? transaction,
        CommandType? commandType,
        CancellationToken cancellationToken)
    {
        var execution = await ResolveAsync(transaction, cancellationToken).ConfigureAwait(false);

        try
        {
            Trace(sql, parameters);

            var reader = await execution.Connection
                .QueryMultipleAsync(Command(sql, parameters, transaction, commandType, cancellationToken))
                .ConfigureAwait(false);

            return new SqlResultSets(reader, execution.Owned ? execution.Connection : null);
        }
        catch
        {
            await execution.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private static DynamicParameters ToParameters<T>(
        SqlEntityMap map,
        T entity,
        IReadOnlyList<SqlColumnMap> columns)
        where T : class
    {
        var parameters = new DynamicParameters();

        foreach (var column in columns)
        {
            parameters.Add(column.PropertyPath, column.GetValue(entity));
        }

        if (map.KeyColumns.Count == 1)
        {
            parameters.Add("Id", map.SingleKeyColumn.GetValue(entity));
        }

        return parameters;
    }

    private static CommandDefinition Command(
        string sql,
        object? parameters,
        IDbTransaction? transaction,
        CommandType? commandType,
        CancellationToken cancellationToken) =>
        new(
            sql,
            parameters,
            transaction,
            commandType: commandType,
            cancellationToken: cancellationToken);

    private void Trace(string sql, object? parameters)
    {
        if (!formatter.InterpolationEnabled || !logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        logger.LogDebug("Executing SQL: {Sql}", formatter.Describe(sql, parameters));
    }

    private async Task<Execution> ResolveAsync(
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction?.Connection is DbConnection enlisted)
        {
            if (enlisted.State != ConnectionState.Open)
            {
                await enlisted.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            return new Execution(enlisted, Owned: false);
        }

        return new Execution(await OpenAsync(cancellationToken).ConfigureAwait(false), Owned: true);
    }

    private async Task<DbConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = provider.CreateConnection(connectionString.Value);

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    private sealed record Execution(DbConnection Connection, bool Owned)
    {
        public async ValueTask DisposeAsync()
        {
            if (Owned)
            {
                await Connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
