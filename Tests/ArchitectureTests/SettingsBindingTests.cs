using System.Text.RegularExpressions;

namespace ArchitectureTests;

public sealed partial class SettingsBindingTests
{
    [Fact]
    public void OnlyTheConfigurationSetupBindsSettingsOutsideTheOptionsPipeline()
    {
        var root = RepositoryRoot();
        var source = Path.Combine(root, "Source");
        var allowed = Path.Combine(source, "Infrastructure", "Platform", "CrossCutting", "Setup", "AddConfigurationSetup.cs");

        var offenders = Directory
            .EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !string.Equals(file, allowed, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => SectionBinding().IsMatch(entry.line))
                .Select(entry => $"{Path.GetRelativePath(root, file)}:{entry.number}"))
            .ToList();

        offenders.ShouldBeEmpty(
            "settings are bound once: StartupSettings (ConfigurationSetup.GetStartupSettings) for registration, "
            + "IOptions<T> for runtime. A second .Get<T>() copy skips secrets, environment overrides and "
            + "validation. Found: " + string.Join(", ", offenders));
    }

    [GeneratedRegex(@"\.Get<\w+(Settings|Options)>\(|GetSection\([^)]*\)\s*\.Get<")]
    private static partial Regex SectionBinding();

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
