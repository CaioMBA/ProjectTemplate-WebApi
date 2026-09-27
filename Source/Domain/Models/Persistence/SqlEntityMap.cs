using Domain.Enums;

namespace Domain.Models.Persistence;

public sealed record SqlColumnMap(
    string ColumnName,
    string PropertyPath,
    Func<object, object?> GetValue,
    bool IsKey,
    bool IsDatabaseGenerated);

public sealed record SqlEntityMap(
    Type EntityType,
    string TableName,
    string? Schema,
    IReadOnlyList<SqlColumnMap> Columns,
    SqlMapSource Source)
{
    public IReadOnlyList<SqlColumnMap> KeyColumns { get; } =
        [.. Columns.Where(column => column.IsKey)];

    public IReadOnlyList<SqlColumnMap> InsertableColumns { get; } =
        [.. Columns.Where(column => !column.IsDatabaseGenerated)];

    public IReadOnlyList<SqlColumnMap> UpdatableColumns { get; } =
        [.. Columns.Where(column => !column.IsKey && !column.IsDatabaseGenerated)];

    public SqlColumnMap SingleKeyColumn =>
        KeyColumns.Count == 1
            ? KeyColumns[0]
            : throw new InvalidOperationException(
                $"{EntityType.Name} has {KeyColumns.Count} key columns. "
                + "Identity-based CRUD requires exactly one. Use ExecuteAsync with explicit SQL "
                + "for composite or keyless tables.");
}
