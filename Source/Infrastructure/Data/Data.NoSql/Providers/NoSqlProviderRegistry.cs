using Domain.Enums;
using Domain.Interfaces.Persistence;

namespace Data.NoSql.Providers;

public static class NoSqlProviderRegistry
{
    private static readonly INoSqlDatabaseProvider[] _providers =
    [
        new MongoDbProvider(),
        new CosmosDbProvider(),
        new DynamoDbProvider(),
        new RavenDbProvider(),
    ];

    public static IReadOnlyList<INoSqlDatabaseProvider> All => _providers;

    public static INoSqlDatabaseProvider Resolve(DatabaseType providerType)
    {
        if (providerType.Family() != DatabaseFamily.Document)
        {
            throw new NotSupportedException(
                $"DatabaseType '{providerType}' is a {providerType.Family()} database; "
                + "the NoSql module only serves Document engines. Relational entries are served by the Sql module.");
        }

        return _providers.SingleOrDefault(provider => provider.ProviderType == providerType)
            ?? throw new NotSupportedException(
                $"DatabaseType '{providerType}' has no INoSqlDatabaseProvider. "
                + $"Registered providers: {string.Join(", ", _providers.Select(p => p.ProviderType))}.");
    }
}
