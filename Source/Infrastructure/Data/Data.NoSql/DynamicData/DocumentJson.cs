using System.Text.Json;

namespace Data.NoSql.DynamicData;

public static class DocumentJson
{
    public static object? Plain(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(property => property.Name, property => Plain(property.Value), StringComparer.Ordinal),
        JsonValueKind.Array => element.EnumerateArray().Select(Plain).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public static IReadOnlyDictionary<string, object?> Document(JsonElement element, Func<string, bool>? include = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return element.EnumerateObject()
            .Where(property => include?.Invoke(property.Name) != false)
            .ToDictionary(property => property.Name, property => Plain(property.Value), StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, object?> Document(string json, Func<string, bool>? include = null)
    {
        using var document = JsonDocument.Parse(json);

        return Document(document.RootElement, include);
    }

    public static bool IsSafeName(string name) =>
        !string.IsNullOrEmpty(name)
        && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
