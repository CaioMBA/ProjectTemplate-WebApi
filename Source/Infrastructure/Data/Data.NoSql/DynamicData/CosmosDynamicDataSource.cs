using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.DynamicData;
using Microsoft.Azure.Cosmos;

namespace Data.NoSql.DynamicData;

public sealed class CosmosQueryText(string text, IReadOnlyDictionary<string, object?> parameters)
{
    public string Text { get; } = text;

    public IReadOnlyDictionary<string, object?> Parameters { get; } = parameters;

    public QueryDefinition ToDefinition()
    {
        var definition = new QueryDefinition(Text);

        foreach (var (name, value) in Parameters)
        {
            definition = definition.WithParameter(name, value);
        }

        return definition;
    }
}

public static class CosmosQueryBuilder
{
    public static CosmosQueryText Select(DynamicQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var sql = new StringBuilder("SELECT * FROM c");

        AppendWhere(sql, query.Set, query.Filter, parameters);

        var terms = query.Sort
            .Select(term => $"{Path(query.Set, term.Field)} {(term.Direction == SortDirection.Descending ? "DESC" : "ASC")}")
            .ToList();

        if (terms.Count > 0)
        {
            sql.Append(" ORDER BY ").AppendJoin(", ", terms);
        }

        sql.Append(CultureInfo.InvariantCulture, $" OFFSET {query.Skip} LIMIT {query.Take}");

        return new CosmosQueryText(sql.ToString(), parameters);
    }

    public static CosmosQueryText Count(EntitySetModel set, FilterNode? filter)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var sql = new StringBuilder("SELECT VALUE COUNT(1) FROM c");

        AppendWhere(sql, set, filter, parameters);

        return new CosmosQueryText(sql.ToString(), parameters);
    }

    public static CosmosQueryText Aggregate(EntitySetModel set, FilterNode? filter, AggregateFunction function, string field)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);

        var name = function switch
        {
            AggregateFunction.Min => "MIN",
            AggregateFunction.Max => "MAX",
            AggregateFunction.Sum => "SUM",
            _ => "AVG",
        };

        var sql = new StringBuilder($"SELECT VALUE {name}({Path(set, field)}) FROM c");

        AppendWhere(sql, set, filter, parameters);

        return new CosmosQueryText(sql.ToString(), parameters);
    }

    public static CosmosQueryText ByValues(EntitySetModel set, string field, IReadOnlyList<object> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) { ["@p0"] = values.ToArray() };

        return new CosmosQueryText($"SELECT * FROM c WHERE ARRAY_CONTAINS(@p0, {Path(set, field)})", parameters);
    }

    private static void AppendWhere(StringBuilder sql, EntitySetModel set, FilterNode? filter, Dictionary<string, object?> parameters)
    {
        if (filter is not null)
        {
            sql.Append(" WHERE ").Append(Render(set, filter, parameters));
        }
    }

    private static string Render(EntitySetModel set, FilterNode filter, Dictionary<string, object?> parameters) => filter switch
    {
        AndFilter and => and.Nodes.Count == 0 ? "true" : $"({string.Join(" AND ", and.Nodes.Select(node => Render(set, node, parameters)))})",
        OrFilter or => or.Nodes.Count == 0 ? "false" : $"({string.Join(" OR ", or.Nodes.Select(node => Render(set, node, parameters)))})",
        NotFilter not => $"NOT ({Render(set, not.Node, parameters)})",
        ConditionFilter condition => Condition(set, condition, parameters),
        _ => throw new NotSupportedException($"Filter node {filter.GetType().Name} is not supported."),
    };

    private static string Condition(EntitySetModel set, ConditionFilter condition, Dictionary<string, object?> parameters)
    {
        var field = set.FindPath(condition.Field)
            ?? throw new InvalidOperationException($"'{condition.Field}' is not a field of '{set.Name}'.");

        if (!SchemaExposurePolicy.OperatorsFor(field).Contains(condition.Operator))
        {
            throw new InvalidOperationException($"Operator {condition.Operator} is not supported on '{condition.Field}'.");
        }

        var path = Path(set, condition.Field);
        var missing = $"(NOT IS_DEFINED({path}) OR IS_NULL({path}))";

        string Add(object? value)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"@p{parameters.Count}");
            parameters[name] = Value(value);

            return name;
        }

        var value = condition.Value;

        return condition.Operator switch
        {
            FilterOperator.Eq when value is null => missing,
            FilterOperator.Eq => $"{path} = {Add(value)}",
            FilterOperator.Neq when value is null => $"NOT {missing}",
            FilterOperator.Neq => $"({missing} OR {path} != {Add(value)})",
            FilterOperator.In => $"ARRAY_CONTAINS({Add(Items(value))}, {path})",
            FilterOperator.NotIn => $"NOT ARRAY_CONTAINS({Add(Items(value))}, {path})",
            FilterOperator.Contains => $"CONTAINS({path}, {Add(Text(value))}, true)",
            FilterOperator.NotContains => $"({missing} OR NOT CONTAINS({path}, {Add(Text(value))}, true))",
            FilterOperator.StartsWith => $"STARTSWITH({path}, {Add(Text(value))}, true)",
            FilterOperator.NotStartsWith => $"({missing} OR NOT STARTSWITH({path}, {Add(Text(value))}, true))",
            FilterOperator.EndsWith => $"ENDSWITH({path}, {Add(Text(value))}, true)",
            FilterOperator.NotEndsWith => $"({missing} OR NOT ENDSWITH({path}, {Add(Text(value))}, true))",
            FilterOperator.Like => $"RegexMatch({path}, {Add(InMemoryQuery.LikeToRegex(Text(value)).ToString())}, \"i\")",
            FilterOperator.Gt => $"{path} > {Add(value)}",
            FilterOperator.Gte => $"{path} >= {Add(value)}",
            FilterOperator.Lt => $"{path} < {Add(value)}",
            FilterOperator.Lte => $"{path} <= {Add(value)}",
            FilterOperator.IsNull => value is false ? $"NOT {missing}" : missing,
            _ => throw new NotSupportedException($"Operator {condition.Operator} is not supported."),
        };
    }

    private static string Path(EntitySetModel set, string path)
    {
        if (set.FindPath(path) is null)
        {
            throw new InvalidOperationException($"'{path}' is not a field of '{set.Name}'.");
        }

        var segments = path.Split('.');

        if (!segments.All(DocumentJson.IsSafeName))
        {
            throw new InvalidOperationException($"'{path}' cannot be queried.");
        }

        return "c" + string.Concat(segments.Select(segment => $"[\"{segment}\"]"));
    }

    private static object? Value(object? value) => value switch
    {
        DateTimeOffset offset => offset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString(),
        object?[] items => items.Select(Value).ToArray(),
        _ => value,
    };

    private static object?[] Items(object? value) =>
        (value is IEnumerable sequence and not string ? sequence.Cast<object?>() : [value]).ToArray();

    private static string Text(object? value) =>
        value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
}

