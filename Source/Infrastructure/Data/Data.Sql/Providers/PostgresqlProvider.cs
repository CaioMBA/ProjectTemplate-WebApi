using System.Data.Common;
using System.Globalization;
using Data.Sql.DynamicData;
using Data.Sql.EntityFrameworkContexts.Contexts;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Data.Sql.Providers;

public sealed class PostgresqlDialect : ISqlDialect
{
    public DatabaseType ProviderType => DatabaseType.Postgresql;

    public string ParameterPrefix => "@";

    public string JsonColumnType => "jsonb";

    public int MaxIdentifierLength => 63;

    public bool SupportsSavepoints => true;

    public string QuoteIdentifier(string name) => $"\"{name}\"";

    public string Parameter(string name) => $"{ParameterPrefix}{name}";

    public string BooleanLiteral(bool value) => value ? "TRUE" : "FALSE";

    public string CaseInsensitiveLike(string column, string parameter) =>
        $"{column} ILIKE {parameter}";

    public string EscapeLikePattern(string value) =>
        SqlLikePatterns.Escape(value, escapeBrackets: false);

    public string ApplyPagination(string sql, int offset, int limit) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{sql} LIMIT {limit} OFFSET {offset}");
}

public sealed class PostgresqlDatabaseProvider : SqlDatabaseProviderBase<PostgresqlAppDbContext>
{
    public override DatabaseType ProviderType => DatabaseType.Postgresql;

    public override int DefaultPort => 5432;

    public override ISqlDialect Dialect { get; } = new PostgresqlDialect();

    public override SqlCatalogReader CatalogReader { get; } = new PostgresqlCatalogReader();

    public override string BuildConnectionString(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new NpgsqlConnectionStringBuilder
        {
            Host = connection.Host,
            Port = PortOf(connection),
            Database = connection.Database,
            Username = connection.Username,
            Password = connection.Password,
            CommandTimeout = connection.TimeoutSeconds,
            MaxPoolSize = connection.Sql.MaxPoolSize,
            SslMode = connection.UseSsl ? SslMode.Require : SslMode.Disable,
            KeepAlive = 30,
        }.ConnectionString;
    }

    public override DbConnection CreateConnection(string connectionString) =>
        new NpgsqlConnection(connectionString);

    protected override void ConfigureProvider(
        DbContextOptionsBuilder builder,
        SqlProviderRegistration registration) =>
        builder.UseNpgsql(registration.ConnectionString, provider =>
        {
            provider.MigrationsAssembly(MigrationsAssemblyName);

            provider.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null);

            provider.CommandTimeout(registration.Connection.TimeoutSeconds);
        });
}
