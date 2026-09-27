using System.Data.Common;
using System.Globalization;
using Dapper;

namespace Data.Sql.DatabaseAccess;

internal static class SqlRowReader
{
    public static Func<DbDataReader, T> For<T>(DbDataReader reader)
    {
        if (!IsScalar(typeof(T)))
        {
            var parser = reader.GetRowParser<T>();

            return row => parser(row);
        }

        return row => Convert<T>(row.IsDBNull(0) ? null : row.GetValue(0));
    }

    private static bool IsScalar(Type type)
    {
        var effective = Nullable.GetUnderlyingType(type) ?? type;

        return effective.IsPrimitive
            || effective.IsEnum
            || effective == typeof(string)
            || effective == typeof(decimal)
            || effective == typeof(Guid)
            || effective == typeof(DateTime)
            || effective == typeof(DateTimeOffset)
            || effective == typeof(DateOnly)
            || effective == typeof(TimeOnly)
            || effective == typeof(TimeSpan)
            || effective == typeof(byte[]);
    }

    private static T Convert<T>(object? value)
    {
        if (value is null)
        {
            return default!;
        }

        if (value is T typed)
        {
            return typed;
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        object converted = target switch
        {
            _ when target == typeof(Guid) => value is byte[] bytes
                ? new Guid(bytes)
                : Guid.Parse(AsString(value), CultureInfo.InvariantCulture),
            _ when target == typeof(DateTime) => value is string text
                ? DateTime.Parse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                : System.Convert.ToDateTime(value, CultureInfo.InvariantCulture),
            _ when target == typeof(DateOnly) => DateOnly.FromDateTime(
                System.Convert.ToDateTime(value, CultureInfo.InvariantCulture)),
            _ when target == typeof(TimeOnly) => value is TimeSpan span
                ? TimeOnly.FromTimeSpan(span)
                : TimeOnly.Parse(AsString(value), CultureInfo.InvariantCulture),
            _ when target == typeof(TimeSpan) => TimeSpan.Parse(AsString(value), CultureInfo.InvariantCulture),
            _ when target == typeof(DateTimeOffset) => DateTimeOffset.Parse(
                AsString(value),
                CultureInfo.InvariantCulture),
            _ when target.IsEnum => Enum.Parse(target, AsString(value), ignoreCase: true),
            _ => System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture),
        };

        return (T)converted;
    }

    private static string AsString(object value) =>
        System.Convert.ToString(value, CultureInfo.InvariantCulture)
        ?? throw new InvalidCastException($"Cannot read a scalar from '{value.GetType()}'.");
}
