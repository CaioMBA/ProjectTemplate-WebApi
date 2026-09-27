using System.Data.Common;
using System.Globalization;
using Data.Sql.DynamicData;
using Data.Sql.EntityFrameworkContexts.Contexts;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;

namespace Data.Sql.Providers;

public sealed class OracleDialect : ISqlDialect
{
    public DatabaseType ProviderType => DatabaseType.Oracle;

    public string ParameterPrefix => ":";

    public string JsonColumnType => "CLOB";

    public int MaxIdentifierLength => 128;

    public bool SupportsSavepoints => true;

    public string QuoteIdentifier(string name) => $"\"{name}\"";

    public string Parameter(string name) => $"{ParameterPrefix}{name}";

    public string BooleanLiteral(bool value) => value ? "1" : "0";

    public string CaseInsensitiveLike(string column, string parameter) =>
        $"LOWER({column}) LIKE LOWER({parameter})";

    public string EscapeLikePattern(string value) =>
        SqlLikePatterns.Escape(value, escapeBrackets: false);

    public string ApplyPagination(string sql, int offset, int limit) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{sql} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY");
}

public sealed class OracleDatabaseProvider : SqlDatabaseProviderBase<OracleAppDbContext>
{
    public override DatabaseType ProviderType => DatabaseType.Oracle;

    public override int DefaultPort => 1521;

    public override ISqlDialect Dialect { get; } = new OracleDialect();

    public override SqlCatalogReader CatalogReader { get; } = new OracleCatalogReader();

    public override string BuildConnectionString(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new OracleConnectionStringBuilder
        {
            DataSource = string.Create(
                CultureInfo.InvariantCulture,
                $"{connection.Host}:{PortOf(connection)}/{connection.Database}"),
            UserID = connection.Username ?? string.Empty,
            Password = connection.Password ?? string.Empty,
            ConnectionTimeout = connection.TimeoutSeconds,
            MaxPoolSize = connection.Sql.MaxPoolSize,
        }.ConnectionString;
    }

    public override DbConnection CreateConnection(string connectionString)
    {
        var connection = new OracleConnection(connectionString);

        connection.KeepAlive = true;

        return connection;
    }

    protected override void ConfigureProvider(
        DbContextOptionsBuilder builder,
        SqlProviderRegistration registration) =>
        builder.UseOracle(registration.ConnectionString, provider =>
        {
            provider.MigrationsAssembly(MigrationsAssemblyName);

            provider.CommandTimeout(registration.Connection.TimeoutSeconds);
        });
}
