using System.Collections;
using System.Globalization;
using System.Text.Json;
using Domain.Enums;
using Domain.Extensions;

namespace Domain.Abstractions;

public static class FieldValues
{
    public static object? ToCanonical(FieldKind kind, object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return kind switch
        {
            FieldKind.Text or FieldKind.Unknown => value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture),
            FieldKind.Integer32 => ToInt32(value),
            FieldKind.Integer64 => ToInt64(value),
            FieldKind.Fixed => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            FieldKind.Floating => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            FieldKind.Flag => ToBoolean(value),
            FieldKind.Timestamp => ToDateTime(value),
            FieldKind.TimestampOffset => ToDateTimeOffset(value),
            FieldKind.Date => ToDate(value),
            FieldKind.Time => ToTime(value),
            FieldKind.Uuid => ToGuid(value),
            FieldKind.Json or FieldKind.Array => ToJson(value),
            FieldKind.Binary => ToBinary(value),
            FieldKind.Document => value,
            _ => value,
        };
    }

    public static object? FromInput(FieldKind kind, object? value)
    {
        if (value is null)
        {
            return null;
        }

        return kind switch
        {
            FieldKind.Timestamp => DateTime.SpecifyKind(ToDateTime(value), DateTimeKind.Unspecified),
            FieldKind.TimestampOffset => ToDateTimeOffset(value).ToUniversalTime(),
            _ => ToCanonical(kind, value),
        };
    }

    private static int ToInt32(object value) =>
        value is bool flag ? Convert.ToInt32(flag) : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static long ToInt64(object value) =>
        value is bool flag ? Convert.ToInt64(flag) : Convert.ToInt64(value, CultureInfo.InvariantCulture);

    private static bool ToBoolean(object value) => value switch
    {
        bool flag => flag,
        string text when bool.TryParse(text, out var parsed) => parsed,
        string text => text is "1" or "Y" or "y" or "T" or "t",
        char character => character is '1' or 'Y' or 'y' or 'T' or 't',
        _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture) != 0m,
    };

    private static DateTime ToDateTime(object value) => value switch
    {
        DateTime dateTime => dateTime,
        DateTimeOffset offset => offset.UtcDateTime,
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        string text => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        _ => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
    };

    private static DateTimeOffset ToDateTimeOffset(object value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime { Kind: DateTimeKind.Local } local => new DateTimeOffset(local),
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        _ => new DateTimeOffset(DateTime.SpecifyKind(Convert.ToDateTime(value, CultureInfo.InvariantCulture), DateTimeKind.Utc)),
    };

    private static DateOnly ToDate(object value) => value switch
    {
        DateOnly date => date,
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        DateTimeOffset offset => DateOnly.FromDateTime(offset.DateTime),
        string text => DateOnly.Parse(text, CultureInfo.InvariantCulture),
        _ => DateOnly.FromDateTime(Convert.ToDateTime(value, CultureInfo.InvariantCulture)),
    };

    private static TimeOnly ToTime(object value) => value switch
    {
        TimeOnly time => time,
        TimeSpan span => TimeOnly.FromTimeSpan(span),
        DateTime dateTime => TimeOnly.FromDateTime(dateTime),
        string text => TimeOnly.Parse(text, CultureInfo.InvariantCulture),
        _ => TimeOnly.FromTimeSpan(TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture)),
    };

    private static Guid ToGuid(object value) => value switch
    {
        Guid guid => guid,
        byte[] { Length: 16 } bytes => new Guid(bytes),
        string text => Guid.Parse(text, CultureInfo.InvariantCulture),
        _ => Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture),
    };

    private static string ToJson(object value) => value switch
    {
        string text => text,
        JsonElement element => element.GetRawText(),
        JsonDocument document => document.RootElement.GetRawText(),
        _ => JsonSerializer.Serialize(value, value.GetType(), JsonDefaults.Standard),
    };

    private static byte[]? ToBinary(object value) => value switch
    {
        byte[] bytes => bytes,
        Guid guid => guid.ToByteArray(),
        string text => Convert.FromBase64String(text),
        IEnumerable sequence => sequence.Cast<object>().Select(item => Convert.ToByte(item, CultureInfo.InvariantCulture)).ToArray(),
        _ => null,
    };
}
