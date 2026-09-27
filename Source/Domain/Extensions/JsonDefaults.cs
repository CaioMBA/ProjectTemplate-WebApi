using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Results;

namespace Domain.Extensions;

public static class JsonDefaults
{
    public static JsonSerializerOptions Standard { get; } = Create();

    public static JsonSerializerOptions Indented { get; } = Create(writeIndented: true);

    private static JsonSerializerOptions Create(bool writeIndented = false)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = writeIndented,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ResultJsonConverter());

        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }
}
