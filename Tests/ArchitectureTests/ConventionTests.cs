using System.Reflection;
using Domain.Abstractions;
using NetArchTest.Rules;

namespace ArchitectureTests;

public sealed class ConventionTests
{
    private static readonly Assembly _domainAssembly = typeof(Domain.Results.Result).Assembly;
    private static readonly Assembly _applicationAssembly = typeof(Application.Dispatching.Sender).Assembly;

    private static readonly string[] _projectAssemblyNames =
    [
        "Domain",
        "Application",
        "CrossCutting",
        "Observability",
        "Scheduling",
        "Data.Sql",
        "Data.NoSql",
        "Data.Cache",
        "Data.Broker",
        "Data.RestApi",
        "Data.GraphqlApi",
        "Data.GrpcApi",
        "WebApi",
    ];

    [Theory]
    [InlineData("Domain")]
    [InlineData("Application")]
    [InlineData("CrossCutting")]
    [InlineData("Observability")]
    [InlineData("Scheduling")]
    [InlineData("Data.Sql")]
    [InlineData("Data.NoSql")]
    [InlineData("Data.Cache")]
    [InlineData("Data.Broker")]
    [InlineData("Data.RestApi")]
    [InlineData("Data.GraphqlApi")]
    [InlineData("Data.GrpcApi")]
    [InlineData("WebApi")]
    public void EveryProject_HasASetupNamespace(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var setupTypes = assembly
            .GetTypes()
            .Where(type => type.Namespace?.EndsWith(".Setup", StringComparison.Ordinal) == true)
            .ToArray();

        setupTypes.ShouldNotBeEmpty(
            $"{assemblyName} must expose its registrations from a Setup/ folder " +
            $"(file Add{{Concern}}Setup.cs -> class {{Concern}}Setup -> method Add{{Concern}}Setup).");
    }

    [Theory]
    [InlineData("Domain")]
    [InlineData("Application")]
    [InlineData("CrossCutting")]
    [InlineData("Observability")]
    [InlineData("Scheduling")]
    [InlineData("Data.Sql")]
    [InlineData("Data.NoSql")]
    [InlineData("Data.Cache")]
    [InlineData("Data.Broker")]
    [InlineData("Data.RestApi")]
    [InlineData("Data.GraphqlApi")]
    [InlineData("Data.GrpcApi")]
    [InlineData("WebApi")]
    public void SetupClasses_AreStaticAndNamedBySuffix(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);

        var violations = assembly
            .GetTypes()
            .Where(type =>
                type.Namespace?.EndsWith(".Setup", StringComparison.Ordinal) == true
                && type.IsPublic
                && type.Name.EndsWith("Setup", StringComparison.Ordinal))

            .Where(type => !(type.IsAbstract && type.IsSealed))
            .Select(type => type.FullName)
            .ToArray();

        violations.ShouldBeEmpty(
            $"Setup classes must be static extension-method holders. Offenders: {string.Join(", ", violations)}");
    }

    [Fact]
    public void AggregateRoots_ExposeNoPublicSetters()
    {
        var violations = _domainAssembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && IsAggregateRoot(type))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })

                .Where(property => !IsInfrastructureStamp(property))
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToArray();

        violations.ShouldBeEmpty(
            $"Aggregate state must change through methods, not setters. Offenders: {string.Join(", ", violations)}");
    }

    [Fact]
    public void RequestHandlers_AreSealed()
    {
        var result = Types.InAssembly(_applicationAssembly)
            .That()
            .ImplementInterface(typeof(Domain.Interfaces.Messaging.IRequestHandler<,>))
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Handlers must be sealed. Offenders: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void PipelineBehaviors_AreSealed()
    {
        var result = Types.InAssembly(_applicationAssembly)
            .That()
            .ImplementInterface(typeof(Domain.Interfaces.Messaging.IPipelineBehavior<,>))
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Behaviours must be sealed. Offenders: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void NoProject_UsesNewtonsoftJson()
    {
        var offenders = _projectAssemblyNames
            .Select(Assembly.Load)
            .Where(assembly => assembly
                .GetReferencedAssemblies()
                .Any(reference => reference.Name?.Contains("Newtonsoft", StringComparison.OrdinalIgnoreCase) == true))
            .Select(assembly => assembly.GetName().Name)
            .ToArray();

        offenders.ShouldBeEmpty(
            $"System.Text.Json is the only serializer in this template. Offenders: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoProject_UsesAutoMapper()
    {
        var offenders = _projectAssemblyNames
            .Select(Assembly.Load)
            .Where(assembly => assembly
                .GetReferencedAssemblies()
                .Any(reference => reference.Name?.StartsWith("AutoMapper", StringComparison.OrdinalIgnoreCase) == true))
            .Select(assembly => assembly.GetName().Name)
            .ToArray();

        offenders.ShouldBeEmpty(
            $"Mapster is the mapper in this template. Offenders: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void NoProject_UsesMediatR()
    {
        var offenders = _projectAssemblyNames
            .Select(Assembly.Load)
            .Where(assembly => assembly
                .GetReferencedAssemblies()
                .Any(reference => reference.Name?.StartsWith("MediatR", StringComparison.OrdinalIgnoreCase) == true))
            .Select(assembly => assembly.GetName().Name)
            .ToArray();

        offenders.ShouldBeEmpty(
            $"The CQRS dispatcher is hand-rolled. Offenders: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void WebApi_DefinesNoMinimalApiEndpointAbstraction()
    {
        var webApi = Assembly.Load("WebApi");

        var offenders = webApi
            .GetTypes()
            .Where(type => type.Name is "IEndpoint" or "IEndpointDefinition" or "EndpointExtensions")
            .Select(type => type.FullName)
            .ToArray();

        offenders.ShouldBeEmpty(
            $"This template is controllers-only. Offenders: {string.Join(", ", offenders)}");
    }

    private static bool IsAggregateRoot(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInfrastructureStamp(PropertyInfo property) =>
        property.DeclaringType is not null
        && (typeof(IAuditable).GetProperty(property.Name) is not null
            || typeof(ISoftDeletable).GetProperty(property.Name) is not null);
}
