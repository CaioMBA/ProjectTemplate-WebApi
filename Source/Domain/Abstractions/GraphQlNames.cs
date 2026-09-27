using System.Globalization;
using System.Text;

namespace Domain.Abstractions;

public static class GraphQlNames
{
    private const string FallbackName = "field";

    public static IReadOnlyList<string> Words(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var words = new List<string>();
        var current = new StringBuilder();

        for (var index = 0; index < identifier.Length; index++)
        {
            var character = identifier[index];

            if (!char.IsAsciiLetterOrDigit(character))
            {
                Flush(words, current);

                continue;
            }

            if (current.Length > 0 && StartsNewWord(identifier, index))
            {
                Flush(words, current);
            }

            current.Append(character);
        }

        Flush(words, current);

        return words;
    }

    public static string Camel(string identifier)
    {
        var words = Words(identifier);

        if (words.Count == 0)
        {
            return FallbackName;
        }

        var builder = new StringBuilder(words[0].ToLowerInvariant());

        foreach (var word in words.Skip(1))
        {
            builder.Append(Capitalize(word));
        }

        return Guard(builder.ToString());
    }

    public static string Pascal(string identifier)
    {
        var words = Words(identifier);

        if (words.Count == 0)
        {
            return Capitalize(FallbackName);
        }

        return Guard(string.Concat(words.Select(Capitalize)));
    }

    public static string Capitalize(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        if (word.Length == 0)
        {
            return word;
        }

        return string.Concat(
            word[..1].ToUpperInvariant(),
            word[1..].ToLowerInvariant());
    }

    public static bool IsValid(string name) =>
        !string.IsNullOrEmpty(name)
        && !name.StartsWith("__", StringComparison.Ordinal)
        && (char.IsAsciiLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool StartsNewWord(string identifier, int index)
    {
        var previous = identifier[index - 1];
        var character = identifier[index];

        if (char.IsAsciiLetterLower(previous) && char.IsAsciiLetterUpper(character))
        {
            return true;
        }

        return char.IsAsciiLetterUpper(previous)
               && char.IsAsciiLetterUpper(character)
               && index + 1 < identifier.Length
               && char.IsAsciiLetterLower(identifier[index + 1]);
    }

    private static void Flush(List<string> words, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        words.Add(current.ToString());
        current.Clear();
    }

    private static string Guard(string name) =>
        char.IsAsciiDigit(name[0])
            ? string.Create(CultureInfo.InvariantCulture, $"_{name}")
            : name;
}

public sealed class GraphQlNameScope
{
    private readonly HashSet<string> _claimed;

    public GraphQlNameScope(IEnumerable<string>? reserved = null)
    {
        _claimed = new HashSet<string>(reserved ?? [], StringComparer.Ordinal);
    }

    public bool IsClaimed(string name) => _claimed.Contains(name);

    public string Claim(string desired, IReadOnlyList<string>? derivedSuffixes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desired);

        var suffixes = derivedSuffixes ?? [];
        var attempt = 1;
        var candidate = desired;

        while (_claimed.Contains(candidate) || suffixes.Any(suffix => _claimed.Contains(candidate + suffix)))
        {
            attempt++;
            candidate = string.Create(CultureInfo.InvariantCulture, $"{desired}{attempt}");
        }

        _claimed.Add(candidate);

        foreach (var suffix in suffixes)
        {
            _claimed.Add(candidate + suffix);
        }

        return candidate;
    }
}
