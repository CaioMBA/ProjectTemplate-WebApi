using CrossCutting.Setup;
using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using Microsoft.Extensions.Configuration;

namespace UnitTests.TestSupport;

public sealed class FakeEnvironmentAccessor(IReadOnlyDictionary<string, string>? variables = null) : IEnvironmentAccessor
{
    private readonly IReadOnlyDictionary<string, string> _variables = variables ?? new Dictionary<string, string>();

    public string? GetVariable(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    public string GetRequiredVariable(string name) =>
        GetVariable(name) ?? throw new InvalidOperationException($"{name} is not set.");

    public bool TryGetVariable(string name, out string value)
    {
        value = GetVariable(name) ?? string.Empty;

        return _variables.ContainsKey(name);
    }

    public IReadOnlyDictionary<string, string> GetVariables() => _variables;

    public string ExpandVariables(string value) => value;

    public string[] GetCommandLineArguments() => [];
}

public static class TestStartup
{
    public const string EnvironmentName = "Testing";

    public static StartupSettings From(IConfiguration configuration, IEnvironmentAccessor? environment = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetStartupSettings(
            EnvironmentName,
            environment ?? new FakeEnvironmentAccessor(),
            configuration.CreateSecretResolver());
    }
}