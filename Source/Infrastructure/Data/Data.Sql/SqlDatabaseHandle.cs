using Data.Sql.Providers;
using Data.Sql.Setup;
using Domain.Models.Configuration;
using Domain.Models.Persistence;

namespace Data.Sql;

public sealed class SqlDatabaseHandle
{
    private SqlDatabaseHandle(SqlDatabaseProvider provider, SqlProviderRegistration registration)
    {
        Provider = provider;
        Registration = registration;
    }

    public SqlDatabaseProvider Provider { get; }

    public SqlProviderRegistration Registration { get; }

    public static SqlDatabaseHandle Create(DatabaseSettings connection, bool recordStatements)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var provider = SqlProviderRegistry.Resolve(connection.Type);

        var connectionString = string.IsNullOrWhiteSpace(connection.ConnectionString)
            ? provider.BuildConnectionString(connection)
            : connection.ConnectionString;

        DapperSetup.EnsureProviderTypeHandlers(provider);

        return new SqlDatabaseHandle(
            provider,
            new SqlProviderRegistration(connectionString, connection, recordStatements));
    }
}