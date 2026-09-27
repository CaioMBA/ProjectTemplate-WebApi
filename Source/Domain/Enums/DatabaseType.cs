using System.Text.Json.Serialization;

namespace Domain.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<DatabaseType>))]
public enum DatabaseType
{
    Postgresql = 0,
    SqlServer = 1,
    Mysql = 2,
    Mariadb = 3,
    Oracle = 4,
    Firebird = 5,
    Sqlite = 6,
    MongoDb = 7,
    CosmosDb = 8,
    DynamoDb = 9,
    RavenDb = 10,
}

[JsonConverter(typeof(JsonStringEnumConverter<DatabaseFamily>))]
public enum DatabaseFamily
{
    Relational = 0,
    Document = 1,
}

public static class DatabaseTypeExtensions
{
    public static DatabaseFamily Family(this DatabaseType type) =>
        type switch
        {
            DatabaseType.Postgresql
                or DatabaseType.SqlServer
                or DatabaseType.Mysql
                or DatabaseType.Mariadb
                or DatabaseType.Oracle
                or DatabaseType.Firebird
                or DatabaseType.Sqlite => DatabaseFamily.Relational,

            DatabaseType.MongoDb
                or DatabaseType.CosmosDb
                or DatabaseType.DynamoDb
                or DatabaseType.RavenDb => DatabaseFamily.Document,

            _ => throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                $"DatabaseType '{type}' has no DatabaseFamily."),
        };
}
