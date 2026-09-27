using System.Reflection;

namespace ArchitectureTests;

public sealed class DeadCodeTests
{
    private static readonly Assembly _domainAssembly = typeof(Domain.Results.Result).Assembly;

    private static readonly Assembly[] _solutionAssemblies =
    [
        _domainAssembly,
        typeof(Application.Dispatching.Sender).Assembly,
        typeof(CrossCutting.Setup.CrossCuttingSetup).Assembly,
        typeof(Observability.Setup.ObservabilitySetup).Assembly,
        typeof(Scheduling.Setup.SchedulingSetup).Assembly,
        typeof(Data.Sql.Setup.DataSqlSetup).Assembly,
        typeof(Data.NoSql.Setup.DataNoSqlSetup).Assembly,
        typeof(Data.Cache.Setup.DataCacheSetup).Assembly,
        typeof(Data.Broker.Setup.DataBrokerSetup).Assembly,
        typeof(Data.RestApi.Setup.DataRestApiSetup).Assembly,
        typeof(Data.GraphqlApi.Setup.DataGraphqlApiSetup).Assembly,
        typeof(Data.GrpcApi.Setup.DataGrpcApiSetup).Assembly,
        typeof(Program).Assembly,
    ];

    private static IEnumerable<Type> DomainPorts =>
        _domainAssembly
            .GetTypes()
            .Where(type => type.IsInterface
                           && type.IsPublic
                           && type.Namespace?.StartsWith("Domain.Interfaces", StringComparison.Ordinal) == true);

    [Fact]
    public void EveryDomainPortHasAtLeastOneImplementation()
    {
        var implementations = _solutionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .ToList();

        var unimplemented = DomainPorts
            .Where(port => !implementations.Any(type => Implements(type, port)))
            .Select(port => port.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        unimplemented.ShouldBeEmpty(
            $"every interface under Domain.Interfaces must have a concrete implementation, " +
            $"otherwise it is a contract nothing honours: {string.Join(", ", unimplemented)}");
    }

    [Fact]
    public void EveryMarkerInterfaceIsAppliedSomewhere()
    {
        var markers = _domainAssembly
            .GetTypes()
            .Where(type => type.IsInterface
                           && type.IsPublic
                           && type.Namespace == "Domain.Interfaces.Messaging"
                           && type.GetMethods().Length == 0
                           && type.GetProperties().Length > 0
                           && !type.IsGenericTypeDefinition)
            .ToList();

        var users = _solutionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsAbstract: false })
            .ToList();

        var unused = markers
            .Where(marker => !users.Any(type => Implements(type, marker)))
            .Select(marker => marker.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        unused.ShouldBeEmpty(
            $"a marker interface the pipeline branches on must be implemented by at least one " +
            $"request, otherwise the branch is unreachable: {string.Join(", ", unused)}");
    }

    [Fact]
    public void EveryEnumMemberIsReferencedOutsideItsOwnDeclaration()
    {
        var enums = _domainAssembly
            .GetTypes()
            .Where(type => type.IsEnum && type.IsPublic)
            .ToList();

        enums.ShouldNotBeEmpty();

        var emptyEnums = enums
            .Where(type => Enum.GetNames(type).Length == 0)
            .Select(type => type.Name)
            .ToList();

        emptyEnums.ShouldBeEmpty();

        var nonZeroBased = enums
            .Where(type => !Enum.GetValues(type)
                .Cast<object>()
                .Select(value => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture))
                .Contains(0L))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        nonZeroBased.ShouldBeEmpty(
            $"an enum with no zero member binds badly from configuration because the default " +
            $"value is unrepresentable: {string.Join(", ", nonZeroBased)}");
    }

    [Fact]
    public void NoConfigurationModelDeclaresAPropertyNothingCanRead()
    {
        var settingsTypes = _domainAssembly
            .GetTypes()
            .Where(type => type.Namespace == "Domain.Models.Configuration"
                           && type is { IsClass: true, IsPublic: true })
            .ToList();

        settingsTypes.ShouldNotBeEmpty();

        var unreadable = settingsTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetGetMethod() is null)
                .Select(property => $"{type.Name}.{property.Name}"))
            .ToList();

        unreadable.ShouldBeEmpty(
            $"configuration binding needs both accessors: {string.Join(", ", unreadable)}");
    }

    private static bool Implements(Type candidate, Type contract) =>
        candidate != contract
        && !candidate.IsInterface
        && candidate.GetInterfaces().Any(implemented =>
            implemented == contract
            || (implemented.IsGenericType
                && contract.IsGenericTypeDefinition
                && implemented.GetGenericTypeDefinition() == contract));
}
