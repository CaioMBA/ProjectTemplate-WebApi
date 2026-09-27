using System.Collections.Concurrent;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Persistence;

namespace Data.Sql.DatabaseAccess;

public sealed class SqlCrudStatementFactory(ISqlDialect dialect)
{
    private static readonly ConcurrentDictionary<(DatabaseType Provider, Type Entity), SqlCrudStatements> _cache =
        new();

    public SqlCrudStatements For(SqlEntityMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return _cache.GetOrAdd((dialect.ProviderType, map.EntityType), _ => Build(map));
    }

    private SqlCrudStatements Build(SqlEntityMap map)
    {
        var table = QualifiedTable(map);

        var allColumns = string.Join(", ", map.Columns.Select(column => dialect.QuoteIdentifier(column.ColumnName)));

        var key = map.KeyColumns.Count == 1 ? map.SingleKeyColumn : null;

        var keyPredicate = key is null
            ? string.Empty
            : $"{dialect.QuoteIdentifier(key.ColumnName)} = {dialect.Parameter(key.PropertyPath)}";

        var identityPredicate = key is null
            ? string.Empty
            : $"{dialect.QuoteIdentifier(key.ColumnName)} = {dialect.Parameter("Id")}";

        var insertColumns = string.Join(
            ", ",
            map.InsertableColumns.Select(column => dialect.QuoteIdentifier(column.ColumnName)));

        var insertValues = string.Join(
            ", ",
            map.InsertableColumns.Select(column => dialect.Parameter(column.PropertyPath)));

        var assignments = string.Join(
            ", ",
            map.UpdatableColumns.Select(column =>
                $"{dialect.QuoteIdentifier(column.ColumnName)} = {dialect.Parameter(column.PropertyPath)}"));

        return new SqlCrudStatements(
            Select: $"SELECT {allColumns} FROM {table} WHERE {identityPredicate}",
            SelectAll: $"SELECT {allColumns} FROM {table}",
            Insert: $"INSERT INTO {table} ({insertColumns}) VALUES ({insertValues})",
            Update: $"UPDATE {table} SET {assignments} WHERE {keyPredicate}",
            Delete: $"DELETE FROM {table} WHERE {keyPredicate}",
            DeleteById: $"DELETE FROM {table} WHERE {identityPredicate}");
    }

    private string QualifiedTable(SqlEntityMap map) =>
        string.IsNullOrWhiteSpace(map.Schema)
            ? dialect.QuoteIdentifier(map.TableName)
            : $"{dialect.QuoteIdentifier(map.Schema)}.{dialect.QuoteIdentifier(map.TableName)}";
}
