using System.IO.Enumeration;
using Domain.Enums;
using Domain.Models.Configuration;
using Domain.Models.DynamicData;

namespace Domain.Abstractions;

public static class SchemaExposurePolicy
{
    private static readonly FilterOperator[] _nullOnly = [FilterOperator.IsNull];

    private static readonly FilterOperator[] _equality =
    [
        FilterOperator.Eq,
        FilterOperator.Neq,
        FilterOperator.In,
        FilterOperator.NotIn,
        FilterOperator.IsNull,
    ];

    private static readonly FilterOperator[] _ordered =
    [
        .. _equality,
        FilterOperator.Gt,
        FilterOperator.Gte,
        FilterOperator.Lt,
        FilterOperator.Lte,
    ];

    private static readonly FilterOperator[] _pattern =
    [
        FilterOperator.Contains,
        FilterOperator.NotContains,
        FilterOperator.StartsWith,
        FilterOperator.NotStartsWith,
        FilterOperator.EndsWith,
        FilterOperator.NotEndsWith,
        FilterOperator.Like,
    ];

    private static readonly FilterOperator[] _text = [.. _ordered, .. _pattern];

    private static readonly FilterOperator[] _longText = [.. _pattern, FilterOperator.IsNull];

    public const string StatusFieldName = "autoSchemaStatus";

    public const string StatusTypeName = "AutoSchemaStatus";

    public const string DatabaseStatusTypeName = "AutoSchemaDatabaseStatus";

    public static IReadOnlyList<string> ReservedTypeNames { get; } = [StatusTypeName, DatabaseStatusTypeName];

    public static string ReasonOf(DatabaseExposure exposure) => exposure switch
    {
        DatabaseExposure.Exposed => "exposed",
        DatabaseExposure.Excluded => "excluded",
        DatabaseExposure.Unreachable => "unreachable",
        DatabaseExposure.NothingToExpose => "nothing to expose",
        DatabaseExposure.NoDataSource => "no data source",
        _ => throw new ArgumentOutOfRangeException(nameof(exposure), exposure, "Unknown database exposure."),
    };

    public static IReadOnlyList<string> DerivedTypeSuffixes { get; } =
        ["Filter", "Sort", "Page", "Aggregate", "MinMax", "Sum", "Average"];

    public static IReadOnlyList<string> BuiltInExcludedSchemas { get; } =
        ["pg_catalog", "pg_toast", "information_schema", "sys", "hangfire"];

    public static IReadOnlyList<string> BuiltInExcludedTables { get; } =
    [
        "__EFMigrationsHistory",
        "outbox_messages",
        "sqlite_*",
        "hangfire*",
        "system.*",
        "@*",
        "RDB$*",
        "MON$*",
        "SEC$*",
        "BIN$*",
    ];

    public static IReadOnlyList<FilterOperator> OperatorsFor(FieldModel field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Kind switch
        {
            FieldKind.Text => field.IsLongText ? _longText : _text,
            FieldKind.Integer32
                or FieldKind.Integer64
                or FieldKind.Fixed
                or FieldKind.Floating
                or FieldKind.Timestamp
                or FieldKind.TimestampOffset
                or FieldKind.Date
                or FieldKind.Time => _ordered,
            FieldKind.Flag or FieldKind.Uuid => _equality,
            _ => _nullOnly,
        };
    }

    public static bool IsScalar(FieldModel field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return field.Kind is not (FieldKind.Unknown or FieldKind.Document or FieldKind.Array);
    }

    public static bool IsSortable(FieldModel field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return !field.IsLongText && field.Kind is FieldKind.Text
            or FieldKind.Integer32
            or FieldKind.Integer64
            or FieldKind.Fixed
            or FieldKind.Floating
            or FieldKind.Flag
            or FieldKind.Timestamp
            or FieldKind.TimestampOffset
            or FieldKind.Date
            or FieldKind.Time
            or FieldKind.Uuid;
    }

    public static bool IsKeyCandidate(FieldModel field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return IsSortable(field) && field.Kind != FieldKind.Flag;
    }

    public static bool SupportsMinMax(FieldModel field, DataSourceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(capabilities);

        return capabilities.MinMax
               && IsSortable(field)
               && field.Kind is not (FieldKind.Flag or FieldKind.Uuid);
    }

