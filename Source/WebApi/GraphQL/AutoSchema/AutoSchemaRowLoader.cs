using System.Collections;
using System.Globalization;
using System.Text;
using Domain.Interfaces.Persistence;
using Domain.Models.DynamicData;
using GreenDonut;

namespace WebApi.GraphQL.AutoSchema;

public sealed class AutoSchemaRowLoader(
    IServiceProvider services,
    IBatchScheduler batchScheduler,
    DataLoaderOptions options)
    : BatchDataLoader<AutoSchemaRowLoader.RowKey, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(batchScheduler, options)
{
    public static object Normalize(object value) => value switch
    {
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal =>
            Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        float or double => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        _ => value,
    };

    public static string FilterSignature(FilterNode? filter) => filter switch
    {
        null => "-",
        AndFilter and => $"&({string.Join(',', and.Nodes.Select(FilterSignature))})",
        OrFilter or => $"|({string.Join(',', or.Nodes.Select(FilterSignature))})",
        NotFilter not => $"!({FilterSignature(not.Node)})",
        ConditionFilter condition => $"{condition.Field}:{condition.Operator}:{ValueSignature(condition.Value)}",
        _ => filter.GetType().Name,
    };

    protected override async Task<IReadOnlyDictionary<RowKey, IReadOnlyList<IReadOnlyDictionary<string, object?>>>> LoadBatchAsync(
        IReadOnlyList<RowKey> keys,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<RowKey, IReadOnlyList<IReadOnlyDictionary<string, object?>>>();

        foreach (var group in keys.GroupBy(key => key.Request.Signature, StringComparer.Ordinal))
        {
            var request = group.First().Request;
            var source = services.GetRequiredKeyedService<IDynamicDataSource>(request.DatabaseId);
            var values = group.Select(key => key.Value).ToList();

            var rows = request.Related
                ? await source.GetRelatedAsync(
                        new RelatedQuery(request.Set, request.Field, values, request.Filter, request.Sort, request.Skip, request.Take),
                        cancellationToken)
                    .ConfigureAwait(false)
                : await source.GetByValuesAsync(request.Set, request.Field, values, cancellationToken).ConfigureAwait(false);

            var byValue = rows
                .Where(row => row.GetValueOrDefault(request.Field) is not null)
                .GroupBy(row => Normalize(row[request.Field]!))
                .ToDictionary(rowGroup => rowGroup.Key, rowGroup => (IReadOnlyList<IReadOnlyDictionary<string, object?>>)rowGroup.ToList());

            foreach (var key in group)
            {
                result[key] = byValue.GetValueOrDefault(Normalize(key.Value)) ?? [];
            }
        }

        return result;
    }

    private static string ValueSignature(object? value) => value switch
    {
        null => "null",
        string text => $"s'{text.Replace("'", "''", StringComparison.Ordinal)}'",
        IEnumerable sequence => $"[{string.Join(';', sequence.Cast<object?>().Select(ValueSignature))}]",
        IFormattable formattable => $"{value.GetType().Name}'{formattable.ToString(null, CultureInfo.InvariantCulture)}'",
        _ => $"{value.GetType().Name}'{value}'",
    };

    public sealed class RowRequest
    {
        public RowRequest(
            string databaseId,
            ExposedEntitySet set,
            string field,
            bool related,
            FilterNode? filter = null,
            IReadOnlyList<SortTerm>? sort = null,
            int skip = 0,
            int take = 0)
        {
            DatabaseId = databaseId;
            Set = set.Model;
            Field = field;
            Related = related;
            Filter = filter;
            Sort = sort ?? [];
            Skip = skip;
            Take = take;

            var signature = new StringBuilder()
                .Append(databaseId).Append('|')
                .Append(set.TypeName).Append('|')
                .Append(field).Append('|')
                .Append(related ? "related" : "lookup").Append('|')
                .Append(FilterSignature(filter)).Append('|')
                .AppendJoin(',', Sort.Select(term => $"{term.Field}:{term.Direction}")).Append('|')
                .Append(skip.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(take.ToString(CultureInfo.InvariantCulture));

            Signature = signature.ToString();
        }

        public string DatabaseId { get; }

        public EntitySetModel Set { get; }

        public string Field { get; }

        public bool Related { get; }

        public FilterNode? Filter { get; }

        public IReadOnlyList<SortTerm> Sort { get; }

        public int Skip { get; }

        public int Take { get; }

        public string Signature { get; }
    }

    public sealed class RowKey(RowRequest request, object value) : IEquatable<RowKey>
    {
        private readonly object _normalized = Normalize(value);

        public RowRequest Request { get; } = request;

        public object Value { get; } = value;

        public bool Equals(RowKey? other) =>
            other is not null
            && string.Equals(Request.Signature, other.Request.Signature, StringComparison.Ordinal)
            && Equals(_normalized, other._normalized);

        public override bool Equals(object? obj) => obj is RowKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Request.Signature, _normalized);
    }
}
