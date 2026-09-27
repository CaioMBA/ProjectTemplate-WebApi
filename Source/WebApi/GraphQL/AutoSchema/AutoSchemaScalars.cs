using Domain.Abstractions;
using Domain.Enums;
using Domain.Models.DynamicData;
using HotChocolate.Types;

namespace WebApi.GraphQL.AutoSchema;

public static class AutoSchemaScalars
{
    public const string SortDirectionTypeName = "AutoSortDirection";

    public static IReadOnlyList<string> SharedTypeNames { get; } =
    [
        SortDirectionTypeName,
        "AutoTextFilter",
        "AutoLongTextFilter",
        "AutoIntFilter",
        "AutoLongFilter",
        "AutoDecimalFilter",
        "AutoFloatFilter",
        "AutoBooleanFilter",
        "AutoLocalDateTimeFilter",
        "AutoDateTimeFilter",
        "AutoDateFilter",
        "AutoTimeFilter",
        "AutoUuidFilter",
        "AutoNullFilter",
    ];

    public static Type ScalarOf(FieldKind kind) => kind switch
    {
        FieldKind.Integer32 => typeof(IntType),
        FieldKind.Integer64 => typeof(LongType),
        FieldKind.Fixed => typeof(DecimalType),
        FieldKind.Floating => typeof(FloatType),
        FieldKind.Flag => typeof(BooleanType),
        FieldKind.Timestamp => typeof(LocalDateTimeType),
        FieldKind.TimestampOffset => typeof(DateTimeType),
        FieldKind.Date => typeof(DateType),
        FieldKind.Time => typeof(LocalTimeType),
        FieldKind.Uuid => typeof(UuidType),
        FieldKind.Binary => typeof(Base64StringType),
        _ => typeof(StringType),
    };

    public static Type NonNull(Type type) => typeof(NonNullType<>).MakeGenericType(type);

    public static Type ListOf(Type type) => typeof(ListType<>).MakeGenericType(type);

    public static Type OutputOf(FieldModel field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var scalar = ScalarOf(field.Kind);

        return field.IsNullable ? scalar : NonNull(scalar);
    }

    public static string OperationFilterName(FieldModel field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Kind switch
        {
            FieldKind.Text => field.IsLongText ? "AutoLongTextFilter" : "AutoTextFilter",
            FieldKind.Integer32 => "AutoIntFilter",
            FieldKind.Integer64 => "AutoLongFilter",
            FieldKind.Fixed => "AutoDecimalFilter",
            FieldKind.Floating => "AutoFloatFilter",
            FieldKind.Flag => "AutoBooleanFilter",
            FieldKind.Timestamp => "AutoLocalDateTimeFilter",
            FieldKind.TimestampOffset => "AutoDateTimeFilter",
            FieldKind.Date => "AutoDateFilter",
            FieldKind.Time => "AutoTimeFilter",
            FieldKind.Uuid => "AutoUuidFilter",
            _ => "AutoNullFilter",
        };
    }

    public static string OperatorName(FilterOperator filterOperator) => filterOperator switch
    {
        FilterOperator.Eq => "eq",
        FilterOperator.Neq => "neq",
        FilterOperator.In => "in",
        FilterOperator.NotIn => "nin",
        FilterOperator.Contains => "contains",
        FilterOperator.NotContains => "ncontains",
        FilterOperator.StartsWith => "startsWith",
        FilterOperator.NotStartsWith => "nstartsWith",
        FilterOperator.EndsWith => "endsWith",
        FilterOperator.NotEndsWith => "nendsWith",
        FilterOperator.Like => "like",
        FilterOperator.Gt => "gt",
        FilterOperator.Gte => "gte",
        FilterOperator.Lt => "lt",
        FilterOperator.Lte => "lte",
        _ => "isNull",
    };

    public static FilterOperator? OperatorOf(string name) =>
        Enum.GetValues<FilterOperator>()
            .Select(value => (FilterOperator?)value)
            .FirstOrDefault(value => OperatorName(value!.Value) == name);

    public static InputObjectType OperationFilter(FieldModel field)
    {
        var scalar = ScalarOf(field.Kind);

        return new InputObjectType(descriptor =>
        {
            descriptor.Name(OperationFilterName(field));

            foreach (var filterOperator in SchemaExposurePolicy.OperatorsFor(field))
            {
                var type = filterOperator switch
                {
                    FilterOperator.IsNull => typeof(BooleanType),
                    FilterOperator.In or FilterOperator.NotIn => ListOf(scalar),
                    FilterOperator.Contains
                        or FilterOperator.NotContains
                        or FilterOperator.StartsWith
                        or FilterOperator.NotStartsWith
                        or FilterOperator.EndsWith
                        or FilterOperator.NotEndsWith
                        or FilterOperator.Like => typeof(StringType),
                    _ => scalar,
                };

                descriptor.Field(OperatorName(filterOperator)).Type(type);
            }
        });
    }

    public static EnumType<SortDirection> SortDirectionType() =>
        new(descriptor =>
        {
            descriptor.Name(SortDirectionTypeName);
            descriptor.Value(SortDirection.Ascending).Name("ASC");
            descriptor.Value(SortDirection.Descending).Name("DESC");
        });
}
