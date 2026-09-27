using System.Text;
using System.Text.Json;

namespace Domain.Extensions;

public static class ObjectExtension
{
    public static string ToJson<T>(this T value) =>
        JsonSerializer.Serialize(value, RuntimeTypeOf(value), JsonDefaults.Standard);

    public static string ToJsonIndented<T>(this T value) =>
        JsonSerializer.Serialize(value, RuntimeTypeOf(value), JsonDefaults.Indented);

    public static byte[] ToJsonBytes<T>(this T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, RuntimeTypeOf(value), JsonDefaults.Standard);

    private static Type RuntimeTypeOf<T>(T value) => value?.GetType() ?? typeof(T);

    public static T? FromJsonBytes<T>(this byte[]? bytes) =>
        bytes is null || bytes.Length == 0
            ? default
            : JsonSerializer.Deserialize<T>(bytes, JsonDefaults.Standard);

    public static string ToQueryString<T>(this T value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder();

        foreach (var property in typeof(T).GetProperties())
        {
            if (!property.CanRead)
            {
                continue;
            }

            var propertyValue = property.GetValue(value);

            if (propertyValue is null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder
                .Append(Uri.EscapeDataString(property.Name))
                .Append('=')
                .Append(Uri.EscapeDataString(
                    Convert.ToString(propertyValue, System.Globalization.CultureInfo.InvariantCulture)
                    ?? string.Empty));
        }

        return builder.ToString();
    }

    public static T? DeepClone<T>(this T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Standard), JsonDefaults.Standard);
}
