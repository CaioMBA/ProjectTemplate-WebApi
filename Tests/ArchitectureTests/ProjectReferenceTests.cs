using System.Xml.Linq;

namespace ArchitectureTests;

public sealed class ProjectReferenceTests
{
    private static readonly string[] _dataModuleProjects =
    [
        "Data.Sql",
        "Data.NoSql",
        "Data.Cache",
        "Data.Broker",
        "Data.RestApi",
        "Data.GraphqlApi",
        "Data.GrpcApi",
    ];

    private static readonly Dictionary<string, string> _infrastructureGroups = new(StringComparer.Ordinal)
    {
        ["Data.Sql"] = "Data",
        ["Data.NoSql"] = "Data",
        ["Data.Cache"] = "Data",
        ["Data.Broker"] = "Data",
        ["Data.RestApi"] = "Api",
        ["Data.GraphqlApi"] = "Api",
        ["Data.GrpcApi"] = "Api",
        ["CrossCutting"] = "Platform",
        ["Observability"] = "Platform",
        ["Scheduling"] = "Platform",
    };

    [Fact]
    public void Domain_DeclaresNoProjectReferences()
    {
        var references = ReadProjectReferences(FindProject("Source/Domain/Domain.csproj"));

        references.ShouldBeEmpty(
            $"Domain must declare no ProjectReference. Found: {string.Join(", ", references)}");
    }

    [Fact]
    public void Application_DeclaresOnlyADomainReference()
    {
        var references = ReadProjectReferences(FindProject("Source/Application/Application.csproj"));

        references.ShouldBe(["Domain"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("Data.Sql")]
    [InlineData("Data.NoSql")]
    [InlineData("Data.Cache")]
    [InlineData("Data.Broker")]
    [InlineData("Data.RestApi")]
    [InlineData("Data.GraphqlApi")]
    [InlineData("Data.GrpcApi")]
    public void DataModule_DeclaresOnlyADomainReference(string moduleName)
    {
        var references = ReadProjectReferences(FindInfrastructureProject(moduleName));

        references.ShouldBe(["Domain"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("Observability")]
    [InlineData("Scheduling")]
    public void PlatformService_DeclaresOnlyADomainReference(string projectName)
    {
        var references = ReadProjectReferences(FindInfrastructureProject(projectName));

        references.ShouldBe(["Domain"], ignoreOrder: true);
    }

    [Fact]
    public void WebApi_DoesNotReferenceADataModuleDirectly()
    {
        var references = ReadProjectReferences(FindProject("Source/WebApi/WebApi.csproj"));

        var violations = _dataModuleProjects.Intersect(references, StringComparer.Ordinal).ToArray();

        violations.ShouldBeEmpty(
            $"WebApi must compose infrastructure through CrossCutting. Found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void CrossCutting_ReferencesEveryModule()
    {
        var references = ReadProjectReferences(FindInfrastructureProject("CrossCutting"));

        foreach (var module in _dataModuleProjects)
        {
            references.ShouldContain(module);
        }

        references.ShouldContain("Domain");
        references.ShouldContain("Application");
        references.ShouldContain("Observability");
        references.ShouldContain("Scheduling");
    }

    [Theory]
    [InlineData("Data.Sql", "Data")]
    [InlineData("Data.NoSql", "Data")]
    [InlineData("Data.Cache", "Data")]
    [InlineData("Data.Broker", "Data")]
    [InlineData("Data.RestApi", "Api")]
    [InlineData("Data.GraphqlApi", "Api")]
    [InlineData("Data.GrpcApi", "Api")]
    [InlineData("CrossCutting", "Platform")]
    [InlineData("Observability", "Platform")]
    [InlineData("Scheduling", "Platform")]
    public void InfrastructureProject_LivesInItsDesignatedGroup(string projectName, string groupName)
    {
        var expected = Path.Combine(
            RepositoryRoot(),
            "Source",
            "Infrastructure",
            groupName,
            projectName,
            $"{projectName}.csproj");

        File.Exists(expected).ShouldBeTrue(
            $"{projectName} must live in Source/Infrastructure/{groupName}/. Expected {expected}.");
    }

    [Fact]
    public void EveryInfrastructureProject_SitsInsideAGroupFolder()
    {
        var infrastructure = Path.Combine(RepositoryRoot(), "Source", "Infrastructure");

        var groups = new[] { "Api", "Data", "Platform" };

        var misplaced = Directory
            .EnumerateFiles(infrastructure, "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(infrastructure, path))
            .Where(relative => !groups.Contains(
                relative.Split(Path.DirectorySeparatorChar)[0],
                StringComparer.Ordinal))
            .ToArray();

        misplaced.ShouldBeEmpty(
            "Every Infrastructure project must live under Api/, Data/ or Platform/. "
            + $"Misplaced: {string.Join(", ", misplaced)}");
    }

    [Fact]
    public void EveryInfrastructureGroup_HoldsAtLeastOneProject()
    {
        var infrastructure = Path.Combine(RepositoryRoot(), "Source", "Infrastructure");

        foreach (var group in new[] { "Api", "Data", "Platform" })
        {
            var groupPath = Path.Combine(infrastructure, group);

            Directory.Exists(groupPath).ShouldBeTrue($"Missing group folder {group}.");

            Directory
                .EnumerateFiles(groupPath, "*.csproj", SearchOption.AllDirectories)
                .ShouldNotBeEmpty($"Group {group} holds no project.");
        }
    }

    [Fact]
    public void SolutionFile_ReferencesEveryProjectAtItsRealPath()
    {
        var solution = Path.Combine(RepositoryRoot(), "ProjectTemplate-WebApi.slnx");

        var declared = XDocument
            .Load(solution)
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();

        declared.ShouldNotBeEmpty();

        foreach (var path in declared)
        {
            var full = Path.Combine(RepositoryRoot(), path.Replace('/', Path.DirectorySeparatorChar));

            File.Exists(full).ShouldBeTrue($"Solution declares a project that does not exist: {path}.");
        }
    }

    [Fact]
    public void Dockerfile_CopiesEveryProjectFileItNeeds()
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepositoryRoot(), "Dockerfile"));

        foreach (var (project, group) in _infrastructureGroups)
        {
            var expected = $"Source/Infrastructure/{group}/{project}/{project}.csproj";

            dockerfile.ShouldContain(
                expected,
                customMessage: $"Dockerfile must COPY {expected} or the restore layer breaks.");
        }
    }

    private static string[] ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);

        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .ToArray();
    }

    private static string FindInfrastructureProject(string projectName)
    {
        var group = _infrastructureGroups[projectName];

        return FindProject($"Source/Infrastructure/{group}/{projectName}/{projectName}.csproj");
    }

    private static string FindProject(string relativePath)
    {
        var fullPath = Path.Combine(
            RepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(fullPath).ShouldBeTrue($"Expected project file at {fullPath}.");

        return fullPath;
    }

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
