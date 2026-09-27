using System.Data.Common;
using System.Globalization;
using Data.Sql.DynamicData;
using Data.Sql.EntityFrameworkContexts.Contexts;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Data.Sql.Providers;

public sealed class MysqlDialect : ISqlDialect
{
    public DatabaseType ProviderType => DatabaseType.Mysql;

    public string ParameterPrefix => "@";

    public string JsonColumnType => "json";

    public int MaxIdentifierLength => 64;

    public bool SupportsSavepoints => true;

    public string QuoteIdentifier(string name) => $"`{name}`";

    public string Parameter(string name) => $"{ParameterPrefix}{name}";

    public string BooleanLiteral(bool value) => value ? "1" : "0";

    public string CaseInsensitiveLike(string column, string parameter) =>
        $"{column} LIKE {parameter}";

    public string EscapeLikePattern(string value) =>
        SqlLikePatterns.Escape(value, escapeBrackets: false);

    public string ApplyPagination(string sql, int offset, int limit) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{sql} LIMIT {limit} OFFSET {offset}");
}

public sealed class MysqlDatabaseProvider : SqlDatabaseProviderBase<MysqlAppDbContext>
{
    public override DatabaseType ProviderType => DatabaseType.Mysql;

    public override int DefaultPort => 3306;

    public override ISqlDialect Dialect { get; } = new MysqlDialect();

    public override SqlCatalogReader CatalogReader { get; } = new MysqlCatalogReader();

    public override string BuildConnectionString(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new MySqlConnectionStringBuilder
        {
            Server = connection.Host,
            Port = (uint)PortOf(connection),
            Database = connection.Database,
            UserID = connection.Username,
            Password = connection.Password,
            DefaultCommandTimeout = (uint)connection.TimeoutSeconds,
            MaximumPoolSize = (uint)connection.Sql.MaxPoolSize,
            SslMode = connection.UseSsl ? MySqlSslMode.Required : MySqlSslMode.Disabled,
        }.ConnectionString;
    }

    public override DbConnection CreateConnection(string connectionString) =>
        new MySqlConnection(connectionString);

    protected override void ConfigureProvider(
        DbContextOptionsBuilder builder,
        SqlProviderRegistration registration) =>
        builder.UseMySQL(registration.ConnectionString, provider =>
        {
            provider.MigrationsAssembly(MigrationsAssemblyName);

            provider.CommandTimeout(registration.Connection.TimeoutSeconds);
        });
}