public sealed class CosmosDynamicDataSource(
    string databaseId,
    Database database,
    int sampleSize) : IDynamicDataSource
{
    public const string KeyField = "id";

    public static DataSourceCapabilities Capabilities { get; } = new(Relations: false, MinMax: true, SumAverage: true);

    public async Task<DataSourceSchema> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var containers = new List<string>();

        using (var iterator = database.GetContainerQueryIterator<ContainerProperties>())
        {
            while (iterator.HasMoreResults)
            {
                containers.AddRange((await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false)).Select(container => container.Id));
            }
        }

        var sets = new List<EntitySetModel>();

        foreach (var name in containers.Where(DocumentJson.IsSafeName).Order(StringComparer.Ordinal))
        {
            var samples = await ReadAsync(
                    database.GetContainer(name),
                    new QueryDefinition(string.Create(CultureInfo.InvariantCulture, $"SELECT TOP {sampleSize} * FROM c")),
                    cancellationToken)
                .ConfigureAwait(false);

            var fields = DocumentSchemaInference.Infer(samples.Select(Document), KeyField)
                .Where(field => DocumentJson.IsSafeName(field.Name))
                .ToList();

            sets.Add(new EntitySetModel(null, name, fields, [KeyField], []));
        }

        return new DataSourceSchema(databaseId, DatabaseType.CosmosDb, sets, Capabilities);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        DynamicQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await RowsAsync(query.Set, CosmosQueryBuilder.Select(query), cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> CountAsync(EntitySetModel set, FilterNode? filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);

        var results = await ReadAsync(Container(set), CosmosQueryBuilder.Count(set, filter).ToDefinition(), cancellationToken)
            .ConfigureAwait(false);

        return results.Sum(result => result.GetInt64());
    }

    public async Task<IReadOnlyDictionary<string, object?>> AggregateAsync(
        EntitySetModel set,
        FilterNode? filter,
        AggregateFunction function,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(fields);

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            var values = await ReadAsync(Container(set), CosmosQueryBuilder.Aggregate(set, filter, function, field).ToDefinition(), cancellationToken)
                .ConfigureAwait(false);

            var kind = function switch
            {
                AggregateFunction.Sum => SchemaExposurePolicy.SumKind(set.FindPath(field)!.Kind),
                AggregateFunction.Average => FieldKind.Floating,
                _ => set.FindPath(field)!.Kind,
            };

            result[field] = values.Count == 0 ? null : DocumentSchemaInference.Canonical(kind, DocumentJson.Plain(values[0]));
        }

        return result;
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetByValuesAsync(
        EntitySetModel set,
        string field,
        IReadOnlyList<object> values,
        CancellationToken cancellationToken = default) =>
        await RowsAsync(set, CosmosQueryBuilder.ByValues(set, field, values), cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRelatedAsync(
        RelatedQuery query,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Document databases expose no relations.");

    private static IReadOnlyDictionary<string, object?> Document(JsonElement element) =>
        DocumentJson.Document(element, name => !name.StartsWith('_'));

    private static async Task<List<JsonElement>> ReadAsync(Container container, QueryDefinition definition, CancellationToken cancellationToken)
    {
        var results = new List<JsonElement>();

        using var iterator = container.GetItemQueryIterator<JsonElement>(definition);

        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RowsAsync(
        EntitySetModel set,
        CosmosQueryText query,
        CancellationToken cancellationToken)
    {
        var results = await ReadAsync(Container(set), query.ToDefinition(), cancellationToken).ConfigureAwait(false);

        return results.Select(result => DocumentSchemaInference.Project(set.Fields, Document(result))).ToList();
    }

    private Container Container(EntitySetModel set) => database.GetContainer(set.Name);
}
