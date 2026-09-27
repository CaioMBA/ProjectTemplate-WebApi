using System.Text.RegularExpressions;

namespace ArchitectureTests;

public sealed partial class HealthCheckPolicyTests
{
    [Fact]
    public void OnlyTheHealthCheckPolicyRegistersHealthChecks()
    {
        var root = RepositoryRoot();
        var source = Path.Combine(root, "Source");
        var policy = Path.Combine(source, "Domain", "Abstractions", "HealthCheckPolicy.cs");

        var offenders = Directory
            .EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !string.Equals(file, policy, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => DirectRegistration().IsMatch(entry.line))
                .Select(entry => $"{Path.GetRelativePath(root, file)}:{entry.number}"))
            .ToList();

        offenders.ShouldBeEmpty(
            "health checks must be registered through HealthCheckPolicy.Registration(kind, ...) so their tags "
            + "and failure status follow the dependency kind. Direct registrations: " + string.Join(", ", offenders));
    }

    [GeneratedRegex(@"new\s+HealthCheckRegistration\s*\(|\.AddCheck\s*[<(]|\.AddTypeActivatedCheck\s*<|\.AddAsyncCheck\s*\(|\.AddUrlGroup\s*\(")]
    private static partial Regex DirectRegistration();

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "ProjectTemplate-WebApi.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not locate the repository root from the test output directory.");

        return directory.FullName;
    }
}
