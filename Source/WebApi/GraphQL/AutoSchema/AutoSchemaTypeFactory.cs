using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.DynamicData;
using GreenDonut.DependencyInjection;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace WebApi.GraphQL.AutoSchema;

public sealed class AutoSchemaTypeFactory(GraphQlAutoSchemaOptions options, string queryTypeName)
{
    private const string ItemsField = "items";

    private const string TotalCountField = "totalCount";

    private const string AggregateFieldName = "aggregate";

    public IReadOnlyList<ITypeSystemMember> Create(SchemaExposure exposure, IReadOnlyList<DatabaseExposureStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(exposure);
        ArgumentNullException.ThrowIfNull(statuses);

        var types = new List<ITypeSystemMember>(StatusTypes(queryTypeName, statuses));

        if (exposure.Sources.Count == 0)
        {
            return types;
        }

        types.Add(AutoSchemaScalars.SortDirectionType());
        var operationFilters = new Dictionary<string, InputObjectType>(StringComparer.Ordinal);

        foreach (var source in exposure.Sources)
        {
            var sets = source.Sets.ToDictionary(set => set.TypeName, StringComparer.Ordinal);

            foreach (var set in source.Sets)
            {
                types.Add(EntityType(source, set, sets));
                types.AddRange(NestedTypes(set.Fields));
                types.AddRange(FilterTypes(set.TypeName, set.Fields, operationFilters));
                types.Add(PageType(source, set));
                types.AddRange(AggregateTypes(source, set));

                if (set.Fields.Any(field => field.Sortable))
                {
                    types.Add(SortType(set));
                }
            }

            types.Add(DatabaseType(source));
        }

        types.AddRange(operationFilters.Values);
        types.Add(QueryExtension(queryTypeName, exposure.Sources));

        return types;
    }