    public static bool SupportsSumAverage(FieldModel field, DataSourceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(capabilities);

        return capabilities.SumAverage
               && field.Kind is FieldKind.Integer32 or FieldKind.Integer64 or FieldKind.Fixed or FieldKind.Floating;
    }

    public static FieldKind SumKind(FieldKind kind) => kind switch
    {
        FieldKind.Integer32 or FieldKind.Integer64 => FieldKind.Integer64,
        FieldKind.Fixed => FieldKind.Fixed,
        _ => FieldKind.Floating,
    };

    public static bool IsDatabaseExcluded(string databaseId, GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(databaseId);
        ArgumentNullException.ThrowIfNull(options);

        return Matches(options.ExcludeDatabases, databaseId);
    }

    public static bool IsSchemaExcluded(string databaseId, string? schema, GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (schema is null)
        {
            return false;
        }

        return Matches(BuiltInExcludedSchemas, schema)
               || Matches(options.ExcludeSchemas, schema, $"{databaseId}:{schema}");
    }

    public static bool IsTableExcluded(string databaseId, EntitySetModel set, GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(options);

        if (IsSchemaExcluded(databaseId, set.Schema, options) || Matches(BuiltInExcludedTables, set.Name))
        {
            return true;
        }

        return Matches(
            options.ExcludeTables,
            set.Name,
            set.QualifiedName,
            $"{databaseId}:{set.Name}",
            $"{databaseId}:{set.QualifiedName}");
    }

    public static bool IsColumnExcluded(
        string databaseId,
        EntitySetModel set,
        string path,
        GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(options);

        return Matches(
            options.ExcludeColumns,
            path,
            $"{set.Name}.{path}",
            $"{set.QualifiedName}.{path}",
            $"{databaseId}:{set.Name}.{path}",
            $"{databaseId}:{set.QualifiedName}.{path}");
    }

    public static string NothingExposedMessage(
        IReadOnlyList<string> databaseIds,
        IReadOnlyDictionary<string, string> skipped)
    {
        ArgumentNullException.ThrowIfNull(databaseIds);
        ArgumentNullException.ThrowIfNull(skipped);

        if (databaseIds.Count == 0)
        {
            return "GraphQL is enabled (Api:GraphQlServer:Enabled) but Settings:Databases is empty, so only autoSchemaStatus is served.";
        }

        var reasons = databaseIds.Select(id => $"{id}: {skipped.GetValueOrDefault(id) ?? "not exposed"}");

        return "GraphQL is enabled (Api:GraphQlServer:Enabled) but no database could be exposed: "
               + string.Join("; ", reasons)
               + ". Only autoSchemaStatus is served until a restart can expose a database; fix the connection or relax Api:GraphQlServer:AutoSchema exclusions.";
    }

    public static SchemaExposure Expose(
        IReadOnlyList<DataSourceSchema> schemas,
        GraphQlAutoSchemaOptions options,
        IEnumerable<string> reservedRootFields,
        IEnumerable<string> reservedTypeNames)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(options);

        var notes = new List<string>();
        var rootScope = new GraphQlNameScope([StatusFieldName, .. reservedRootFields]);
        var typeScope = new GraphQlNameScope([.. ReservedTypeNames, .. reservedTypeNames]);
        var sources = new List<ExposedDataSource>();

        foreach (var schema in schemas)
        {
            if (IsDatabaseExcluded(schema.DatabaseId, options))
            {
                notes.Add($"Database '{schema.DatabaseId}' is excluded by configuration.");

                continue;
            }

            if (!schema.EntitySets.Any(set => !IsTableExcluded(schema.DatabaseId, set, options)))
            {
                notes.Add($"Database '{schema.DatabaseId}' has no exposable tables or collections; it is left out.");

                continue;
            }

            var source = new SourceBuilder(schema, options, typeScope, notes).Build(
                rootScope.Claim(GraphQlNames.Camel(schema.DatabaseId)));

            if (source.Sets.Count == 0)
            {
                notes.Add($"Database '{schema.DatabaseId}' has no exposable tables or collections; it is left out.");

                continue;
            }

            sources.Add(source);
        }

