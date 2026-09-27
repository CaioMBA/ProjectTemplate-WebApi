using System.Text.Json.Nodes;
using Domain.Extensions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Data.Sql.EntityFrameworkContexts.Converters;

public sealed class JsonObjectConverter : ValueConverter<JsonObject, string>
{
    public JsonObjectConverter()
        : base(
            node => node.ToJsonString(JsonDefaults.Standard),
            text => JsonNode.Parse(text)!.AsObject())
    {
    }
}

public sealed class JsonArrayConverter : ValueConverter<JsonArray, string>
{
    public JsonArrayConverter()
        : base(
            node => node.ToJsonString(JsonDefaults.Standard),
            text => JsonNode.Parse(text)!.AsArray())
    {
    }
}

public sealed class JsonNodeConverter : ValueConverter<JsonNode, string>
{
    public JsonNodeConverter()
        : base(
            node => node.ToJsonString(JsonDefaults.Standard),
            text => JsonNode.Parse(text)!)
    {
    }
}

