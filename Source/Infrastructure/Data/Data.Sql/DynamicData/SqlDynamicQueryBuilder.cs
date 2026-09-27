using System.Collections;
using System.Globalization;
using System.Text;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.DynamicData;

namespace Data.Sql.DynamicData;

public sealed class SqlStatement(string sql, IReadOnlyDictionary<string, object?> parameters)
{
    public string Sql { get; } = sql;

    public IReadOnlyDictionary<string, object?> Parameters { get; } = parameters;
}

public sealed class SqlDynamicQueryBuilder
{
    public const string RowNumberColumn = "__rn";

    private const char Alias = 't';

    private const char OuterAlias = 'x';

    private static readonly char[] _forbiddenIdentifierCharacters = ['"', '`', '[', ']', '\0'];

    private readonly ISqlDialect _dialect;

    private readonly int _maxInValues;

    public SqlDynamicQueryBuilder(ISqlDialect dialect, int maxInValues = 500)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxInValues, 1);

        _dialect = dialect;
        _maxInValues = maxInValues;
    }

    public static bool IsSafeIdentifier(string identifier) =>
        !string.IsNullOrEmpty(identifier) && identifier.IndexOfAny(_forbiddenIdentifierCharacters) < 0;

    public SqlStatement Select(DynamicQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Skip);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Take);

        var parameters = new ParameterBag(_dialect);
        var sql = new StringBuilder();

        sql.Append("SELECT ").Append(Columns(query.Set, Alias))
            .Append(" FROM ").Append(Table(query.Set)).Append(' ').Append(Alias);

        AppendWhere(sql, query.Set, query.Filter, parameters, prefix: null);

        var order = OrderBy(query.Set, query.Sort, Alias);

        if (order.Length > 0)
        {
            sql.Append(" ORDER BY ").Append(order);
        }

        return new SqlStatement(_dialect.ApplyPagination(sql.ToString(), query.Skip, query.Take), parameters.Values);
    }

    public SqlStatement Count(EntitySetModel set, FilterNode? filter)
    {
        ArgumentNullException.ThrowIfNull(set);

        var parameters = new ParameterBag(_dialect);
        var sql = new StringBuilder("SELECT COUNT(*) FROM ").Append(Table(set)).Append(' ').Append(Alias);

        AppendWhere(sql, set, filter, parameters, prefix: null);

        return new SqlStatement(sql.ToString(), parameters.Values);
    }

    public SqlStatement Aggregate(
        EntitySetModel set,
        FilterNode? filter,
        AggregateFunction function,
        IReadOnlyList<string> fields)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(fields);

        if (fields.Count == 0)
        {
            throw new ArgumentException("At least one field is required for an aggregate.", nameof(fields));
        }

        var parameters = new ParameterBag(_dialect);

        var projections = fields.Select(name =>
        {
            var field = RequireField(set, name);

            var allowed = function is AggregateFunction.Min or AggregateFunction.Max
                ? SchemaExposurePolicy.SupportsMinMax(field, DataSourceCapabilities.Relational)
                : SchemaExposurePolicy.SupportsSumAverage(field, DataSourceCapabilities.Relational);

            if (!allowed)
            {
                throw new InvalidOperationException($"{function} is not supported on '{set.QualifiedName}.{field.Name}' ({field.Kind}).");
            }

            var column = Column(Alias, field.Name);

            var expression = function switch
            {
                AggregateFunction.Min => $"MIN({column})",
                AggregateFunction.Max => $"MAX({column})",
                AggregateFunction.Sum => $"SUM({column})",
                _ => $"AVG(CAST({column} AS {DoubleType}))",
            };

            return $"{expression} AS {Quote(field.Name)}";
        });

        var sql = new StringBuilder("SELECT ")
            .AppendJoin(", ", projections)
            .Append(" FROM ").Append(Table(set)).Append(' ').Append(Alias);

        AppendWhere(sql, set, filter, parameters, prefix: null);

        return new SqlStatement(sql.ToString(), parameters.Values);
    }

    public IReadOnlyList<SqlStatement> ByValues(EntitySetModel set, string field, IReadOnlyList<object> values)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(values);

        var key = RequireField(set, field);

        return values.Distinct().Chunk(_maxInValues).Select(chunk =>
        {
            var parameters = new ParameterBag(_dialect);

            var names = chunk.Select(value => parameters.Add(FieldValues.FromInput(key.Kind, value)));

            var sql = new StringBuilder("SELECT ").Append(Columns(set, Alias))
                .Append(" FROM ").Append(Table(set)).Append(' ').Append(Alias)
                .Append(" WHERE ").Append(Column(Alias, key.Name)).Append(" IN (").AppendJoin(", ", names).Append(')');

            return new SqlStatement(sql.ToString(), parameters.Values);
        }).ToList();
    }

    public IReadOnlyList<SqlStatement> Related(RelatedQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Skip);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Take);

        var set = query.Set;
        var partition = RequireField(set, query.Field);
        var order = OrderBy(set, query.Sort, Alias);

        if (order.Length == 0)
        {
            order = Column(Alias, partition.Name);
        }

        var lower = query.Skip.ToString(CultureInfo.InvariantCulture);
        var upper = ((long)query.Skip + query.Take).ToString(CultureInfo.InvariantCulture);
        var rowNumber = Quote(RowNumberColumn);

        return query.Values.Distinct().Chunk(_maxInValues).Select(chunk =>
        {
            var parameters = new ParameterBag(_dialect);
            var names = chunk.Select(value => parameters.Add(FieldValues.FromInput(partition.Kind, value))).ToList();

            var inner = new StringBuilder("SELECT ").Append(Columns(set, Alias))
                .Append(", ROW_NUMBER() OVER (PARTITION BY ").Append(Column(Alias, partition.Name))
                .Append(" ORDER BY ").Append(order).Append(") AS ").Append(rowNumber)
                .Append(" FROM ").Append(Table(set)).Append(' ').Append(Alias);

            var membership = $"{Column(Alias, partition.Name)} IN ({string.Join(", ", names)})";

            AppendWhere(inner, set, query.Filter, parameters, membership);

            var sql = new StringBuilder("SELECT ").Append(Columns(set, OuterAlias))
                .Append(" FROM (").Append(inner).Append(") ").Append(OuterAlias)
                .Append(" WHERE ").Append(OuterAlias).Append('.').Append(rowNumber).Append(" > ").Append(lower)
                .Append(" AND ").Append(OuterAlias).Append('.').Append(rowNumber).Append(" <= ").Append(upper)
                .Append(" ORDER BY ").Append(Column(OuterAlias, partition.Name))
                .Append(", ").Append(OuterAlias).Append('.').Append(rowNumber);

            return new SqlStatement(sql.ToString(), parameters.Values);
        }).ToList();
    }

    private string DoubleType => _dialect.ProviderType == DatabaseType.Mysql ? "DOUBLE" : "DOUBLE PRECISION";

    private void AppendWhere(
        StringBuilder sql,
        EntitySetModel set,
        FilterNode? filter,
        ParameterBag parameters,
        string? prefix)
    {
        var clauses = new List<string>();

        if (prefix is not null)
        {
            clauses.Add(prefix);
        }

        if (filter is not null)
        {
            clauses.Add(Render(set, filter, parameters));
        }

        if (clauses.Count > 0)
        {
            sql.Append(" WHERE ").AppendJoin(" AND ", clauses);
        }
    }

    private string Render(EntitySetModel set, FilterNode node, ParameterBag parameters) => node switch
    {
        AndFilter and => Combine(and.Nodes.Select(child => Render(set, child, parameters)).ToList(), " AND ", "1 = 1"),
        OrFilter or => Combine(or.Nodes.Select(child => Render(set, child, parameters)).ToList(), " OR ", "1 = 0"),
        NotFilter not => $"NOT ({Render(set, not.Node, parameters)})",
        ConditionFilter condition => Condition(set, condition, parameters),
        _ => throw new NotSupportedException($"Filter node {node.GetType().Name} is not supported."),
    };

    private static string Combine(List<string> parts, string separator, string empty) =>
        parts.Count == 0 ? empty : $"({string.Join(separator, parts)})";

    private string Condition(EntitySetModel set, ConditionFilter condition, ParameterBag parameters)
    {
        var field = RequireField(set, condition.Field);

        if (!SchemaExposurePolicy.OperatorsFor(field).Contains(condition.Operator))
        {
            throw new InvalidOperationException(
                $"Operator {condition.Operator} is not supported on '{set.QualifiedName}.{field.Name}' ({field.Kind}).");
        }

        var column = Column(Alias, field.Name);
        var value = condition.Value;

        return condition.Operator switch
        {
            FilterOperator.Eq when value is null => $"{column} IS NULL",
            FilterOperator.Eq => $"{column} = {parameters.Add(Input(field, value))}",
            FilterOperator.Neq when value is null => $"{column} IS NOT NULL",
            FilterOperator.Neq => $"({column} <> {parameters.Add(Input(field, value))} OR {column} IS NULL)",
            FilterOperator.In => In(column, field, value, parameters, negate: false),
            FilterOperator.NotIn => In(column, field, value, parameters, negate: true),
            FilterOperator.Contains => Like(column, $"%{Escape(value)}%", parameters, escaped: true),
            FilterOperator.StartsWith => Like(column, $"{Escape(value)}%", parameters, escaped: true),
            FilterOperator.EndsWith => Like(column, $"%{Escape(value)}", parameters, escaped: true),
            FilterOperator.Like => Like(column, Text(value), parameters, escaped: false),
            FilterOperator.NotContains => Negate(column, Like(column, $"%{Escape(value)}%", parameters, escaped: true)),
            FilterOperator.NotStartsWith => Negate(column, Like(column, $"{Escape(value)}%", parameters, escaped: true)),
            FilterOperator.NotEndsWith => Negate(column, Like(column, $"%{Escape(value)}", parameters, escaped: true)),
            FilterOperator.Gt => $"{column} > {parameters.Add(Input(field, Required(value, condition)))}",
            FilterOperator.Gte => $"{column} >= {parameters.Add(Input(field, Required(value, condition)))}",
            FilterOperator.Lt => $"{column} < {parameters.Add(Input(field, Required(value, condition)))}",
            FilterOperator.Lte => $"{column} <= {parameters.Add(Input(field, Required(value, condition)))}",
            FilterOperator.IsNull => value is false ? $"{column} IS NOT NULL" : $"{column} IS NULL",
            _ => throw new NotSupportedException($"Operator {condition.Operator} is not supported."),
        };
    }

    private string In(string column, FieldModel field, object? value, ParameterBag parameters, bool negate)
    {
        if (value is not IEnumerable sequence || value is string)
        {
            throw new InvalidOperationException($"The in/nin operator on '{field.Name}' needs a list.");
        }

        var items = sequence.Cast<object?>().ToList();

        if (items.Count > _maxInValues)
        {
            throw new InvalidOperationException($"The in/nin list on '{field.Name}' exceeds {_maxInValues} values.");
        }

        var hasNull = items.Any(item => item is null);
        var names = items.Where(item => item is not null).Distinct().Select(item => parameters.Add(Input(field, item))).ToList();
        var list = names.Count == 0 ? null : $"{column} IN ({string.Join(", ", names)})";

        if (!negate)
        {
            return (list, hasNull) switch
            {
                (null, false) => "1 = 0",
                (null, true) => $"{column} IS NULL",
                (_, true) => $"({list} OR {column} IS NULL)",
                _ => list,
            };
        }

        return (list, hasNull) switch
        {
            (null, false) => "1 = 1",
            (null, true) => $"{column} IS NOT NULL",
            (_, true) => $"(NOT ({list}) AND {column} IS NOT NULL)",
            _ => $"(NOT ({list}) OR {column} IS NULL)",
        };
    }

    private string Like(string column, string pattern, ParameterBag parameters, bool escaped)
    {
        var like = _dialect.CaseInsensitiveLike(column, parameters.Add(pattern));

        return escaped ? like + SqlLikePatterns.EscapeClause : like;
    }

    private static string Negate(string column, string predicate) => $"({column} IS NULL OR NOT ({predicate}))";

    private string Escape(object? value) => _dialect.EscapeLikePattern(Text(value));

    private static string Text(object? value) =>
        value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException("Pattern operators need a value.");

    private static object Required(object? value, ConditionFilter condition) =>
        value ?? throw new InvalidOperationException($"Operator {condition.Operator} on '{condition.Field}' needs a value.");

    private static object? Input(FieldModel field, object? value) => FieldValues.FromInput(field.Kind, value);

    private string OrderBy(EntitySetModel set, IReadOnlyList<SortTerm> sort, char alias)
    {
        var terms = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var term in sort)
        {
            var field = RequireField(set, term.Field);

            if (!SchemaExposurePolicy.IsSortable(field))
            {
                throw new InvalidOperationException($"'{set.QualifiedName}.{field.Name}' cannot be sorted ({field.Kind}).");
            }

            if (used.Add(field.Name))
            {
                terms.Add($"{Column(alias, field.Name)} {(term.Direction == SortDirection.Descending ? "DESC" : "ASC")}");
            }
        }

        var tieBreakers = set.KeyFields.Count > 0
            ? set.KeyFields
            : set.Fields.Where(SchemaExposurePolicy.IsSortable).Take(1).Select(field => field.Name).ToList();

        terms.AddRange(tieBreakers.Where(used.Add).Select(name => $"{Column(alias, RequireField(set, name).Name)} ASC"));

        if (terms.Count == 0 && _dialect.ProviderType == DatabaseType.SqlServer)
        {
            terms.Add("(SELECT NULL)");
        }

        return string.Join(", ", terms);
    }

    private static FieldModel RequireField(EntitySetModel set, string name)
    {
        var field = set.FindField(name)
            ?? throw new InvalidOperationException($"'{name}' is not a column of '{set.QualifiedName}'.");

        if (!IsSafeIdentifier(field.Name) || !SchemaExposurePolicy.IsScalar(field))
        {
            throw new InvalidOperationException($"'{set.QualifiedName}.{field.Name}' cannot be queried.");
        }

        return field;
    }

    private string Columns(EntitySetModel set, char alias)
    {
        var columns = set.Fields.Where(SchemaExposurePolicy.IsScalar).Select(field => Column(alias, RequireField(set, field.Name).Name)).ToList();

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"'{set.QualifiedName}' has no columns to select.");
        }

        return string.Join(", ", columns);
    }

    private string Table(EntitySetModel set)
    {
        if (!IsSafeIdentifier(set.Name) || (set.Schema is not null && !IsSafeIdentifier(set.Schema)))
        {
            throw new InvalidOperationException($"'{set.QualifiedName}' cannot be queried.");
        }

        return set.Schema is null ? Quote(set.Name) : $"{Quote(set.Schema)}.{Quote(set.Name)}";
    }

    private string Column(char alias, string name) => $"{alias}.{Quote(name)}";

    private string Quote(string identifier) => _dialect.QuoteIdentifier(identifier);

    private sealed class ParameterBag(ISqlDialect dialect)
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, object?> Values => _values;

        public string Add(object? value)
        {
            var name = string.Create(CultureInfo.InvariantCulture, $"p{_values.Count}");

            _values[name] = value;

            return dialect.Parameter(name);
        }
    }
}
