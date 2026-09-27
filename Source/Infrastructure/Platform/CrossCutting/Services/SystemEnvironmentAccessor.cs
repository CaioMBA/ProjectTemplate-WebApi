using System.Collections;
using Domain.Interfaces.Platform;

namespace CrossCutting.Services;

public sealed class SystemEnvironmentAccessor : IEnvironmentAccessor
{
    public string? GetVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public string GetRequiredVariable(string name) =>
        GetVariable(name)
        ?? throw new InvalidOperationException(
            $"Environment variable '{name}' is not set or is empty.");

    public bool TryGetVariable(string name, out string value)
    {
        var resolved = GetVariable(name);

        value = resolved ?? string.Empty;

        return resolved is not null;
    }

    public IReadOnlyDictionary<string, string> GetVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                variables[key] = value;
            }
        }

        return variables;
    }

    public string ExpandVariables(string value) => Environment.ExpandEnvironmentVariables(value);

    public string[] GetCommandLineArguments() => Environment.GetCommandLineArgs();
}
