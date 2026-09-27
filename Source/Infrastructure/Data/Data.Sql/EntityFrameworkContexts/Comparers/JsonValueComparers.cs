using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Data.Sql.EntityFrameworkContexts.Comparers;

public static class JsonValueComparers
{
    public static ValueComparer<JsonObject?> JsonObjectNullableComparer { get; } =
        new ValueComparer<JsonObject?>(
            (left, right) => JsonNode.DeepEquals(left, right),
            node => node == null ? 0 : node.ToJsonString().GetHashCode(StringComparison.Ordinal),
            node => node == null ? null : node.DeepClone().AsObject());

    public static ValueComparer<JsonObject> JsonObjectComparer { get; } =
        new ValueComparer<JsonObject>(
            (left, right) => JsonNode.DeepEquals(left, right),
            node => node.ToJsonString().GetHashCode(StringComparison.Ordinal),
            node => node.DeepClone().AsObject());

    public static ValueComparer<JsonArray?> JsonArrayNullableComparer { get; } =
        new ValueComparer<JsonArray?>(
            (left, right) => JsonNode.DeepEquals(left, right),
            node => node == null ? 0 : node.ToJsonString().GetHashCode(StringComparison.Ordinal),
            node => node == null ? null : node.DeepClone().AsArray());

    public static ValueComparer<JsonArray> JsonArrayComparer { get; } =
        new ValueComparer<JsonArray>(
            (left, right) => JsonNode.DeepEquals(left, right),
            node => node.ToJsonString().GetHashCode(StringComparison.Ordinal),
            node => node.DeepClone().AsArray());

    public static ValueComparer<JsonNode?> JsonNodeNullableComparer { get; } =
        new ValueComparer<JsonNode?>(
            (left, right) => JsonNode.DeepEquals(left, right),
            node => node == null ? 0 : node.ToJsonString().GetHashCode(StringComparison.Ordinal),
            node => node == null ? null : node.DeepClone());

    public static ValueComparer<JsonNode> JsonNodeComparer { get; } =
        new ValueComparer<JsonNode>(
            (left, right) => JsonNode.DeepEquals(left, right),
            node => node.ToJsonString().GetHashCode(StringComparison.Ordinal),
            node => node.DeepClone());
}
