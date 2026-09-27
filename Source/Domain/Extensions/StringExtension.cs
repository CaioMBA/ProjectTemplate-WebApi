using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Domain.Extensions;

public static class StringExtension
{
    public static string Capitalize(this string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        value.CopyTo(buffer);
        buffer[0] = char.ToUpperInvariant(buffer[0]);

        return new string(buffer);
    }

    public static int ToInteger(this string? value, int fallback = 0) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public static long ToLong(this string? value, long fallback = 0) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public static decimal ToDecimal(this string? value, decimal fallback = 0) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public static string ToSha256(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexStringLower(hash);
    }

    public static string ToSha512(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var hash = SHA512.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexStringLower(hash);
    }

    public static string ToBase64(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public static string FromBase64(this string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    public static bool IsBase64String(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length % 4 != 0)
        {
            return false;
        }

        var buffer = new byte[(value.Length * 3 / 4) + 3];

        return Convert.TryFromBase64String(value, buffer, out _);
    }

    public static T? ToObject<T>(this string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? default
            : JsonSerializer.Deserialize<T>(value, JsonDefaults.Standard);

    public static string Truncate(this string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return maxLength <= 1 ? value[..maxLength] : string.Concat(value.AsSpan(0, maxLength - 1), "\u2026");
    }

    public static string? NullIfBlank(this string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
