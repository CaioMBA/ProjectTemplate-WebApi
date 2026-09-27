using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;

namespace Data.Sql.DatabaseAccess;

public sealed partial class SqlDebugFormatter(ISqlDialect dialect, SqlDatabaseOptions options)
{
    [GeneratedRegex(@"(?<!\w)(?<prefix>[@:])(?<name>\w+)(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterToken();

    public bool InterpolationEnabled => options.LogInterpolatedSql;

    public string Describe(string sql, object? parameters)
    {
        if (!options.LogInterpolatedSql)
        {
            return sql;
        }

        var values = Extract(parameters);

        return values.Count == 0
            ? sql
            : ParameterToken().Replace(sql, match =>
            {
                var name = match.Groups["name"].Value;

                return values.TryGetValue(name, out var value)
                    ? Render(value)
                    : match.Value;
            });
    }

    private static Dictionary<string, object?> Extract(object? parameters)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        switch (parameters)
        {
            case null:
                break;

            case DynamicParameters dynamicParameters:
                foreach (var name in dynamicParameters.ParameterNames)
                {
                    values[name] = dynamicParameters.Get<object?>(name);
                }

                break;

            case IEnumerable<KeyValuePair<string, object?>> pairs:
                foreach (var pair in pairs)
                {
                    values[pair.Key] = pair.Value;
                }

                break;

            default:
                foreach (var property in parameters.GetType().GetProperties())
                {
                    values[property.Name] = property.GetValue(parameters);
                }

                break;
        }

        return values;
    }

    private string Render(object? value) => value switch
    {
        null or DBNull => "NULL",
        string text => Quote(text),
        char character => Quote(character.ToString()),
        bool flag => dialect.BooleanLiteral(flag),
        Guid identifier => Quote(identifier.ToString()),
        DateOnly date => Quote(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        TimeOnly time => Quote(time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)),
        DateTime timestamp => Quote(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
        DateTimeOffset timestamp => Quote(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture)),
        byte[] bytes => $"0x{Convert.ToHexString(bytes)}",
        Enum enumeration => Convert.ToInt64(enumeration, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture),
        IEnumerable sequence => $"({string.Join(", ", sequence.Cast<object?>().Select(Render))})",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Quote(value.ToString() ?? string.Empty),
    };

    private static string Quote(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
