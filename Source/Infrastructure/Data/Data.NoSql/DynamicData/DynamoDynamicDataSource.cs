using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.DynamicData;

namespace Data.NoSql.DynamicData;

public sealed class DynamoDynamicDataSource(
    string databaseId,
    IAmazonDynamoDB client,
    int sampleSize,
    int maxScanItems) : IDynamicDataSource
{
    public static DataSourceCapabilities Capabilities { get; } = new(Relations: false, MinMax: true, SumAverage: true);

    public static object? Plain(AttributeValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.NULL == true)
        {
            return null;
        }

        if (value.S is not null)
        {
            return value.S;
        }

        if (value.N is not null)
        {
            return Number(value.N);
        }

        if (value.BOOL is not null)
        {
            return value.BOOL.Value;
        }

        if (value.B is not null)
        {
            return value.B.ToArray();
        }

        if (value.M is { } map && (value.IsMSet || map.Count > 0))
        {
            return map.ToDictionary(pair => pair.Key, pair => Plain(pair.Value), StringComparer.Ordinal);
        }

        if (value.L is { } list && (value.IsLSet || list.Count > 0))
        {
            return list.Select(Plain).ToList();
        }

        if (value.SS is { Count: > 0 } strings)
        {
            return strings.Cast<object?>().ToList();
        }

        if (value.NS is { Count: > 0 } numbers)
        {
            return numbers.Select(number => (object?)Number(number)).ToList();
        }

        return value.BS is { Count: > 0 } binaries ? binaries.Select(binary => (object?)binary.ToArray()).ToList() : null;
    }

    public async Task<DataSourceSchema> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var tables = new List<string>();
        string? start = null;

        do
        {
            var page = await client.ListTablesAsync(new ListTablesRequest { ExclusiveStartTableName = start }, cancellationToken)
                .ConfigureAwait(false);

            tables.AddRange(page.TableNames ?? []);
            start = page.LastEvaluatedTableName;
        }
        while (!string.IsNullOrEmpty(start));

        var sets = new List<EntitySetModel>();

        foreach (var table in tables.Where(DocumentJson.IsSafeName).Order(StringComparer.Ordinal))
        {
            var description = await client.DescribeTableAsync(table, cancellationToken).ConfigureAwait(false);

            var keys = (description.Table.KeySchema ?? [])
                .OrderBy(key => key.KeyType == KeyType.HASH ? 0 : 1)
                .Select(key => key.AttributeName)
                .ToList();

            var scan = await client.ScanAsync(new ScanRequest { TableName = table, Limit = sampleSize }, cancellationToken)
                .ConfigureAwait(false);

            var fields = DocumentSchemaInference.Infer((scan.Items ?? []).Select(Document), keys.Count == 1 ? keys[0] : null)
                .Where(field => DocumentJson.IsSafeName(field.Name))
                .ToList();

            sets.Add(new EntitySetModel(null, table, fields, keys, []));
        }

        return new DataSourceSchema(databaseId, DatabaseType.DynamoDb, sets, Capabilities);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        DynamicQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await ScanAsync(query.Set, cancellationToken).ConfigureAwait(false);

        return InMemoryQuery.Apply(query.Set, rows, query.Filter, query.Sort, query.Skip, query.Take);
    }

    public async Task<long> CountAsync(EntitySetModel set, FilterNode? filter, CancellationToken cancellationToken = default)
    {
        var rows = await ScanAsync(set, cancellationToken).ConfigureAwait(false);

        return InMemoryQuery.Where(set, rows, filter).LongCount();
    }

    public async Task<IReadOnlyDictionary<string, object?>> AggregateAsync(
        EntitySetModel set,
        FilterNode? filter,
        AggregateFunction function,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken = default)
    {
        var rows = await ScanAsync(set, cancellationToken).ConfigureAwait(false);

        return InMemoryQuery.Aggregate(set, InMemoryQuery.Where(set, rows, filter), function, fields);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetByValuesAsync(
        EntitySetModel set,
        string field,
        IReadOnlyList<object> values,
        CancellationToken cancellationToken = default)
    {
        var rows = await ScanAsync(set, cancellationToken).ConfigureAwait(false);

        var filter = new ConditionFilter(field, FilterOperator.In, values.ToList());

        return InMemoryQuery.Where(set, rows, filter).ToList();
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRelatedAsync(
        RelatedQuery query,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Document databases expose no relations.");

    private static object Number(string text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            ? integer
            : decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static IReadOnlyDictionary<string, object?> Document(Dictionary<string, AttributeValue> item) =>
        item.ToDictionary(pair => pair.Key, pair => Plain(pair.Value), StringComparer.Ordinal);

    private async Task<List<IReadOnlyDictionary<string, object?>>> ScanAsync(EntitySetModel set, CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        Dictionary<string, AttributeValue>? start = null;

        do
        {
            var page = await client.ScanAsync(new ScanRequest { TableName = set.Name, ExclusiveStartKey = start }, cancellationToken)
                .ConfigureAwait(false);

            rows.AddRange((page.Items ?? []).Select(item => DocumentSchemaInference.Project(set.Fields, Document(item))));

            if (rows.Count > maxScanItems)
            {
                throw new InvalidOperationException(
                    $"DynamoDB table '{set.Name}' holds more than {maxScanItems} items; the auto schema filters DynamoDB in memory. "
                    + "Raise Api:GraphQlServer:AutoSchema:MaxScanItems or exclude the table.");
            }

            start = page.LastEvaluatedKey is { Count: > 0 } key ? key : null;
        }
        while (start is not null);

        return rows;
    }
}
