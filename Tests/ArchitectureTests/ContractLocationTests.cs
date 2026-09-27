using System.Reflection;
using System.Runtime.CompilerServices;

namespace ArchitectureTests;

public sealed class ContractLocationTests
{
    private static readonly Assembly[] _nonDomainAssemblies =
    [
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

    public static TheoryData<string> NonDomainAssemblyNames =>
        [.. _nonDomainAssemblies.Select(assembly => assembly.GetName().Name!)];

    [Theory]
    [MemberData(nameof(NonDomainAssemblyNames))]
    public void InterfacesEnumsAndRecordsAreDeclaredOnlyInDomain(string assemblyName)
    {
        var assembly = _nonDomainAssemblies.Single(candidate => candidate.GetName().Name == assemblyName);

        var misplaced = assembly
            .GetTypes()
            .Where(IsHandWrittenTopLevelType)
            .Where(type => type.IsInterface || type.IsEnum || IsRecord(type))
            .Select(type => $"{Kind(type)} {type.FullName}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        misplaced.ShouldBeEmpty(
            $"Interfaces, enums and records (DTOs, models, options, commands, queries) belong in the "
            + $"Domain project so every layer can use them. Move these out of {assemblyName}: "
            + string.Join(", ", misplaced));
    }

    private static bool IsHandWrittenTopLevelType(Type type) =>
        !type.IsNested
        && type.Namespace is not null
        && type.GetCustomAttribute<CompilerGeneratedAttribute>() is null
        && !type.Name.Contains('<', StringComparison.Ordinal)
        && !IsGeneratedByTooling(type);

    private static bool IsGeneratedByTooling(Type type) =>
        type.GetCustomAttributes().Any(attribute =>
            attribute.GetType().Name is "GeneratedCodeAttribute" or "EmbeddedAttribute")
        || type.Namespace!.StartsWith("Microsoft.", StringComparison.Ordinal)
        || type.Namespace.StartsWith("System.", StringComparison.Ordinal);

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null
        || (type.IsValueType
            && type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null);

    private static string Kind(Type type)
    {
        if (type.IsInterface)
        {
            return "interface";
        }

        return type.IsEnum ? "enum" : "record";
    }
}
