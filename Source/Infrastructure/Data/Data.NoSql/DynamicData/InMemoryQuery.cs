using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Models.DynamicData;

namespace Data.NoSql.DynamicData;

public static class InMemoryQuery
{
    private static readonly TimeSpan _regexTimeout = TimeSpan.FromSeconds(1);

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Apply(
        EntitySetModel set,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        FilterNode? filter,
        IReadOnlyList<SortTerm> sort,
        int skip,
        int take)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(sort);

        var matching = Where(set, rows, filter);

        return Order(set, matching, sort).Skip(skip).Take(take).ToList();
    }

    public static IEnumerable<IReadOnlyDictionary<string, object?>> Where(
        EntitySetModel set,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        FilterNode? filter)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(rows);

        return filter is null ? rows : rows.Where(row => Matches(set, filter, row));
    }

    public static IReadOnlyDictionary<string, object?> Aggregate(
        EntitySetModel set,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        AggregateFunction function,
        IReadOnlyList<string> fields)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(fields);

        var materialized = rows.ToList();
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var path in fields)
        {
            var field = RequireField(set, path);
            var values = materialized.Select(row => Canonical(field, Resolve(row, path))).OfType<object>().ToList();

            result[path] = values.Count == 0 ? null : function switch
            {
                AggregateFunction.Min => values.Min(Comparer<object>.Create(Compare)),
                AggregateFunction.Max => values.Max(Comparer<object>.Create(Compare)),
                AggregateFunction.Sum => Sum(field.Kind, values),
                _ => values.Average(value => Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            };
        }

        return result;
    }

    public static bool Matches(EntitySetModel set, FilterNode filter, IReadOnlyDictionary<string, object?> row)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(row);

        return filter switch
        {
            AndFilter and => and.Nodes.All(node => Matches(set, node, row)),
            OrFilter or => or.Nodes.Any(node => Matches(set, node, row)),
            NotFilter not => !Matches(set, not.Node, row),
            ConditionFilter condition => Condition(set, condition, row),
            _ => throw new NotSupportedException($"Filter node {filter.GetType().Name} is not supported."),
        };
    }

    public static object? Resolve(IReadOnlyDictionary<string, object?> row, string path)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(path);

        object? current = row;

        foreach (var segment in path.Split('.'))
        {
            current = current is IReadOnlyDictionary<string, object?> map ? map.GetValueOrDefault(segment) : null;
        }

        return current;
    }

    public static Regex LikeToRegex(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var builder = new StringBuilder("^");

        foreach (var character in pattern)
        {
            builder.Append(character switch
            {
                '%' => ".*",
                '_' => ".",
                _ => Regex.Escape(character.ToString()),
            });
        }

        return new Regex(builder.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, _regexTimeout);
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> Order(
        EntitySetModel set,
        IEnumerable<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<SortTerm> sort)
    {
        var terms = sort.Count > 0 ? sort : set.KeyFields.Select(key => new SortTerm(key, SortDirection.Ascending)).ToList();

        if (terms.Count == 0)
        {
            return rows;
        }

        var comparer = Comparer<object?>.Create(CompareNullable);
        IOrderedEnumerable<IReadOnlyDictionary<string, object?>>? ordered = null;

        foreach (var term in terms)
        {
            var field = RequireField(set, term.Field);

            object? Key(IReadOnlyDictionary<string, object?> row) => Canonical(field, Resolve(row, term.Field));

            var descending = term.Direction == SortDirection.Descending;

            if (ordered is null)
            {
                ordered = descending ? rows.OrderByDescending(Key, comparer) : rows.OrderBy(Key, comparer);

                continue;
            }

            ordered = descending ? ordered.ThenByDescending(Key, comparer) : ordered.ThenBy(Key, comparer);
        }

        return ordered!;
    }

    private static bool Condition(EntitySetModel set, ConditionFilter condition, IReadOnlyDictionary<string, object?> row)
    {
        var field = RequireField(set, condition.Field);

        if (!SchemaExposurePolicy.OperatorsFor(field).Contains(condition.Operator))
        {
            throw new InvalidOperationException($"Operator {condition.Operator} is not supported on '{condition.Field}'.");
        }

        var value = Canonical(field, Resolve(row, condition.Field));
        var operand = condition.Value;

        return condition.Operator switch
        {
            FilterOperator.Eq => Equal(field, value, operand),
            FilterOperator.Neq => !Equal(field, value, operand),
            FilterOperator.In => List(operand).Any(item => Equal(field, value, item)),
            FilterOperator.NotIn => !List(operand).Any(item => Equal(field, value, item)),
            FilterOperator.Contains => Text(value)?.Contains(Text(operand)!, StringComparison.OrdinalIgnoreCase) == true,
            FilterOperator.NotContains => Text(value)?.Contains(Text(operand)!, StringComparison.OrdinalIgnoreCase) != true,
            FilterOperator.StartsWith => Text(value)?.StartsWith(Text(operand)!, StringComparison.OrdinalIgnoreCase) == true,
            FilterOperator.NotStartsWith => Text(value)?.StartsWith(Text(operand)!, StringComparison.OrdinalIgnoreCase) != true,
            FilterOperator.EndsWith => Text(value)?.EndsWith(Text(operand)!, StringComparison.OrdinalIgnoreCase) == true,
            FilterOperator.NotEndsWith => Text(value)?.EndsWith(Text(operand)!, StringComparison.OrdinalIgnoreCase) != true,
            FilterOperator.Like => Text(value) is { } text && LikeToRegex(Text(operand)!).IsMatch(text),
            FilterOperator.Gt => Ordered(field, value, operand) > 0,
            FilterOperator.Gte => Ordered(field, value, operand) >= 0,
            FilterOperator.Lt => value is not null && Ordered(field, value, operand) < 0,
            FilterOperator.Lte => value is not null && Ordered(field, value, operand) <= 0,
            FilterOperator.IsNull => operand is false ? value is not null : value is null,
            _ => throw new NotSupportedException($"Operator {condition.Operator} is not supported."),
        };
    }

    private static bool Equal(FieldModel field, object? value, object? operand)
    {
        if (value is null || operand is null)
        {
            return value is null && operand is null;
        }

        return Compare(value, Canonical(field, operand)!) == 0;
    }

    private static int Ordered(FieldModel field, object? value, object? operand)
    {
        if (value is null || operand is null)
        {
            return int.MinValue;
        }

        return Compare(value, Canonical(field, operand)!);
    }

    private static int CompareNullable(object? left, object? right) => (left, right) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        _ => Compare(left, right),
    };

    private static int Compare(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return CompareNullable(left, right);
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture)
                .CompareTo(Convert.ToDecimal(right, CultureInfo.InvariantCulture));
        }

        if (left is string leftText && right is string rightText)
        {
            return string.CompareOrdinal(leftText, rightText);
        }

        return left.GetType() == right.GetType() && left is IComparable comparable
            ? comparable.CompareTo(right)
            : string.CompareOrdinal(
                Convert.ToString(left, CultureInfo.InvariantCulture),
                Convert.ToString(right, CultureInfo.InvariantCulture));
    }

    private static bool IsNumber(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double;

    private static object Sum(FieldKind kind, List<object> values) => SchemaExposurePolicy.SumKind(kind) switch
    {
        FieldKind.Integer64 => values.Sum(value => Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        FieldKind.Fixed => values.Sum(value => Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
        _ => values.Sum(value => Convert.ToDouble(value, CultureInfo.InvariantCulture)),
    };

    private static object? Canonical(FieldModel field, object? value) => DocumentSchemaInference.Canonical(field.Kind, value);

    private static string? Text(object? value) => value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);

    private static IEnumerable<object?> List(object? operand) =>
        operand is IEnumerable sequence and not string ? sequence.Cast<object?>() : [operand];

    private static FieldModel RequireField(EntitySetModel set, string path) =>
        set.FindPath(path) ?? throw new InvalidOperationException($"'{path}' is not a field of '{set.QualifiedName}'.");
}
