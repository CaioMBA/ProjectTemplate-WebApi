using System.Globalization;
using System.Text;

namespace Data.NoSql.Providers;

public static class DocumentNaming
{
    public static string CollectionFor<TDocument>() => CollectionFor(typeof(TDocument));

    public static string CollectionFor(Type documentType)
    {
        ArgumentNullException.ThrowIfNull(documentType);

        var name = documentType.Name;

        var suffix = Array.Find(
            ["Document", "Entity", "Record"],
            candidate => name.EndsWith(candidate, StringComparison.Ordinal));

        if (suffix is not null)
        {
            name = name[..^suffix.Length];
        }

        return Pluralize(ToLowerCamel(name));
    }

    private static string ToLowerCamel(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        var builder = new StringBuilder(name.Length);

        builder.Append(char.ToLower(name[0], CultureInfo.InvariantCulture));
        builder.Append(name.AsSpan(1));

        return builder.ToString();
    }

    private static string Pluralize(string name)
    {
        if (name.EndsWith('y') && name.Length > 1 && !"aeiou".Contains(name[^2], StringComparison.Ordinal))
        {
            return string.Concat(name.AsSpan(0, name.Length - 1), "ies");
        }

        return name.EndsWith('s')
            || name.EndsWith("ch", StringComparison.Ordinal)
            || name.EndsWith('x')
                ? name + "es"
                : name + "s";
    }
}
