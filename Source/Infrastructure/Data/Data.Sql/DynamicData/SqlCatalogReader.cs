using System.Data.Common;
using System.Globalization;
using System.Text;
using Domain.Enums;
using Domain.Models.DynamicData;

namespace Data.Sql.DynamicData;

public abstract class SqlCatalogReader
{
    public abstract string ColumnsSql { get; }

    public abstract string PrimaryKeysSql { get; }

    public abstract string ForeignKeysSql { get; }

    public abstract FieldModel MapColumn(string name, bool isNullable, CatalogColumnType type);

    public async Task<IReadOnlyList<EntitySetModel>> ReadAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var columns = await ReadRowsAsync(connection, ColumnsSql, cancellationToken).ConfigureAwait(false);
        var keys = await ReadRowsAsync(connection, PrimaryKeysSql, cancellationToken).ConfigureAwait(false);
        var foreignKeys = await ReadRowsAsync(connection, ForeignKeysSql, cancellationToken).ConfigureAwait(false);

        return Assemble(columns, keys, foreignKeys);
    }

    public IReadOnlyList<EntitySetModel> Assemble(
        IReadOnlyList<object?[]> columns,
        IReadOnlyList<object?[]> keys,
        IReadOnlyList<object?[]> foreignKeys)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(foreignKeys);

        var tables = columns
            .Select(row => (
                Table: new TableKey(Text(row[0]), Text(row[1])!),
                Ordinal: Number(row[3]) ?? 0,
                Field: MapColumn(
                    Text(row[2])!,
                    Number(row[4]) == 1,
                    new CatalogColumnType(
                        Text(row[5]) ?? string.Empty,
                        Number(row[6]),
                        Number(row[7]),
                        Number(row[8]),
                        Number(row[9]),
                        Text(row[10])))))
            .GroupBy(column => column.Table)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(column => column.Ordinal).Select(column => column.Field).ToList());

        var primaryKeys = keys
            .GroupBy(row => new TableKey(Text(row[0]), Text(row[1])!))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderBy(row => Number(row[3]) ?? 0)
                    .Select(row => Text(row[2])!)
                    .ToList());

        var references = foreignKeys
            .GroupBy(row => (Name: Text(row[0])!, Table: new TableKey(Text(row[1]), Text(row[2])!)))
            .Select(group =>
            {
                var ordered = group.OrderBy(row => Number(row[4]) ?? 0).ToList();
                var target = new TableKey(Text(ordered[0][5]), Text(ordered[0][6])!);
                var targetColumns = ordered.Select(row => Text(row[7])).ToList();

                if (targetColumns.Any(column => column is null)
                    && primaryKeys.TryGetValue(target, out var targetKey)
                    && targetKey.Count == ordered.Count)
                {
                    targetColumns = [.. targetKey];
                }

                return (
                    group.Key.Table,
                    Key: new ForeignKeyModel(
                        group.Key.Name,
                        ordered.Select(row => Text(row[3])!).ToList(),
                        target.Schema,
                        target.Name,
                        targetColumns.Select(column => column ?? string.Empty).ToList()));
            })
            .Where(reference => reference.Key.TargetColumns.All(column => column.Length > 0))
            .GroupBy(reference => reference.Table)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ForeignKeyModel>)group.Select(item => item.Key).ToList());

        return tables
            .OrderBy(table => table.Key.Schema, StringComparer.Ordinal)
            .ThenBy(table => table.Key.Name, StringComparer.Ordinal)
            .Select(table => new EntitySetModel(
                table.Key.Schema,
                table.Key.Name,
                table.Value,
                primaryKeys.GetValueOrDefault(table.Key) ?? [],
                references.GetValueOrDefault(table.Key) ?? []))
            .ToList();
    }

    protected static FieldModel Field(string name, FieldKind kind, bool isNullable, string nativeType, bool isLongText = false) =>
        new(name, kind, isNullable, nativeType, isLongText);

    private static async Task<List<object?[]>> ReadRowsAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

#pragma warning disable CA2100
        command.CommandText = sql;
#pragma warning restore CA2100

        var rows = new List<object?[]>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new object?[reader.FieldCount];

            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[index] = await reader.IsDBNullAsync(index, cancellationToken).ConfigureAwait(false)
                    ? null
                    : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string? Text(object? value) => value switch
    {
        null or DBNull => null,
        string text => text.Trim(),
        byte[] bytes => Encoding.UTF8.GetString(bytes).Trim(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim(),
    };

    private static int? Number(object? value) => value switch
    {
        null or DBNull => null,
        bool flag => flag ? 1 : 0,
        string text => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
        _ => Convert.ToInt32(value, CultureInfo.InvariantCulture),
    };

    public sealed class CatalogColumnType(
        string dataType,
        int? precision = null,
        int? scale = null,
        int? length = null,
        int? subType = null,
        string? extra = null)
    {
        public string DataType { get; } = dataType;

        public int? Precision { get; } = precision;

        public int? Scale { get; } = scale;

        public int? Length { get; } = length;

        public int? SubType { get; } = subType;

        public string? Extra { get; } = extra;

        public string Lowered => DataType.ToLowerInvariant();
    }

    private readonly record struct TableKey(string? Schema, string Name);
}
