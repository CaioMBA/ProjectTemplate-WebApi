using System.Collections;
using System.Text.Json;
using Domain.Abstractions;
using Domain.Enums;
using Domain.Models.DynamicData;

namespace Data.NoSql.DynamicData;

public static class DocumentSchemaInference
{
    public const string MixedType = "mixed";

    public static IReadOnlyList<FieldModel> Infer(
        IEnumerable<IReadOnlyDictionary<string, object?>> documents,
        string? keyField = null)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var root = new Accumulator();
        var count = 0;

        foreach (var document in documents)
        {
            root.MergeDocument(document);
            count++;
        }

        var fields = root.ToFields(count);

        if (keyField is null)
        {
            return fields;
        }

        var key = fields.FirstOrDefault(field => field.Name == keyField);

        return key is null ? fields : [key with { IsNullable = false }, .. fields.Where(field => field.Name != keyField)];
    }

    public static FieldKind KindOf(object value) => value switch
    {
        string => FieldKind.Text,
        bool => FieldKind.Flag,
        byte or sbyte or short or ushort or int or uint or long => FieldKind.Integer64,
        ulong or decimal => FieldKind.Fixed,
        float or double => FieldKind.Floating,
        DateTime or DateTimeOffset => FieldKind.TimestampOffset,
        DateOnly => FieldKind.Date,
        TimeOnly or TimeSpan => FieldKind.Time,
        Guid => FieldKind.Uuid,
        byte[] => FieldKind.Binary,
        JsonElement => FieldKind.Json,
        IReadOnlyDictionary<string, object?> or IDictionary => FieldKind.Document,
        IEnumerable => FieldKind.Array,
        _ => FieldKind.Unknown,
    };

    public static FieldKind Combine(FieldKind left, FieldKind right)
    {
        if (left == right)
        {
            return left;
        }

        var numbers = new[] { left, right };

        if (numbers.All(kind => kind is FieldKind.Integer64 or FieldKind.Fixed or FieldKind.Floating))
        {
            return numbers.Contains(FieldKind.Floating) ? FieldKind.Floating : FieldKind.Fixed;
        }

        return FieldKind.Json;
    }

    public static IReadOnlyDictionary<string, object?> Project(
        IReadOnlyList<FieldModel> fields,
        IReadOnlyDictionary<string, object?> document)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(document);

        var row = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            var value = document.GetValueOrDefault(field.Name);

            if (field.Kind != FieldKind.Document)
            {
                row[field.Name] = Canonical(field.Kind, value);

                continue;
            }

            row[field.Name] = value is IReadOnlyDictionary<string, object?> nested ? Project(field.Children, nested) : null;
        }

        return row;
    }

    public static object? Canonical(FieldKind kind, object? value)
    {
        try
        {
            return FieldValues.ToCanonical(kind, value);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    private sealed class Accumulator
    {
        private readonly Dictionary<string, Accumulator> _children = new(StringComparer.Ordinal);

        private readonly List<string> _order = [];

        private FieldKind? _kind;

        private bool _nullable;

        private int _seen;

        private int _documents;

        public void MergeDocument(IReadOnlyDictionary<string, object?> document)
        {
            _documents++;

            foreach (var (name, value) in document)
            {
                if (!_children.TryGetValue(name, out var child))
                {
                    child = new Accumulator();
                    _children[name] = child;
                    _order.Add(name);
                }

                child.Merge(value);
            }
        }

        public List<FieldModel> ToFields(int documents) =>
            _order
                .Select(name => _children[name].ToField(name, documents))
                .Where(field => field.Kind != FieldKind.Unknown || field.NativeType == "null")
                .ToList();

        private void Merge(object? value)
        {
            _seen++;

            if (value is null)
            {
                _nullable = true;

                return;
            }

            var kind = KindOf(value);

            _kind = _kind is null ? kind : Combine(_kind.Value, kind);

            if (kind == FieldKind.Document && value is IReadOnlyDictionary<string, object?> nested)
            {
                MergeDocument(nested);
            }
        }

        private FieldModel ToField(string name, int documents)
        {
            var nullable = _nullable || _seen < documents;

            if (_kind is null)
            {
                return new FieldModel(name, FieldKind.Unknown, true, "null");
            }

            var kind = _kind.Value;

            if (kind == FieldKind.Document)
            {
                return new FieldModel(name, FieldKind.Document, nullable, "object")
                {
                    Children = ToFields(_documents),
                };
            }

            return new FieldModel(name, kind, nullable, kind == FieldKind.Json ? MixedType : kind.ToString());
        }
    }
}
