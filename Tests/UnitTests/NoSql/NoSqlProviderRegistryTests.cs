using Data.NoSql.Providers;
using Data.NoSql.Repositories;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace UnitTests.NoSql;

public sealed class NoSqlProviderRegistryTests
{
    public static TheoryData<DatabaseType> DocumentTypes =>
        [.. Enum.GetValues<DatabaseType>().Where(type => type.Family() == DatabaseFamily.Document)];

    public static TheoryData<DatabaseType> RelationalTypes =>
        [.. Enum.GetValues<DatabaseType>().Where(type => type.Family() == DatabaseFamily.Relational)];

    [Theory]
    [MemberData(nameof(DocumentTypes))]
    public void EveryDocumentTypeResolvesToExactlyOneProvider(DatabaseType type)
    {
        NoSqlProviderRegistry.All.Count(provider => provider.ProviderType == type).ShouldBe(1);

        NoSqlProviderRegistry.Resolve(type).ProviderType.ShouldBe(type);
    }

    [Theory]
    [MemberData(nameof(RelationalTypes))]
    public void RelationalTypesAreRejected(DatabaseType type) =>
        Should.Throw<NotSupportedException>(() => NoSqlProviderRegistry.Resolve(type))
            .Message.ShouldContain("Relational");

    [Fact]
    public void RegistryHoldsOnlyDocumentProviders() =>
        NoSqlProviderRegistry.All
            .ShouldAllBe(provider => provider.ProviderType.Family() == DatabaseFamily.Document);

    [Fact]
    public void RegistryContainsNoDuplicateProviderTypes()
    {
        var duplicates = NoSqlProviderRegistry.All
            .GroupBy(provider => provider.ProviderType)
            .Where(group => group.Count() > 1)
            .ToList();

        duplicates.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DatabaseType.MongoDb, typeof(MongoDocumentRepository<>))]
    [InlineData(DatabaseType.CosmosDb, typeof(CosmosDocumentRepository<>))]
    [InlineData(DatabaseType.DynamoDb, typeof(DynamoDocumentRepository<>))]
    [InlineData(DatabaseType.RavenDb, typeof(RavenDocumentRepository<>))]
    public void ProviderMapsToItsRepository(DatabaseType providerType, Type expected) =>
        NoSqlProviderRegistry.Resolve(providerType).RepositoryType.ShouldBe(expected);

    [Fact]
    public void MongoValidationRequiresConnectionStringAndDatabase()
    {
        var provider = NoSqlProviderRegistry.Resolve(DatabaseType.MongoDb);

        Should.Throw<InvalidOperationException>(() => provider.Validate(Connection(DatabaseType.MongoDb)))
            .Message.ShouldContain("Settings:Databases[Id=DOCUMENTS]:ConnectionString");

        var withConnectionString = Connection(DatabaseType.MongoDb);
        withConnectionString.ConnectionString = "mongodb://localhost:27017";

        Should.Throw<InvalidOperationException>(() => provider.Validate(withConnectionString))
            .Message.ShouldContain("Settings:Databases[Id=DOCUMENTS]:Database");
    }

    [Fact]
    public void RavenValidationRequiresClusterUrls()
    {
        var provider = NoSqlProviderRegistry.Resolve(DatabaseType.RavenDb);

        Should.Throw<InvalidOperationException>(() => provider.Validate(Connection(DatabaseType.RavenDb)))
            .Message.ShouldContain("Cluster:Urls");
    }

    [Fact]
    public void DynamoValidationRequiresCloudRegionOrServiceUrl()
    {
        var provider = NoSqlProviderRegistry.Resolve(DatabaseType.DynamoDb);

        Should.Throw<InvalidOperationException>(() => provider.Validate(Connection(DatabaseType.DynamoDb)))
            .Message.ShouldContain("Cloud:Region");

        var aws = Connection(DatabaseType.DynamoDb);
        aws.Cloud.Region = "us-east-1";

        Should.NotThrow(() => provider.Validate(aws));

        var local = Connection(DatabaseType.DynamoDb);
        local.Cloud.ServiceUrl = "http://localhost:8000";

        Should.NotThrow(() => provider.Validate(local));
    }

    [Fact]
    public void RegisterBindsOpenGenericDocumentRepository()
    {
        var services = new ServiceCollection();

        var connection = Connection(DatabaseType.MongoDb);
        connection.ConnectionString = "mongodb://localhost:27017";
        connection.Database = "templatetests";

        NoSqlProviderRegistry.Resolve(DatabaseType.MongoDb).Register(services, connection);

        services.ShouldContain(descriptor =>
            descriptor.ServiceType == typeof(IDocumentRepository<>));
    }

    private static DatabaseSettings Connection(DatabaseType type) =>
        new() { Id = "DOCUMENTS", Type = type };
}