        return new SchemaExposure(sources, notes);
    }

    private static bool Matches(IEnumerable<string> patterns, params string[] candidates) =>
        patterns.Any(pattern => candidates.Any(candidate =>
            FileSystemName.MatchesSimpleExpression(pattern, candidate, ignoreCase: true)));

    private sealed class SourceBuilder(
        DataSourceSchema schema,
        GraphQlAutoSchemaOptions options,
        GraphQlNameScope typeScope,
        List<string> notes)
    {
        private readonly Dictionary<string, GraphQlNameScope> _fieldScopes = new(StringComparer.Ordinal);

        public ExposedDataSource Build(string fieldName)
        {
            var prefix = GraphQlNames.Pascal(schema.DatabaseId);
            var typeName = typeScope.Claim(prefix + "Database");

            var visible = schema.EntitySets
                .Where(set => !IsTableExcluded(schema.DatabaseId, set, options))
                .Select(set => (Original: set, Model: Filter(set)))
                .Where(pair =>
                {
                    if (pair.Model.Fields.Count > 0)
                    {
                        return true;
                    }

                    notes.Add($"{schema.DatabaseId}:{pair.Original.QualifiedName} has no exposable fields and is skipped.");

                    return false;
                })
                .ToList();

            var duplicatedNames = visible
                .GroupBy(pair => pair.Model.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rootFields = new GraphQlNameScope();
            var sets = new List<ExposedEntitySet>();

            foreach (var (_, model) in visible)
            {
                var baseName = duplicatedNames.Contains(model.Name) && model.Schema is not null
                    ? $"{model.Schema}_{model.Name}"
                    : model.Name;

                var setTypeName = typeScope.Claim(prefix + GraphQlNames.Pascal(baseName), DerivedTypeSuffixes);
                var listField = rootFields.Claim(GraphQlNames.Camel(baseName));
                var fieldScope = new GraphQlNameScope();

                _fieldScopes[setTypeName] = fieldScope;

                var fields = ExposeFields(model.Fields, parentPath: null, setTypeName, fieldScope);

                var key = model.KeyFields.Count == 1
                    ? fields.FirstOrDefault(field => field.Path == model.KeyFields[0] && IsKeyCandidate(field.Model))
                    : null;

                if (key is null && model.KeyFields.Count != 1)
                {
                    notes.Add($"{schema.DatabaseId}:{model.QualifiedName} has no single-column key; no by-id query is generated.");
                }

                var byKeyField = key is null ? null : rootFields.Claim(listField + "ById");

                sets.Add(new ExposedEntitySet(model, setTypeName, listField, byKeyField, key, fields, []));
            }

            if (schema.Capabilities.Relations)
            {
                sets = AddRelations(sets);
            }

            return new ExposedDataSource(schema.DatabaseId, fieldName, typeName, schema.Capabilities, sets);
        }

        private EntitySetModel Filter(EntitySetModel set)
        {
            var fields = FilterFields(set, set.Fields, parentPath: null);
            var visibleTop = fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

            return set with
            {
                Fields = fields,
                KeyFields = set.KeyFields.All(visibleTop.Contains) ? set.KeyFields : [],
            };
        }

        private List<FieldModel> FilterFields(EntitySetModel set, IReadOnlyList<FieldModel> fields, string? parentPath)
        {
            var result = new List<FieldModel>();

            foreach (var field in fields)
            {
                var path = parentPath is null ? field.Name : $"{parentPath}.{field.Name}";

                if (IsColumnExcluded(schema.DatabaseId, set, path, options))
                {
                    continue;
                }

                if (field.Kind == FieldKind.Unknown)
                {
                    notes.Add($"{schema.DatabaseId}:{set.QualifiedName}.{path} has an unsupported type ({field.NativeType}) and is hidden.");

                    continue;
                }

                if (field.Kind == FieldKind.Document)
                {
                    var children = FilterFields(set, field.Children, path);

                    if (children.Count > 0)
                    {
                        result.Add(field with { Children = children });
                    }

                    continue;
                }

                result.Add(field);
            }

            return result;
        }

        private List<ExposedField> ExposeFields(
            IReadOnlyList<FieldModel> fields,
            string? parentPath,
            string ownerTypeName,
            GraphQlNameScope scope)
        {
            var exposed = new List<ExposedField>();

            foreach (var field in fields)
            {
                var path = parentPath is null ? field.Name : $"{parentPath}.{field.Name}";
                var name = scope.Claim(GraphQlNames.Camel(field.Name));

                var result = new ExposedField(
                    field,
                    name,
                    path,
                    OperatorsFor(field),
                    IsSortable(field),
                    SupportsMinMax(field, schema.Capabilities),
                    SupportsSumAverage(field, schema.Capabilities));

                if (field.Kind == FieldKind.Document)
                {
                    var objectTypeName = typeScope.Claim(ownerTypeName + GraphQlNames.Pascal(field.Name), ["Filter"]);

                    result = result with
                    {
                        ObjectTypeName = objectTypeName,
                        Children = ExposeFields(field.Children, path, objectTypeName, new GraphQlNameScope()),
                    };
                }

                exposed.Add(result);
            }

            return exposed;
        }

        private List<ExposedEntitySet> AddRelations(List<ExposedEntitySet> sets)
        {
            var relations = sets.ToDictionary(set => set.TypeName, _ => new List<ExposedRelation>(), StringComparer.Ordinal);

            foreach (var source in sets)
            {
                var original = schema.EntitySets.First(set =>
                    set.Schema == source.Model.Schema && set.Name == source.Model.Name);

                foreach (var foreignKey in original.ForeignKeys.OrderBy(key => key.Name, StringComparer.Ordinal))
                {
                    if (foreignKey.Columns.Count != 1 || foreignKey.TargetColumns.Count != 1)
                    {
                        notes.Add($"{schema.DatabaseId}:{original.QualifiedName} foreign key '{foreignKey.Name}' is composite; no navigation is generated.");

                        continue;
                    }

                    var target = sets.FirstOrDefault(set =>
                        set.Model.Schema == (foreignKey.TargetSchema ?? source.Model.Schema)
                        && set.Model.Name == foreignKey.TargetTable);

                    var localField = source.Fields.FirstOrDefault(field => field.Path == foreignKey.Columns[0]);
                    var remoteField = target?.Fields.FirstOrDefault(field => field.Path == foreignKey.TargetColumns[0]);

                    if (target is null
                        || localField is null
                        || remoteField is null
                        || !IsKeyCandidate(localField.Model)
                        || !IsKeyCandidate(remoteField.Model))
                    {
                        continue;
                    }

                    var manyToOneName = ManyToOneName(foreignKey.Columns[0], target, _fieldScopes[source.TypeName]);

                    relations[source.TypeName].Add(new ExposedRelation(
                        manyToOneName,
                        RelationKind.ManyToOne,
                        target.TypeName,
                        localField.Path,
                        remoteField.Path));

                    var oneToManyName = OneToManyName(source, foreignKey.Columns[0], _fieldScopes[target.TypeName]);

                    relations[target.TypeName].Add(new ExposedRelation(
                        oneToManyName,
                        RelationKind.OneToMany,
                        source.TypeName,
                        remoteField.Path,
                        localField.Path));
                }
            }

            return sets.Select(set => set with { Relations = relations[set.TypeName] }).ToList();
        }

        private static string ManyToOneName(string column, ExposedEntitySet target, GraphQlNameScope scope)
        {
            var words = GraphQlNames.Words(column).ToList();

            if (words.Count > 1 && string.Equals(words[^1], "id", StringComparison.OrdinalIgnoreCase))
            {
                words.RemoveAt(words.Count - 1);
            }

            var preferred = GraphQlNames.Camel(string.Join('_', words));

            if (!scope.IsClaimed(preferred) && words.Count > 0 && !string.Equals(preferred, GraphQlNames.Camel(column), StringComparison.Ordinal))
            {
                return scope.Claim(preferred);
            }

            var byTarget = target.ListField;

            return scope.IsClaimed(byTarget)
                ? scope.Claim(byTarget + "By" + GraphQlNames.Pascal(column))
                : scope.Claim(byTarget);
        }

        private static string OneToManyName(ExposedEntitySet source, string column, GraphQlNameScope scope)
        {
            var preferred = source.ListField;

            return scope.IsClaimed(preferred)
                ? scope.Claim(preferred + "By" + GraphQlNames.Pascal(column))
                : scope.Claim(preferred);
        }
    }
}
