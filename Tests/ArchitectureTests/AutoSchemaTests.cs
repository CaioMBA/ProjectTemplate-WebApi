using System.Reflection;
using Domain.Interfaces.Persistence;
using NetArchTest.Rules;

namespace ArchitectureTests;

public sealed class AutoSchemaTests
{
    private static readonly Assembly _webApi = typeof(Program).Assembly;

    [Fact]
    public void OnlyWebApiReferencesHotChocolate()
    {
        string[] projects =
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
        ];

        var violations = projects
            .Where(name => Assembly.Load(name)
                .GetReferencedAssemblies()
                .Any(reference => reference.Name?.StartsWith("HotChocolate", StringComparison.Ordinal) == true
                                  || reference.Name?.StartsWith("GreenDonut", StringComparison.Ordinal) == true))
            .ToArray();

        violations.ShouldBeEmpty(
            "The GraphQL server lives in WebApi; data modules expose IDynamicDataSource instead. "
            + $"Found HotChocolate references in: {string.Join(", ", violations)}");
    }

    [Fact]
    public void TheAutoSchemaReachesDatabasesOnlyThroughTheDynamicDataSourcePort()
    {
        var result = Types.InAssembly(_webApi)
            .That()
            .ResideInNamespace("WebApi.GraphQL.AutoSchema")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Data.Sql",
                "Data.NoSql",
                "Dapper",
                "Npgsql",
                "MongoDB",
                "Microsoft.Azure.Cosmos",
                "Raven",
                "Amazon",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "The auto schema builds GraphQL types from IDynamicDataSource descriptions only. Offending types: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("Data.Sql")]
    [InlineData("Data.NoSql")]
    public void EveryDataModuleWithDatabasesImplementsTheDynamicDataSource(string moduleName)
    {
        var implementations = Assembly.Load(moduleName)
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IDynamicDataSource).IsAssignableFrom(type))
            .ToList();

        implementations.ShouldNotBeEmpty($"{moduleName} must expose its databases to the GraphQL auto schema.");
        implementations.ShouldAllBe(type => type.IsSealed);
    }
}
