using Domain.Enums;

namespace Domain.Models.DynamicData;

public sealed record SchemaExposure(
    IReadOnlyList<ExposedDataSource> Sources,
    IReadOnlyList<string> Notes);

public sealed record ExposedDataSource(
    string DatabaseId,
    string FieldName,
    string TypeName,
    DataSourceCapabilities Capabilities,
    IReadOnlyList<ExposedEntitySet> Sets);

public sealed record ExposedEntitySet(
    EntitySetModel Model,
    string TypeName,
    string ListField,
    string? ByKeyField,
    ExposedField? Key,
    IReadOnlyList<ExposedField> Fields,
    IReadOnlyList<ExposedRelation> Relations);

public sealed record ExposedField(
    FieldModel Model,
    string Name,
    string Path,
    IReadOnlyList<FilterOperator> Operators,
    bool Sortable,
    bool MinMax,
    bool SumAverage)
{
    public IReadOnlyList<ExposedField> Children { get; init; } = [];

    public string? ObjectTypeName { get; init; }
}

public sealed record ExposedRelation(
    string Name,
    RelationKind Kind,
    string TargetTypeName,
    string LocalField,
    string RemoteField);

public sealed record DatabaseExposureStatus(string DatabaseId, DatabaseExposure Exposure)
{
    public bool Exposed => Exposure == DatabaseExposure.Exposed;
}
