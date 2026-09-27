using System.Globalization;
using Dapper;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.DynamicData;

namespace Data.Sql.DynamicData;

public sealed class SqlDynamicDataSource : IDynamicDataSource
{
    private readonly string _databaseId;

    private readonly SqlDatabaseHandle _handle;

    private readonly ISqlDatabaseAccess _access;

    private readonly SqlDynamicQueryBuilder _builder;

    public SqlDynamicDataSource(
        string databaseId,
        SqlDatabaseHandle handle,
        ISqlDatabaseAccess access,
        int maxInValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseId);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(access);

        _databaseId = databaseId;
        _handle = handle;
        _access = access;
        _builder = new SqlDynamicQueryBuilder(handle.Provider.Dialect, maxInValues);
    }

    public async Task<DataSourceSchema> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var reader = _handle.Provider.CatalogReader
            ?? throw new InvalidOperationException(
                $"Database '{_databaseId}' ({_handle.Provider.ProviderType}) has no catalog reader.");

        var connection = _handle.Provider.CreateConnection(_handle.Registration.ConnectionString);

        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var sets = await reader.ReadAsync(connection, cancellationToken).ConfigureAwait(false);

            var safe = sets
                .Where(set => SqlDynamicQueryBuilder.IsSafeIdentifier(set.Name)
                              && (set.Schema is null || SqlDynamicQueryBuilder.IsSafeIdentifier(set.Schema)))
                .Select(set => set with
                {
                    Fields = set.Fields.Where(field => SqlDynamicQueryBuilder.IsSafeIdentifier(field.Name)).ToList(),
                })
                .ToList();

            return new DataSourceSchema(_databaseId, _handle.Provider.ProviderType, safe, DataSourceCapabilities.Relational);
        }
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        DynamicQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await RunAsync(query.Set, [_builder.Select(query)], cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> CountAsync(
        EntitySetModel set,
        FilterNode? filter,
        CancellationToken cancellationToken = default)
    {
        var statement = _builder.Count(set, filter);

        var value = await _access
            .ExecuteScalarAsync<object>(statement.Sql, Parameters(statement), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Convert.ToInt64(value ?? 0L, CultureInfo.InvariantCulture);
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

        if (fields.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var statement = _builder.Aggregate(set, filter, function, fields);

        var row = await _access
            .QueryFirstOrDefaultAsync<object>(statement.Sql, Parameters(statement), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var values = row as IDictionary<string, object?> ?? new Dictionary<string, object?>();

        return fields.ToDictionary(
            name => name,
            name =>
            {
                var kind = AggregateKind(set.FindField(name)!.Kind, function);

                return FieldValues.ToCanonical(kind, Lookup(values, name));
            },
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetByValuesAsync(
        EntitySetModel set,
        string field,
        IReadOnlyList<object> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            return [];
        }

        return await RunAsync(set, _builder.ByValues(set, field, values), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRelatedAsync(
        RelatedQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Values.Count == 0 || query.Take == 0)
        {
            return [];
        }

        return await RunAsync(query.Set, _builder.Related(query), cancellationToken).ConfigureAwait(false);
    }

    public static FieldKind AggregateKind(FieldKind kind, AggregateFunction function) => function switch
    {
        AggregateFunction.Sum => SchemaExposurePolicy.SumKind(kind),
        AggregateFunction.Average => FieldKind.Floating,
        _ => kind,
    };

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RunAsync(
        EntitySetModel set,
        IReadOnlyList<SqlStatement> statements,
        CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        foreach (var statement in statements)
        {
            var result = await _access
                .QueryAsync<object>(statement.Sql, Parameters(statement), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            rows.AddRange(result.Select(row => Canonical(set, (IDictionary<string, object?>)row)));
        }

        return rows;
    }

    private static Dictionary<string, object?> Canonical(EntitySetModel set, IDictionary<string, object?> row) =>
        set.Fields
            .Where(SchemaExposurePolicy.IsScalar)
            .ToDictionary(
                field => field.Name,
                field => FieldValues.ToCanonical(field.Kind, Lookup(row, field.Name)),
                StringComparer.Ordinal);

    private static object? Lookup(IDictionary<string, object?> row, string name)
    {
        if (row.TryGetValue(name, out var value))
        {
            return value;
        }

        return row.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    private static DynamicParameters Parameters(SqlStatement statement)
    {
        var parameters = new DynamicParameters();

        foreach (var (name, value) in statement.Parameters)
        {
            parameters.Add(name, value);
        }

        return parameters;
    }
}
