using System.Collections;
using System.Globalization;
using Domain.Enums;
using Domain.Models.Configuration;
using Domain.Models.DynamicData;
using HotChocolate;

namespace WebApi.GraphQL.AutoSchema;

public static class AutoSchemaArguments
{
    public const string Where = "where";

    public const string Order = "order";

    public const string Skip = "skip";

    public const string Take = "take";

    public static FilterNode? ParseFilter(
        object? value,
        IReadOnlyList<ExposedField> fields,
        GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(options);

        var filter = Parse(value, fields, options);

        if (filter is not null && filter.Depth > options.MaxFilterDepth)
        {
            throw Error($"The filter is nested {filter.Depth} levels deep; the limit is {options.MaxFilterDepth}.");
        }

        return filter;
    }

    public static IReadOnlyList<SortTerm> ParseSort(object? value, IReadOnlyList<ExposedField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (value is null)
        {
            return [];
        }

        var terms = new List<SortTerm>();

        foreach (var item in Items(value).OfType<object>())
        {
            foreach (var (name, direction) in Entries(item))
            {
                if (direction is null)
                {
                    continue;
                }

                var field = fields.FirstOrDefault(candidate => candidate.Name == name && candidate.Sortable)
                    ?? throw Error($"'{name}' cannot be used to sort.");

                terms.Add(new SortTerm(field.Path, ToDirection(direction)));
            }
        }

        return terms;
    }

    public static (int Skip, int Take) ParsePaging(object? skip, object? take, GraphQlAutoSchemaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var offset = skip is null ? 0 : Convert.ToInt32(skip, CultureInfo.InvariantCulture);
        var limit = take is null ? options.DefaultPageSize : Convert.ToInt32(take, CultureInfo.InvariantCulture);

        if (offset < 0)
        {
            throw Error("skip must not be negative.");
        }

        if (limit < 0 || limit > options.MaxPageSize)
        {
            throw Error($"take must be between 0 and {options.MaxPageSize}.");
        }

        return (offset, limit);
    }

    public static GraphQLException Error(string message) =>
        new(ErrorBuilder.New().SetMessage(message).SetCode("AUTO_SCHEMA_ARGUMENT").Build());

    private static FilterNode? Parse(object? value, IReadOnlyList<ExposedField> fields, GraphQlAutoSchemaOptions options)
    {
        if (value is null)
        {
            return null;
        }

        var nodes = new List<FilterNode>();

        foreach (var (name, entry) in Entries(value))
        {
            if (entry is null)
            {
                continue;
            }

            switch (name)
            {
                case "and":
                    nodes.Add(new AndFilter(Children(entry, fields, options)));
                    break;
                case "or":
                    nodes.Add(new OrFilter(Children(entry, fields, options)));
                    break;
                case "not":
                    if (Parse(entry, fields, options) is { } negated)
                    {
                        nodes.Add(new NotFilter(negated));
                    }

                    break;
                default:
                    var field = fields.FirstOrDefault(candidate => candidate.Name == name)
                        ?? throw Error($"'{name}' cannot be filtered.");

                    if (field.Children.Count > 0)
                    {
                        if (Parse(entry, field.Children, options) is { } nested)
                        {
                            nodes.Add(nested);
                        }

                        break;
                    }

                    nodes.AddRange(Conditions(field, entry, options));
                    break;
            }
        }

        return nodes.Count switch
        {
            0 => null,
            1 => nodes[0],
            _ => new AndFilter(nodes),
        };
    }

    private static List<FilterNode> Children(object value, IReadOnlyList<ExposedField> fields, GraphQlAutoSchemaOptions options) =>
        Items(value)
            .Select(item => Parse(item, fields, options))
            .OfType<FilterNode>()
            .ToList();

    private static IEnumerable<FilterNode> Conditions(ExposedField field, object value, GraphQlAutoSchemaOptions options)
    {
        foreach (var (name, operand) in Entries(value))
        {
            if (operand is null)
            {
                continue;
            }

            var filterOperator = AutoSchemaScalars.OperatorOf(name);

            if (filterOperator is null || !field.Operators.Contains(filterOperator.Value))
            {
                throw Error($"'{name}' is not supported on '{field.Name}'.");
            }

            if (filterOperator is FilterOperator.In or FilterOperator.NotIn)
            {
                var items = Items(operand).ToList();

                if (items.Count > options.MaxInValues)
                {
                    throw Error($"'{name}' on '{field.Name}' accepts at most {options.MaxInValues} values.");
                }

                yield return new ConditionFilter(field.Path, filterOperator.Value, items);

                continue;
            }

            yield return new ConditionFilter(field.Path, filterOperator.Value, operand);
        }
    }

    private static SortDirection ToDirection(object value) => value switch
    {
        SortDirection direction => direction,
        string text when text.Equals("DESC", StringComparison.OrdinalIgnoreCase) => SortDirection.Descending,
        string => SortDirection.Ascending,
        _ => throw Error("Sort directions are ASC or DESC."),
    };

    private static IEnumerable<KeyValuePair<string, object?>> Entries(object value) => value switch
    {
        IReadOnlyDictionary<string, object?> map => map,
        IDictionary<string, object?> map => map,
        IDictionary map => map.Keys.Cast<object>().Select(key => new KeyValuePair<string, object?>(
            Convert.ToString(key, CultureInfo.InvariantCulture)!,
            map[key])),
        _ => throw Error("Expected an input object."),
    };

    private static IEnumerable<object?> Items(object value) => value switch
    {
        string => [value],
        IEnumerable sequence when value is not IDictionary and not IReadOnlyDictionary<string, object?> => sequence.Cast<object?>(),
        _ => [value],
    };
}
