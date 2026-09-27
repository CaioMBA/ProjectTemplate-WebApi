using System.Globalization;

namespace Domain.Extensions;

public static class NumericExtension
{
    public static decimal RoundTo(this decimal value, int decimals = 2) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    public static string ToMoneyString(this decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    public static bool IsBetween<T>(this T value, T min, T max)
        where T : IComparable<T> =>
        value.CompareTo(min) >= 0 && value.CompareTo(max) <= 0;

    public static T Clamp<T>(this T value, T min, T max)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0)
        {
            return min;
        }

        return value.CompareTo(max) > 0 ? max : value;
    }

    public static string ToHumanReadableBytes(this long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];

        if (bytes == 0)
        {
            return "0 B";
        }

        var magnitude = Math.Abs(bytes);
        var unitIndex = (int)Math.Floor(Math.Log(magnitude, 1024));
        unitIndex = Math.Min(unitIndex, units.Length - 1);

        var scaled = magnitude / Math.Pow(1024, unitIndex);
        var sign = bytes < 0 ? "-" : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{scaled:0.##} {units[unitIndex]}");
    }
}
