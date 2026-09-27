using System.Reflection;
using NetArchTest.Rules;

namespace ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly _domainAssembly = typeof(Domain.Results.Result).Assembly;
    private static readonly Assembly _applicationAssembly = typeof(Application.Dispatching.Sender).Assembly;

    private static readonly string[] _dataModuleAssemblyNames =
    [
        "Data.Sql",
        "Data.NoSql",
        "Data.Cache",
        "Data.Broker",
        "Data.RestApi",
        "Data.GraphqlApi",
        "Data.GrpcApi",
    ];

    [Fact]
    public void Domain_ReferencesNoOtherSolutionProject()
    {
        var referenced = _domainAssembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        string[] forbidden =
        [
            "Application",
            "CrossCutting",
            "Observability",
            "Scheduling",
            "WebApi",
            .. _dataModuleAssemblyNames,
        ];

        var violations = forbidden.Intersect(referenced, StringComparer.Ordinal).ToArray();

        violations.ShouldBeEmpty(
            $"Domain is the shared kernel: every other project references IT, never the reverse. " +
            $"Found references to: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Application_DoesNotReferenceAnyDataModule()
    {
        var referenced = _applicationAssembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        var violations = _dataModuleAssemblyNames.Intersect(referenced, StringComparer.Ordinal).ToArray();

        violations.ShouldBeEmpty(
            $"Application must depend only on Domain. Found references to: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Application_DoesNotReferenceWebOrCompositionRoot()
    {
        var referenced = _applicationAssembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        referenced.ShouldNotContain("WebApi");
        referenced.ShouldNotContain("CrossCutting");
    }

    [Theory]
    [InlineData("Data.Sql")]
    [InlineData("Data.NoSql")]
    [InlineData("Data.Cache")]
    [InlineData("Data.Broker")]
    [InlineData("Data.RestApi")]
    [InlineData("Data.GraphqlApi")]
    [InlineData("Data.GrpcApi")]
    public void DataModule_DoesNotReferenceAnotherDataModule(string moduleName)
    {
        var module = LoadModule(moduleName);

        var referenced = module
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        var siblings = _dataModuleAssemblyNames
            .Where(name => !string.Equals(name, moduleName, StringComparison.Ordinal))
            .ToArray();

        var violations = siblings.Intersect(referenced, StringComparer.Ordinal).ToArray();

        violations.ShouldBeEmpty(
            $"{moduleName} must not depend on another Data.* module. " +
            $"Found: {string.Join(", ", violations)}");
    }

    [Theory]
    [InlineData("Data.Sql")]
    [InlineData("Data.NoSql")]
    [InlineData("Data.Cache")]
    [InlineData("Data.Broker")]
    [InlineData("Data.RestApi")]
    [InlineData("Data.GraphqlApi")]
    [InlineData("Data.GrpcApi")]
    public void DataModule_DoesNotReferenceApplication(string moduleName)
    {
        var module = LoadModule(moduleName);

        var referenced = module
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        referenced.ShouldNotContain(
            "Application",
            $"{moduleName} must implement ports from Domain, not depend on Application.");
    }

    [Theory]
    [InlineData("Observability")]
    [InlineData("Scheduling")]
    public void PlatformService_DoesNotReferenceApplicationOrDataModules(string projectName)
    {
        var platform = LoadModule(projectName);

        var referenced = platform
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        referenced.ShouldNotContain("Application");

        var violations = _dataModuleAssemblyNames.Intersect(referenced, StringComparer.Ordinal).ToArray();

        violations.ShouldBeEmpty(
            $"{projectName} must not reference a Data.* module. Found: {string.Join(", ", violations)}");
    }

    [Fact]
    public void OnlySchedulingReferencesHangfire()
    {
        string[] projects =
        [
            "Domain",
            "Application",
            "CrossCutting",
            "Observability",
            "WebApi",
            .. _dataModuleAssemblyNames,
        ];

        var violations = projects
            .Where(name => LoadModule(name)
                .GetReferencedAssemblies()
                .Any(reference => reference.Name?.StartsWith("Hangfire", StringComparison.Ordinal) == true))
            .ToArray();

        violations.ShouldBeEmpty(
            "Hangfire stays inside Platform/Scheduling; jobs implement Domain.Interfaces.Scheduling. "
            + $"Found Hangfire references in: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Domain_ContainsNoEntityFrameworkTypes()
    {
        var result = Types.InAssembly(_domainAssembly)
            .That()
            .ResideInNamespace("Domain")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Domain must not depend on EF Core. Offending types: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_ContainsNoEntityFrameworkTypes()
    {
        var result = Types.InAssembly(_applicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application must reach persistence only through Domain.Interfaces.Persistence. " +
            "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static Assembly LoadModule(string assemblyName) => Assembly.Load(assemblyName);
}
