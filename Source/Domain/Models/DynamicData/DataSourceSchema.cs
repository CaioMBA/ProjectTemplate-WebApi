using Domain.Enums;

namespace Domain.Models.DynamicData;

public sealed record DataSourceSchema(
    string DatabaseId,
    DatabaseType Type,
    IReadOnlyList<EntitySetModel> EntitySets,
    DataSourceCapabilities Capabilities);

public sealed record DataSourceCapabilities(
    bool Relations,
    bool MinMax,
    bool SumAverage)
{
    public static DataSourceCapabilities Relational { get; } = new(Relations: true, MinMax: true, SumAverage: true);
}

public sealed record EntitySetModel(
    string? Schema,
    string Name,
    IReadOnlyList<FieldModel> Fields,
    IReadOnlyList<string> KeyFields,
    IReadOnlyList<ForeignKeyModel> ForeignKeys)
{
    public string QualifiedName => Schema is null ? Name : $"{Schema}.{Name}";

    public FieldModel? FindField(string name) =>
        Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal));

    public FieldModel? FindPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var segments = path.Split('.');
        var fields = Fields;
        FieldModel? found = null;

        foreach (var segment in segments)
        {
            found = fields.FirstOrDefault(field => string.Equals(field.Name, segment, StringComparison.Ordinal));

            if (found is null)
            {
                return null;
            }

            fields = found.Children;
        }

        return found;
    }
}

public sealed record FieldModel(
    string Name,
    FieldKind Kind,
    bool IsNullable,
    string NativeType,
    bool IsLongText = false)
{
    public IReadOnlyList<FieldModel> Children { get; init; } = [];
}

public sealed record ForeignKeyModel(
    string Name,
    IReadOnlyList<string> Columns,
    string? TargetSchema,
    string TargetTable,
    IReadOnlyList<string> TargetColumns);
