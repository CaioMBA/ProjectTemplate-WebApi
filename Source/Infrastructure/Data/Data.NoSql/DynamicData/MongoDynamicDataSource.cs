using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.DynamicData;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using SortDirection = Domain.Enums.SortDirection;

namespace Data.NoSql.DynamicData;

public sealed class MongoDynamicDataSource(
    string databaseId,
    IMongoDatabase database,
    int sampleSize) : IDynamicDataSource
{
    public const string KeyField = "_id";

    public const string ObjectIdType = "objectId";

    public static DataSourceCapabilities Capabilities { get; } = new(Relations: false, MinMax: true, SumAverage: true);

    public async Task<DataSourceSchema> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var names = await (await database.ListCollectionNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sets = new List<EntitySetModel>();

        foreach (var name in names.Where(name => !name.StartsWith("system.", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var samples = await database.GetCollection<BsonDocument>(name)
                .Aggregate()
                .Sample(sampleSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var objectIds = samples.Count > 0 && samples.All(sample => sample.GetValue(KeyField, BsonNull.Value).IsObjectId);
            var fields = DocumentSchemaInference.Infer(samples.Select(sample => (IReadOnlyDictionary<string, object?>)Plain(sample)!), KeyField);

            if (objectIds)
            {
                fields = fields.Select(field => field.Name == KeyField ? field with { NativeType = ObjectIdType } : field).ToList();
            }

            sets.Add(new EntitySetModel(null, name, fields, fields.Any(field => field.Name == KeyField) ? [KeyField] : [], []));
        }

        return new DataSourceSchema(databaseId, DatabaseType.MongoDb, sets, Capabilities);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        DynamicQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var documents = await Collection(query.Set)
            .Find(Render(query.Set, query.Filter))
            .Sort(Sort(query.Set, query.Sort))
            .Skip(query.Skip)
            .Limit(query.Take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Rows(query.Set, documents);
    }

    public async Task<long> CountAsync(EntitySetModel set, FilterNode? filter, CancellationToken cancellationToken = default) =>
        await Collection(set).CountDocumentsAsync(Render(set, filter), cancellationToken: cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<string, object?>> AggregateAsync(
        EntitySetModel set,
        FilterNode? filter,
        AggregateFunction function,
        IReadOnlyList<string> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(fields);

        var operation = function switch
        {
            AggregateFunction.Min => "$min",
            AggregateFunction.Max => "$max",
            AggregateFunction.Sum => "$sum",
            _ => "$avg",
        };

        var group = new BsonDocument("_id", BsonNull.Value);

        for (var index = 0; index < fields.Count; index++)
        {
            RequireField(set, fields[index]);
            group.Add($"f{index}", new BsonDocument(operation, "$" + fields[index]));
        }

        var result = await Collection(set)
            .Aggregate()
            .Match(Render(set, filter))
            .Group(group)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return fields
            .Select((path, index) => (path, index))
            .ToDictionary(
                item => item.path,
                item => result is null
                    ? null
                    : DocumentSchemaInference.Canonical(
                        AggregateKind(RequireField(set, item.path).Kind, function),
                        Plain(result.GetValue($"f{item.index}", BsonNull.Value))),
                StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetByValuesAsync(
        EntitySetModel set,
        string field,
        IReadOnlyList<object> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(values);

        var model = RequireField(set, field);
        var filter = Builders<BsonDocument>.Filter.In(field, values.Select(value => Bson(model, value)));

        var documents = await Collection(set).Find(filter).ToListAsync(cancellationToken).ConfigureAwait(false);

        return Rows(set, documents);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRelatedAsync(
        RelatedQuery query,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Document databases expose no relations.");

    public static FilterDefinition<BsonDocument> Render(EntitySetModel set, FilterNode? filter)
    {
        ArgumentNullException.ThrowIfNull(set);

        var builder = Builders<BsonDocument>.Filter;

        return filter switch
        {
            null => builder.Empty,
            AndFilter and => and.Nodes.Count == 0 ? builder.Empty : builder.And(and.Nodes.Select(node => Render(set, node))),
            OrFilter or => or.Nodes.Count == 0 ? builder.In(KeyField, Array.Empty<BsonValue>()) : builder.Or(or.Nodes.Select(node => Render(set, node))),
            NotFilter not => builder.Not(Render(set, not.Node)),
            ConditionFilter condition => Condition(set, condition),
            _ => throw new NotSupportedException($"Filter node {filter.GetType().Name} is not supported."),
        };
    }

    public static string ToJson(FilterDefinition<BsonDocument> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return filter.Render(new RenderArgs<BsonDocument>(BsonDocumentSerializer.Instance, BsonSerializer.SerializerRegistry))
            .ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson });
    }

    public static object? Plain(BsonValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.BsonType switch
        {
            BsonType.Document => value.AsBsonDocument.Elements.ToDictionary(element => element.Name, element => Plain(element.Value), StringComparer.Ordinal),
            BsonType.Array => value.AsBsonArray.Select(Plain).ToList(),
            BsonType.Null or BsonType.Undefined => null,
            BsonType.ObjectId => value.AsObjectId.ToString(),
            BsonType.String => value.AsString,
            BsonType.Int32 => (long)value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.Double => value.AsDouble,
            BsonType.Decimal128 => (decimal)value.AsDecimal128,
            BsonType.Boolean => value.AsBoolean,
            BsonType.DateTime => new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero),
            BsonType.Binary when value.AsBsonBinaryData.SubType is BsonBinarySubType.UuidStandard
                => value.AsBsonBinaryData.ToGuid(GuidRepresentation.Standard),
            BsonType.Binary => value.AsBsonBinaryData.Bytes,
            _ => value.ToString(),
        };
    }

    private static FilterDefinition<BsonDocument> Condition(EntitySetModel set, ConditionFilter condition)
    {
        var builder = Builders<BsonDocument>.Filter;
        var field = RequireField(set, condition.Field);

        if (!SchemaExposurePolicy.OperatorsFor(field).Contains(condition.Operator))
        {
            throw new InvalidOperationException($"Operator {condition.Operator} is not supported on '{condition.Field}'.");
        }

        var path = condition.Field;
        var value = condition.Value;

        return condition.Operator switch
        {
            FilterOperator.Eq => builder.Eq(path, Bson(field, value)),
            FilterOperator.Neq => builder.Ne(path, Bson(field, value)),
            FilterOperator.In => builder.In(path, Items(value).Select(item => Bson(field, item))),
            FilterOperator.NotIn => builder.Nin(path, Items(value).Select(item => Bson(field, item))),
            FilterOperator.Contains => builder.Regex(path, Pattern(Regex.Escape(Text(value)))),
            FilterOperator.NotContains => builder.Not(builder.Regex(path, Pattern(Regex.Escape(Text(value))))),
            FilterOperator.StartsWith => builder.Regex(path, Pattern("^" + Regex.Escape(Text(value)))),
            FilterOperator.NotStartsWith => builder.Not(builder.Regex(path, Pattern("^" + Regex.Escape(Text(value))))),
            FilterOperator.EndsWith => builder.Regex(path, Pattern(Regex.Escape(Text(value)) + "$")),
            FilterOperator.NotEndsWith => builder.Not(builder.Regex(path, Pattern(Regex.Escape(Text(value)) + "$"))),
            FilterOperator.Like => builder.Regex(path, Pattern(InMemoryQuery.LikeToRegex(Text(value)).ToString())),
            FilterOperator.Gt => builder.Gt(path, Bson(field, value)),
            FilterOperator.Gte => builder.Gte(path, Bson(field, value)),
            FilterOperator.Lt => builder.Lt(path, Bson(field, value)),
            FilterOperator.Lte => builder.Lte(path, Bson(field, value)),
            FilterOperator.IsNull => value is false ? builder.Ne(path, BsonNull.Value) : builder.Eq(path, BsonNull.Value),
            _ => throw new NotSupportedException($"Operator {condition.Operator} is not supported."),
        };
    }

    private static BsonRegularExpression Pattern(string pattern) => new(pattern, "i");

    private static BsonValue Bson(FieldModel field, object? value) => value switch
    {
        null => BsonNull.Value,
        string text when field.NativeType == ObjectIdType && ObjectId.TryParse(text, out var objectId) => objectId,
        Guid guid => new BsonBinaryData(guid, GuidRepresentation.Standard),
        DateTimeOffset offset => new BsonDateTime(offset.UtcDateTime),
        DateTime dateTime => new BsonDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        decimal number when field.Kind == FieldKind.Integer64 && decimal.Truncate(number) == number => (long)number,
        int number => (long)number,
        _ => BsonValue.Create(value),
    };

    private static IEnumerable<object?> Items(object? value) =>
        value is IEnumerable sequence and not string ? sequence.Cast<object?>() : [value];

    private static string Text(object? value) =>
        value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    private static SortDefinition<BsonDocument> Sort(EntitySetModel set, IReadOnlyList<SortTerm> sort)
    {
        var builder = Builders<BsonDocument>.Sort;

        var terms = sort
            .Select(term =>
            {
                RequireField(set, term.Field);

                return term.Direction == SortDirection.Descending ? builder.Descending(term.Field) : builder.Ascending(term.Field);
            })
            .ToList();

        if (sort.All(term => term.Field != KeyField))
        {
            terms.Add(builder.Ascending(KeyField));
        }

        return builder.Combine(terms);
    }

    private static FieldKind AggregateKind(FieldKind kind, AggregateFunction function) => function switch
    {
        AggregateFunction.Sum => SchemaExposurePolicy.SumKind(kind),
        AggregateFunction.Average => FieldKind.Floating,
        _ => kind,
    };

    private static FieldModel RequireField(EntitySetModel set, string path) =>
        set.FindPath(path) ?? throw new InvalidOperationException($"'{path}' is not a field of '{set.QualifiedName}'.");

    private static List<IReadOnlyDictionary<string, object?>> Rows(EntitySetModel set, List<BsonDocument> documents) =>
        documents
            .Select(document => DocumentSchemaInference.Project(set.Fields, (IReadOnlyDictionary<string, object?>)Plain(document)!))
            .ToList();

    private IMongoCollection<BsonDocument> Collection(EntitySetModel set) => database.GetCollection<BsonDocument>(set.Name);
}
