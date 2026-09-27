using System.Data;

namespace Domain.Interfaces.Persistence;

public interface ISqlDatabaseAccess
{
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<T?> QueryFirstOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<T?> ExecuteScalarAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<T> StreamAsync<T>(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> ProcedureAsync<T>(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<T?> ProcedureFirstOrDefaultAsync<T>(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<int> ProcedureExecuteAsync(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<T> ProcedureStreamAsync<T>(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<ISqlResultSets> QueryMultipleAsync(
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<ISqlResultSets> ProcedureMultipleAsync(
        string procedureName,
        object? parameters = null,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryPagedCachedAsync<T>(
        SqlPagedQuery query,
        CancellationToken cancellationToken = default);

    Task WarmPagedCacheAsync<T>(
        SqlPagedQuery query,
        CancellationToken cancellationToken = default);

    Task<T?> GetAsync<T>(
        object id,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<IReadOnlyList<T>> GetAllAsync<T>(
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<int> InsertAsync<T>(
        T entity,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<bool> UpdateAsync<T>(
        T entity,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<bool> DeleteAsync<T>(
        T entity,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<bool> DeleteByIdAsync<T>(
        object id,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
        where T : class;

    Task<ISqlTransactionScope> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}
