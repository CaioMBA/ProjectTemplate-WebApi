using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using Domain.Extensions;
using NCrontab;

namespace Data.Sql.DapperHandlers;

public sealed class JsonObjectTypeHandler : SqlMapper.TypeHandler<JsonObject?>
{
    public override JsonObject? Parse(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text)
            ? JsonNode.Parse(text)?.AsObject()
            : null;

    public override void SetValue(IDbDataParameter parameter, JsonObject? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value?.ToJsonString(JsonDefaults.Standard) ?? (object)DBNull.Value;
        parameter.DbType = DbType.String;
    }
}

public sealed class JsonArrayTypeHandler : SqlMapper.TypeHandler<JsonArray?>
{
    public override JsonArray? Parse(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text)
            ? JsonNode.Parse(text)?.AsArray()
            : null;

    public override void SetValue(IDbDataParameter parameter, JsonArray? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value?.ToJsonString(JsonDefaults.Standard) ?? (object)DBNull.Value;
        parameter.DbType = DbType.String;
    }
}

public sealed class JsonNodeTypeHandler : SqlMapper.TypeHandler<JsonNode?>
{
    public override JsonNode? Parse(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text) ? JsonNode.Parse(text) : null;

    public override void SetValue(IDbDataParameter parameter, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value?.ToJsonString(JsonDefaults.Standard) ?? (object)DBNull.Value;
        parameter.DbType = DbType.String;
    }
}

public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override DateOnly Parse(object value) =>
        value switch
        {
            DateOnly dateOnly => dateOnly,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            string text => DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new DataException($"Cannot convert '{value?.GetType().Name}' to DateOnly."),
        };

    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        parameter.DbType = DbType.Date;
    }
}

public sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override TimeOnly Parse(object value) =>
        value switch
        {
            TimeOnly timeOnly => timeOnly,
            TimeSpan timeSpan => TimeOnly.FromTimeSpan(timeSpan),
            DateTime dateTime => TimeOnly.FromDateTime(dateTime),
            string text => TimeOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new DataException($"Cannot convert '{value?.GetType().Name}' to TimeOnly."),
        };

    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value.ToTimeSpan();
        parameter.DbType = DbType.Time;
    }
}

public sealed class CrontabScheduleTypeHandler : SqlMapper.TypeHandler<CrontabSchedule?>
{
    public override CrontabSchedule? Parse(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text) ? CrontabSchedule.Parse(text) : null;

    public override void SetValue(IDbDataParameter parameter, CrontabSchedule? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value?.ToString() ?? (object)DBNull.Value;
        parameter.DbType = DbType.String;
    }
}

public sealed class DictionaryTypeHandler : SqlMapper.TypeHandler<Dictionary<string, object?>?>
{
    public override Dictionary<string, object?>? Parse(object? value) =>
        value is string text && !string.IsNullOrWhiteSpace(text)
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(text, JsonDefaults.Standard)
            : null;

    public override void SetValue(IDbDataParameter parameter, Dictionary<string, object?>? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value is null
            ? DBNull.Value
            : JsonSerializer.Serialize(value, JsonDefaults.Standard);

        parameter.DbType = DbType.String;
    }
}
