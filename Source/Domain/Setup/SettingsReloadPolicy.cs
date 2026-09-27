using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Domain.Setup;

public static partial class SettingsReloadPolicy
{
    public static readonly IReadOnlyList<string> LivePaths =
    [
        "Settings:Apis[*]:AuthorizationType",
        "Settings:Apis[*]:AuthorizationValue",
        "Settings:Apis[*]:TimeoutSeconds",
        "Settings:Apis[*]:ApiKeyHeaderName",
        "Settings:Apis[*]:Endpoints",
        "Settings:Caches[*]:DefaultTtlMinutes",
        "Settings:Databases[*]:Sql:Outbox:PollIntervalSeconds",
        "Settings:Databases[*]:Sql:Outbox:BatchSize",
        "Settings:Databases[*]:Sql:Outbox:BrokerId",
        "Settings:Scheduling:Jobs",
    ];

    private static readonly JsonSerializerOptions _json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<string> RestartRequiredChanges<T>(string sectionName, T applied, T current)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(current);

        var changes = new List<string>();

        Compare(
            sectionName,
            JsonSerializer.SerializeToNode(applied, _json),
            JsonSerializer.SerializeToNode(current, _json),
            changes);

        return changes.Where(path => !IsLive(path)).Distinct(StringComparer.Ordinal).ToList();
    }

    public static bool IsLive(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var normalized = Index().Replace(path, "[*]");

        return LivePaths.Any(live =>
            normalized.Equals(live, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(live + ":", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(live + "[", StringComparison.OrdinalIgnoreCase));
    }

    private static void Compare(string path, JsonNode? applied, JsonNode? current, List<string> changes)
    {
        switch (applied, current)
        {
            case (JsonObject left, JsonObject right):
                foreach (var key in left.Select(pair => pair.Key).Union(right.Select(pair => pair.Key)))
                {
                    Compare($"{path}:{key}", left[key], right[key], changes);
                }

                break;

            case (JsonArray left, JsonArray right) when left.Count == right.Count:
                for (var index = 0; index < left.Count; index++)
                {
                    Compare($"{path}[{index}]", left[index], right[index], changes);
                }

                break;

            default:
                if (!JsonNode.DeepEquals(applied, current))
                {
                    changes.Add(path);
                }

                break;
        }
    }

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex Index();
}
