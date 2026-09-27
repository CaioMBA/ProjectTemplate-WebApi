using Domain.Enums;
using Domain.Interfaces.Persistence;

namespace Data.Sql.Providers;

public static class SqlProviderRegistry
{
    private static readonly SqlDatabaseProvider[] _providers =
    [
        new PostgresqlDatabaseProvider(),
        new SqlServerDatabaseProvider(),
        new MysqlDatabaseProvider(),
        new MariadbDatabaseProvider(),
        new OracleDatabaseProvider(),
        new FirebirdDatabaseProvider(),
        new SqliteDatabaseProvider(),
    ];

    public static IReadOnlyList<SqlDatabaseProvider> All => _providers;

    public static SqlDatabaseProvider Resolve(DatabaseType providerType)
    {
        var match = Find(providerType);

        return match.IsAvailable
            ? match
            : throw new NotSupportedException(match.UnavailableReason);
    }

    public static ISqlDialect ResolveDialect(DatabaseType providerType) =>
        Find(providerType).Dialect;

    private static SqlDatabaseProvider Find(DatabaseType providerType)
    {
        if (providerType.Family() != DatabaseFamily.Relational)
        {
            throw new NotSupportedException(
                $"DatabaseType '{providerType}' is a {providerType.Family()} database; "
                + "the Sql module only serves Relational engines. Document entries are served by the NoSql module.");
        }

        return _providers.SingleOrDefault(provider => provider.ProviderType == providerType)
            ?? throw new NotSupportedException(
                $"DatabaseType '{providerType}' has no ISqlDatabaseProvider. "
                + $"Registered providers: {string.Join(", ", _providers.Select(p => p.ProviderType))}.");
    }
}
