using System.Collections;
using System.Globalization;
using System.Text;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.DynamicData;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;

namespace Data.NoSql.DynamicData;

public sealed class RavenQueryText(string text, IReadOnlyDictionary<string, object?> parameters)
{
    public string Text { get; } = text;

    public IReadOnlyDictionary<string, object?> Parameters { get; } = parameters;
}

public static class RavenQueryBuilder
{
    public const string KeyField = "id";

    private const string Projection = " select { Id: id(d), Json: JSON.stringify(d) }";

    public static RavenQueryText Select(EntitySetModel set, FilterNode? filter, IReadOnlyList<SortTerm> sort)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(sort);

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var rql = new StringBuilder(From(set));

        AppendWhere(rql, set, filter, parameters);

        var terms = sort.Select(term => $"{OrderPath(set, term.Field)}{(term.Direction == SortDirection.Descending ? " desc" : string.Empty)}").ToList();

        rql.Append(" order by ").Append(terms.Count > 0 ? string.Join(", ", terms) : "id()");

        return new RavenQueryText(rql.Append(Projection).ToString(), parameters);
    }

    public static RavenQueryText Count(EntitySetModel set, FilterNode? filter)
    {
        ArgumentNullException.ThrowIfNull(set);

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var rql = new StringBuilder(From(set));

        AppendWhere(rql, set, filter, parameters);

        return new RavenQueryText(rql.ToString(), parameters);
    }

    public static RavenQueryText ByValues(EntitySetModel set, string field, IReadOnlyList<object> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) { ["p0"] = values.Select(Value).ToArray() };

        return new RavenQueryText($"{From(set)} where {Path(set, field)} in ($p0){Projection}", parameters);
    }

    public static string From(EntitySetModel set)
    {
        ArgumentNullException.ThrowIfNull(set);

        if (set.Name.Contains('\'', StringComparison.Ordinal) || set.Name.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{set.Name}' cannot be queried.");
        }

        return $"from '{set.Name}' as d";
    }

    private static void AppendWhere(StringBuilder rql, EntitySetModel set, FilterNode? filter, Dictionary<string, object?> parameters)
    {
        if (filter is not null)
        {
            rql.Append(" where ").Append(Render(set, filter, parameters));
        }
    }

    private static string Render(EntitySetModel set, FilterNode filter, Dictionary<string, object?> parameters) => filter switch
    {
        AndFilter and => and.Nodes.Count == 0 ? "true" : $"({string.Join(" and ", and.Nodes.Select(node => Render(set, node, parameters)))})",
        OrFilter or => or.Nodes.Count == 0 ? "(true and not true)" : $"({string.Join(" or ", or.Nodes.Select(node => Render(set, node, parameters)))})",
        NotFilter not => Not(Render(set, not.Node, parameters)),
        ConditionFilter condition => Condition(set, condition, parameters),
        _ => throw new NotSupportedException($"Filter node {filter.GetType().Name} is not supported."),
    };

    private static string Not(string expression) => $"(true and not {expression})";

    private static string Condition(EntitySetModel set, ConditionFilter condition, Dictionary<string, object?> parameters)
    {
        var field = set.FindPath(condition.Field)
            ?? throw new InvalidOperationException($"'{condition.Field}' is not a field of '{set.Name}'.");

        if (!SchemaExposurePolicy.OperatorsFor(field).Contains(condition.Operator))
        {
            throw new InvalidOperationException($"Operator {condition.Operator} is not supported on '{condition.Field}'.");
        }

        var path = Path(set, condition.Field);
        var missing = $"({path} = null or {Not($"exists({path})")})";

        string Add(object? value)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"p{parameters.Count}");
            parameters[name] = value;

            return "$" + name;
        }

        string Regex(string pattern) => $"regex({path}, {Add("(?i)" + pattern)})";

        var value = condition.Value;
        var text = Text(value);

        return condition.Operator switch
        {
            FilterOperator.Eq when value is null => missing,
            FilterOperator.Eq => $"{path} = {Add(Value(value))}",
            FilterOperator.Neq when value is null => Not(missing),
            FilterOperator.Neq => Not($"{path} = {Add(Value(value))}"),
            FilterOperator.In => $"{path} in ({Add(Items(value))})",
            FilterOperator.NotIn => Not($"{path} in ({Add(Items(value))})"),
            FilterOperator.Contains => Regex(System.Text.RegularExpressions.Regex.Escape(text)),
            FilterOperator.NotContains => Not(Regex(System.Text.RegularExpressions.Regex.Escape(text))),
            FilterOperator.StartsWith => Regex("^" + System.Text.RegularExpressions.Regex.Escape(text)),
            FilterOperator.NotStartsWith => Not(Regex("^" + System.Text.RegularExpressions.Regex.Escape(text))),
            FilterOperator.EndsWith => Regex(System.Text.RegularExpressions.Regex.Escape(text) + "$"),
            FilterOperator.NotEndsWith => Not(Regex(System.Text.RegularExpressions.Regex.Escape(text) + "$")),
            FilterOperator.Like => $"regex({path}, {Add(InMemoryQuery.LikeToRegex(text).ToString())})",
            FilterOperator.Gt => $"{path} > {Add(Value(value))}",
            FilterOperator.Gte => $"{path} >= {Add(Value(value))}",
            FilterOperator.Lt => $"{path} < {Add(Value(value))}",
            FilterOperator.Lte => $"{path} <= {Add(Value(value))}",
            FilterOperator.IsNull => value is false ? Not(missing) : missing,
            _ => throw new NotSupportedException($"Operator {condition.Operator} is not supported."),
        };
    }

    private static string Path(EntitySetModel set, string path)
    {
        if (set.FindPath(path) is null)
        {
            throw new InvalidOperationException($"'{path}' is not a field of '{set.Name}'.");
        }

        if (path == KeyField)
        {
            return "id()";
        }

        var segments = path.Split('.');

        return segments.All(DocumentJson.IsSafeName)
            ? "d." + string.Join('.', segments)
            : throw new InvalidOperationException($"'{path}' cannot be queried.");
    }

    private static string OrderPath(EntitySetModel set, string path)
    {
        var field = set.FindPath(path) ?? throw new InvalidOperationException($"'{path}' is not a field of '{set.Name}'.");

        return field.Kind switch
        {
            FieldKind.Integer32 or FieldKind.Integer64 => $"{Path(set, path)} as long",
            FieldKind.Fixed or FieldKind.Floating => $"{Path(set, path)} as double",
            _ => Path(set, path),
        };
    }

    private static object? Value(object? value) => value switch
    {
        DateTimeOffset offset => offset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString(),
        _ => value,
    };

    private static object?[] Items(object? value) =>
        (value is IEnumerable sequence and not string ? sequence.Cast<object?>() : [value]).Select(Value).ToArray();

    private static string Text(object? value) =>
        value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
}