    private static IEnumerable<ITypeSystemMember> StatusTypes(string rootTypeName, IReadOnlyList<DatabaseExposureStatus> statuses)
    {
        yield return new ObjectType(descriptor =>
        {
            descriptor.Name(SchemaExposurePolicy.DatabaseStatusTypeName);
            descriptor.Field("id").Type<NonNullType<StringType>>().Resolve(context => context.Parent<DatabaseExposureStatus>().DatabaseId);
            descriptor.Field("exposed").Type<NonNullType<BooleanType>>().Resolve(context => context.Parent<DatabaseExposureStatus>().Exposed);
            descriptor.Field("reason").Type<NonNullType<StringType>>()
                .Resolve(context => SchemaExposurePolicy.ReasonOf(context.Parent<DatabaseExposureStatus>().Exposure));
        });

        yield return new ObjectType(descriptor =>
        {
            descriptor.Name(SchemaExposurePolicy.StatusTypeName);
            descriptor.Field("databases")
                .Type(new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(SchemaExposurePolicy.DatabaseStatusTypeName)))))
                .Resolve(_ => statuses);
        });

        yield return new ObjectTypeExtension(descriptor =>
        {
            descriptor.Name(rootTypeName);
            descriptor.Field(SchemaExposurePolicy.StatusFieldName)
                .Type(new NonNullTypeNode(new NamedTypeNode(SchemaExposurePolicy.StatusTypeName)))
                .Resolve(_ => statuses);
        });
    }

    private static ObjectTypeExtension QueryExtension(string rootTypeName, IReadOnlyList<ExposedDataSource> sources) =>
        new(descriptor =>
        {
            descriptor.Name(rootTypeName);

            foreach (var source in sources)
            {
                descriptor.Field(source.FieldName)
                    .Type(new NonNullTypeNode(new NamedTypeNode(source.TypeName)))
                    .Resolve(_ => source);
            }
        });

    private ObjectType DatabaseType(ExposedDataSource source) =>
        new(descriptor =>
        {
            descriptor.Name(source.TypeName);

            foreach (var set in source.Sets)
            {
                var list = descriptor.Field(set.ListField)
                    .Type(new NonNullTypeNode(new NamedTypeNode(set.TypeName + "Page")));

                AddPageArguments(list, set);

                list.Resolve(context => Page(context, source, set, parentField: null, parentValue: null));

                if (set.ByKeyField is null || set.Key is null)
                {
                    continue;
                }

                var key = set.Key;

                descriptor.Field(set.ByKeyField)
                    .Argument("id", argument => argument.Type(AutoSchemaScalars.NonNull(AutoSchemaScalars.ScalarOf(key.Model.Kind))))
                    .Type(new NamedTypeNode(set.TypeName))
                    .Resolve(async (context, cancellationToken) =>
                    {
                        var id = context.ArgumentValue<object>("id");
                        var request = new AutoSchemaRowLoader.RowRequest(source.DatabaseId, set, key.Path, related: false);
                        var rows = await Loader(context).LoadAsync(new AutoSchemaRowLoader.RowKey(request, id), cancellationToken)
                            .ConfigureAwait(false);

                        return rows is { Count: > 0 } ? rows[0] : null;
                    });
            }
        });

    private static void AddPageArguments(IObjectFieldDescriptor field, ExposedEntitySet set)
    {
        field.Argument(AutoSchemaArguments.Where, argument => argument.Type(new NamedTypeNode(set.TypeName + "Filter")));

        if (set.Fields.Any(candidate => candidate.Sortable))
        {
            field.Argument(AutoSchemaArguments.Order, argument => argument.Type(
                new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(set.TypeName + "Sort")))));
        }

        field.Argument(AutoSchemaArguments.Skip, argument => argument.Type<IntType>());
        field.Argument(AutoSchemaArguments.Take, argument => argument.Type<IntType>());
    }

    private AutoSchemaPage Page(
        IResolverContext context,
        ExposedDataSource source,
        ExposedEntitySet set,
        string? parentField,
        object? parentValue)
    {
        var filter = AutoSchemaArguments.ParseFilter(context.ArgumentValue<object?>(AutoSchemaArguments.Where), set.Fields, options);

        var sort = set.Fields.Any(field => field.Sortable)
            ? AutoSchemaArguments.ParseSort(context.ArgumentValue<object?>(AutoSchemaArguments.Order), set.Fields)
            : [];

        var (skip, take) = AutoSchemaArguments.ParsePaging(
            context.ArgumentValue<object?>(AutoSchemaArguments.Skip),
            context.ArgumentValue<object?>(AutoSchemaArguments.Take),
            options);

        return new AutoSchemaPage(source, set, filter, sort, skip, take, parentField, parentValue);
    }

    private ObjectType EntityType(
        ExposedDataSource source,
        ExposedEntitySet set,
        Dictionary<string, ExposedEntitySet> sets) =>
        new(descriptor =>
        {
            descriptor.Name(set.TypeName);

            AddValueFields(descriptor, set.Fields);

            foreach (var relation in set.Relations)
            {
                var target = sets[relation.TargetTypeName];

                if (relation.Kind == RelationKind.ManyToOne)
                {
                    descriptor.Field(relation.Name)
                        .Type(new NamedTypeNode(target.TypeName))
                        .Resolve(async (context, cancellationToken) =>
                        {
                            var value = context.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(relation.LocalField);

                            if (value is null)
                            {
                                return null;
                            }

                            var request = new AutoSchemaRowLoader.RowRequest(source.DatabaseId, target, relation.RemoteField, related: false);
                            var rows = await Loader(context).LoadAsync(new AutoSchemaRowLoader.RowKey(request, value), cancellationToken)
                                .ConfigureAwait(false);

                            return rows is { Count: > 0 } ? rows[0] : null;
                        });

                    continue;
                }

                var many = descriptor.Field(relation.Name)
                    .Type(new NonNullTypeNode(new NamedTypeNode(target.TypeName + "Page")));

                AddPageArguments(many, target);

                many.Resolve(context => Page(
                    context,
                    source,
                    target,
                    relation.RemoteField,
                    context.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(relation.LocalField)));
            }
        });

    private static void AddValueFields(IObjectTypeDescriptor descriptor, IReadOnlyList<ExposedField> fields)
    {
        foreach (var field in fields)
        {
            var key = field.Model.Name;
            var output = descriptor.Field(field.Name);

            output = field.ObjectTypeName is null
                ? output.Type(AutoSchemaScalars.OutputOf(field.Model))
                : output.Type(new NamedTypeNode(field.ObjectTypeName));

            output.Resolve(context => context.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(key));
        }
    }

    private static IEnumerable<ObjectType> NestedTypes(IReadOnlyList<ExposedField> fields)
    {
        foreach (var field in fields.Where(candidate => candidate.ObjectTypeName is not null))
        {
            yield return new ObjectType(descriptor =>
            {
                descriptor.Name(field.ObjectTypeName!);
                AddValueFields(descriptor, field.Children);
            });

            foreach (var nested in NestedTypes(field.Children))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<InputObjectType> FilterTypes(
        string ownerTypeName,
        IReadOnlyList<ExposedField> fields,
        Dictionary<string, InputObjectType> operationFilters)
    {
        var filterName = ownerTypeName + "Filter";

        foreach (var model in fields.Where(candidate => candidate.Children.Count == 0).Select(field => field.Model))
        {
            var name = AutoSchemaScalars.OperationFilterName(model);

            if (!operationFilters.ContainsKey(name))
            {
                operationFilters[name] = AutoSchemaScalars.OperationFilter(model);
            }
        }

        yield return new InputObjectType(descriptor =>
        {
            descriptor.Name(filterName);

            foreach (var field in fields)
            {
                var type = field.Children.Count > 0
                    ? field.ObjectTypeName + "Filter"
                    : AutoSchemaScalars.OperationFilterName(field.Model);

                descriptor.Field(field.Name).Type(new NamedTypeNode(type));
            }

            descriptor.Field("and").Type(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(filterName))));
            descriptor.Field("or").Type(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(filterName))));
            descriptor.Field("not").Type(new NamedTypeNode(filterName));
        });

        foreach (var field in fields.Where(candidate => candidate.Children.Count > 0))
        {
            foreach (var nested in FilterTypes(field.ObjectTypeName!, field.Children, operationFilters))
            {
                yield return nested;
            }
        }
    }

    private static InputObjectType SortType(ExposedEntitySet set) =>
        new(descriptor =>
        {
            descriptor.Name(set.TypeName + "Sort");

            foreach (var field in set.Fields.Where(candidate => candidate.Sortable))
            {
                descriptor.Field(field.Name).Type(new NamedTypeNode(AutoSchemaScalars.SortDirectionTypeName));
            }
        });

    private static ObjectType PageType(ExposedDataSource source, ExposedEntitySet set) =>
        new(descriptor =>
        {
            descriptor.Name(set.TypeName + "Page");

            descriptor.Field(ItemsField)
                .Type(new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(set.TypeName)))))
                .Resolve(async (context, cancellationToken) =>
                {
                    var page = context.Parent<AutoSchemaPage>();

                    if (page.ParentField is null)
                    {
                        return await Run(() => Source(context, source)
                                .QueryAsync(new DynamicQuery(set.Model, page.Filter, page.Sort, page.Skip, page.Take), cancellationToken))
                            .ConfigureAwait(false);
                    }

                    if (page.ParentValue is null)
                    {
                        return [];
                    }

                    var request = new AutoSchemaRowLoader.RowRequest(
                        source.DatabaseId,
                        set,
                        page.ParentField,
                        related: true,
                        page.Filter,
                        page.Sort,
                        page.Skip,
                        page.Take);

                    var rows = await Loader(context).LoadAsync(new AutoSchemaRowLoader.RowKey(request, page.ParentValue), cancellationToken)
                        .ConfigureAwait(false);

                    return rows ?? [];
                });

            descriptor.Field(TotalCountField)
                .Type<NonNullType<LongType>>()
                .Resolve(async (context, cancellationToken) => await CountAsync(context, source, set, cancellationToken).ConfigureAwait(false));

            descriptor.Field(AggregateFieldName)
                .Type(new NonNullTypeNode(new NamedTypeNode(set.TypeName + "Aggregate")))
                .Resolve(context => context.Parent<AutoSchemaPage>());
        });

    private static IEnumerable<ObjectType> AggregateTypes(ExposedDataSource source, ExposedEntitySet set)
    {
        var minMax = set.Fields.Where(field => field.MinMax && field.Children.Count == 0).ToList();
        var sums = set.Fields.Where(field => field.SumAverage && field.Children.Count == 0).ToList();

        yield return new ObjectType(descriptor =>
        {
            descriptor.Name(set.TypeName + "Aggregate");

            descriptor.Field("count")
                .Type<NonNullType<LongType>>()
                .Resolve(async (context, cancellationToken) => await CountAsync(context, source, set, cancellationToken).ConfigureAwait(false));

            if (minMax.Count > 0)
            {
                AggregateField(descriptor, "min", set.TypeName + "MinMax", source, set, AggregateFunction.Min, minMax);
                AggregateField(descriptor, "max", set.TypeName + "MinMax", source, set, AggregateFunction.Max, minMax);
            }

            if (sums.Count > 0)
            {
                AggregateField(descriptor, "sum", set.TypeName + "Sum", source, set, AggregateFunction.Sum, sums);
                AggregateField(descriptor, "avg", set.TypeName + "Average", source, set, AggregateFunction.Average, sums);
            }
        });

        if (minMax.Count > 0)
        {
            yield return AggregateValuesType(set.TypeName + "MinMax", minMax, kind => kind);
        }

        if (sums.Count > 0)
        {
            yield return AggregateValuesType(set.TypeName + "Sum", sums, SchemaExposurePolicy.SumKind);
            yield return AggregateValuesType(set.TypeName + "Average", sums, _ => FieldKind.Floating);
        }
    }

    private static void AggregateField(
        IObjectTypeDescriptor descriptor,
        string name,
        string typeName,
        ExposedDataSource source,
        ExposedEntitySet set,
        AggregateFunction function,
        IReadOnlyList<ExposedField> fields)
    {
        var paths = fields.Select(field => field.Path).ToList();

        descriptor.Field(name)
            .Type(new NonNullTypeNode(new NamedTypeNode(typeName)))
            .Resolve(async (context, cancellationToken) =>
            {
                var page = context.Parent<AutoSchemaPage>();

                return await Run(() => Source(context, source).AggregateAsync(set.Model, page.EffectiveFilter, function, paths, cancellationToken))
                    .ConfigureAwait(false);
            });
    }

    private static ObjectType AggregateValuesType(string typeName, IReadOnlyList<ExposedField> fields, Func<FieldKind, FieldKind> kindOf) =>
        new(descriptor =>
        {
            descriptor.Name(typeName);

            foreach (var field in fields)
            {
                var path = field.Path;

                descriptor.Field(field.Name)
                    .Type(AutoSchemaScalars.ScalarOf(kindOf(field.Model.Kind)))
                    .Resolve(context => context.Parent<IReadOnlyDictionary<string, object?>>().GetValueOrDefault(path));
            }
        });

    private static async Task<long> CountAsync(
        IResolverContext context,
        ExposedDataSource source,
        ExposedEntitySet set,
        CancellationToken cancellationToken)
    {
        var page = context.Parent<AutoSchemaPage>();

        if (page.ParentField is not null && page.ParentValue is null)
        {
            return 0;
        }

        return await Run(() => Source(context, source).CountAsync(set.Model, page.EffectiveFilter, cancellationToken))
            .ConfigureAwait(false);
    }

    private static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            throw AutoSchemaArguments.Error(exception.Message);
        }
    }

    private static IDynamicDataSource Source(IResolverContext context, ExposedDataSource source) =>
        context.Services.GetRequiredKeyedService<IDynamicDataSource>(source.DatabaseId);

    private static AutoSchemaRowLoader Loader(IResolverContext context) =>
        context.Services.GetRequiredService<IDataLoaderScope>().GetDataLoader<AutoSchemaRowLoader>();
}

public sealed class AutoSchemaPage(
    ExposedDataSource source,
    ExposedEntitySet set,
    FilterNode? filter,
    IReadOnlyList<SortTerm> sort,
    int skip,
    int take,
    string? parentField,
    object? parentValue)
{
    public ExposedDataSource Source { get; } = source;

    public ExposedEntitySet Set { get; } = set;

    public FilterNode? Filter { get; } = filter;

    public IReadOnlyList<SortTerm> Sort { get; } = sort;

    public int Skip { get; } = skip;

    public int Take { get; } = take;

    public string? ParentField { get; } = parentField;

    public object? ParentValue { get; } = parentValue;

    public FilterNode? EffectiveFilter
    {
        get
        {
            if (ParentField is null)
            {
                return Filter;
            }

            var parent = new ConditionFilter(ParentField, FilterOperator.Eq, ParentValue);

            return Filter is null ? parent : new AndFilter([parent, Filter]);
        }
    }
}
