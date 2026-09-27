using Domain.Enums;
using Domain.Models.Configuration;

namespace UnitTests.Domain;

public sealed class DatabaseConfigurationTests
{
    [Fact]
    public void EveryDatabaseTypeBelongsToAFamily()
    {
        foreach (var type in Enum.GetValues<DatabaseType>())
        {
            Should.NotThrow(() => type.Family(), $"DatabaseType.{type} must be classified.");
        }
    }

    [Theory]
    [InlineData(DatabaseType.Postgresql, DatabaseFamily.Relational)]
    [InlineData(DatabaseType.Mariadb, DatabaseFamily.Relational)]
    [InlineData(DatabaseType.Sqlite, DatabaseFamily.Relational)]
    [InlineData(DatabaseType.MongoDb, DatabaseFamily.Document)]
    [InlineData(DatabaseType.CosmosDb, DatabaseFamily.Document)]
    [InlineData(DatabaseType.DynamoDb, DatabaseFamily.Document)]
    [InlineData(DatabaseType.RavenDb, DatabaseFamily.Document)]
    public void FamilyClassifiesTheEngine(DatabaseType type, DatabaseFamily expected) =>
        type.Family().ShouldBe(expected);

    [Fact]
    public void GetDatabaseMatchesIdCaseInsensitively()
    {
        var settings = Settings(("DEFAULT", DatabaseType.Postgresql));

        settings.GetDatabase("default").Id.ShouldBe("DEFAULT");
    }

    [Fact]
    public void GetDatabaseNamesTheConfiguredIdsWhenNothingMatches()
    {
        var settings = Settings(("DEFAULT", DatabaseType.Postgresql), ("DOCUMENTS", DatabaseType.MongoDb));

        var exception = Should.Throw<InvalidOperationException>(() => settings.GetDatabase("MISSING"));

        exception.Message.ShouldContain("'MISSING'");
        exception.Message.ShouldContain("DEFAULT, DOCUMENTS");
    }

    [Fact]
    public void GetDatabaseRejectsAnEntryOfTheWrongFamily()
    {
        var settings = Settings(("DOCUMENTS", DatabaseType.MongoDb));

        var exception = Should.Throw<InvalidOperationException>(() =>
            settings.GetDatabase("DOCUMENTS", DatabaseFamily.Relational));

        exception.Message.ShouldContain("MongoDb");
        exception.Message.ShouldContain("Relational");
    }

    [Fact]
    public void GetDatabaseReturnsAnEntryOfTheExpectedFamily()
    {
        var settings = Settings(("DOCUMENTS", DatabaseType.MongoDb));

        settings.GetDatabase("DOCUMENTS", DatabaseFamily.Document).Type.ShouldBe(DatabaseType.MongoDb);
    }

    [Fact]
    public void ValidateConnectionsRejectsDuplicateIdsIgnoringCase()
    {
        var settings = Settings(("DEFAULT", DatabaseType.Postgresql), ("default", DatabaseType.MongoDb));

        Should.Throw<InvalidOperationException>(settings.ValidateConnections)
            .Message.ShouldContain("DEFAULT");
    }

    [Fact]
    public void ValidateConnectionsRejectsABlankId()
    {
        var settings = Settings(("DEFAULT", DatabaseType.Postgresql), (" ", DatabaseType.MongoDb));

        Should.Throw<InvalidOperationException>(settings.ValidateConnections)
            .Message.ShouldContain("Settings:Databases:1:Id");
    }

    [Fact]
    public void ValidateConnectionsAcceptsDistinctIds()
    {
        var settings = Settings(("DEFAULT", DatabaseType.Postgresql), ("DOCUMENTS", DatabaseType.MongoDb));

        Should.NotThrow(settings.ValidateConnections);
    }

    [Fact]
    public void KeyOfNamesTheEntryById()
    {
        var connection = new DatabaseSettings { Id = "EVENTS", Type = DatabaseType.DynamoDb };

        connection.KeyOf("Cloud:Region").ShouldBe("Settings:Databases[Id=EVENTS]:Cloud:Region");
    }

    private static AppSettings Settings(params (string Id, DatabaseType Type)[] entries) =>
        new()
        {
            Databases = [.. entries.Select(entry => new DatabaseSettings { Id = entry.Id, Type = entry.Type })],
        };
}