public sealed class RavenDynamicDataSource(
    string databaseId,
    IDocumentStore store,
    int sampleSize) : IDynamicDataSource
{
    public static DataSourceCapabilities Capabilities { get; } = new(Relations: false, MinMax: false, SumAverage: false);

    public async Task<DataSourceSchema> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var statistics = await store.Maintenance
            .SendAsync(new GetCollectionStatisticsOperation(), cancellationToken)
            .ConfigureAwait(false);

        var sets = new List<EntitySetModel>();

        foreach (var name in statistics.Collections.Keys.Where(DocumentJson.IsSafeName).Order(StringComparer.Ordinal))
        {
            var probe = new EntitySetModel(null, name, [], [], []);
            var samples = await RunAsync(new RavenQueryText($"{RavenQueryBuilder.From(probe)} select {{ Id: id(d), Json: JSON.stringify(d) }}", new Dictionary<string, object?>()), 0, sampleSize, cancellationToken)
                .ConfigureAwait(false);

            var fields = DocumentSchemaInference.Infer(samples, RavenQueryBuilder.KeyField)
                .Where(field => DocumentJson.IsSafeName(field.Name))
                .ToList();

            sets.Add(new EntitySetModel(null, name, fields, [RavenQueryBuilder.KeyField], []));
        }

        return new DataSourceSchema(databaseId, DatabaseType.RavenDb, sets, Capabilities);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        DynamicQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var documents = await RunAsync(RavenQueryBuilder.Select(query.Set, query.Filter, query.Sort), query.Skip, query.Take, cancellationToken)
            .ConfigureAwait(false);

        return documents.Select(document => DocumentSchemaInference.Project(query.Set.Fields, document)).ToList();
    }

    public async Task<long> CountAsync(EntitySetModel set, FilterNode? filter, CancellationToken cancellationToken = default)
    {
        var text = RavenQueryBuilder.Count(set, filter);

        using var session = store.OpenAsyncSession();

        var query = session.Advanced.AsyncRawQuery<object>(text.Text).Statistics(out var statistics).Take(0);

        foreach (var (name, value) in text.Parameters)
        {
            query = query.AddParameter(name, value);
        }

        await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        return statistics.TotalResults;
    }

    public Task<IReadOnlyDictionary<string, object?>> AggregateAsync(
        EntitySetModel set,
        FilterNode? filter,
        AggregateFunction function,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("RavenDB aggregates are not exposed by the auto schema.");

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetByValuesAsync(
        EntitySetModel set,
        string field,
        IReadOnlyList<object> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(values);

        var documents = await RunAsync(RavenQueryBuilder.ByValues(set, field, values), 0, values.Count, cancellationToken).ConfigureAwait(false);

        return documents.Select(document => DocumentSchemaInference.Project(set.Fields, document)).ToList();
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRelatedAsync(
        RelatedQuery query,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Document databases expose no relations.");

    private async Task<List<IReadOnlyDictionary<string, object?>>> RunAsync(
        RavenQueryText text,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        using var session = store.OpenAsyncSession();

        var query = session.Advanced.AsyncRawQuery<JsonRow>(text.Text).Skip(skip).Take(take);

        foreach (var (name, value) in text.Parameters)
        {
            query = query.AddParameter(name, value);
        }

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Where(row => row.Json is not null)
            .Select(row =>
            {
                var document = new Dictionary<string, object?>(
                    DocumentJson.Document(row.Json!, name => name != "@metadata"),
                    StringComparer.Ordinal)
                {
                    [RavenQueryBuilder.KeyField] = row.Id,
                };

                return (IReadOnlyDictionary<string, object?>)document;
            })
            .ToList();
    }

    public sealed class JsonRow
    {
        public string? Id { get; set; }

        public string? Json { get; set; }
    }
}
