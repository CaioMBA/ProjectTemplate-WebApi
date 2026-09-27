using System.Text;

namespace Data.Sql.DynamicData;

public static class SqlLikePatterns
{
    public const char EscapeCharacter = '!';

    public const string EscapeClause = " ESCAPE '!'";

    public static string Escape(string value, bool escapeBrackets)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length + 8);

        foreach (var character in value)
        {
            if (character is EscapeCharacter or '%' or '_' || (escapeBrackets && character == '['))
            {
                builder.Append(EscapeCharacter);
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
