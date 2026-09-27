namespace Domain.Interfaces.Platform;

public interface IEnvironmentAccessor
{
    string? GetVariable(string name);

    string GetRequiredVariable(string name);

    bool TryGetVariable(string name, out string value);

    IReadOnlyDictionary<string, string> GetVariables();

    string ExpandVariables(string value);

    string[] GetCommandLineArguments();
}
