using System.Data.Common;
using System.Globalization;
using Data.Sql.DynamicData;
using Data.Sql.EntityFrameworkContexts.Contexts;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using FirebirdSql.Data.FirebirdClient;
using Microsoft.EntityFrameworkCore;

namespace Data.Sql.Providers;

public sealed class FirebirdDialect : ISqlDialect
{
    public DatabaseType ProviderType => DatabaseType.Firebird;

    public string ParameterPrefix => "@";

    public string JsonColumnType => "BLOB SUB_TYPE TEXT";

    public int MaxIdentifierLength => 63;

    public bool SupportsSavepoints => true;

    public string QuoteIdentifier(string name) => $"\"{name}\"";

    public string Parameter(string name) => $"{ParameterPrefix}{name}";

    public string BooleanLiteral(bool value) => value ? "TRUE" : "FALSE";

    public string CaseInsensitiveLike(string column, string parameter) =>
        $"UPPER({column}) LIKE UPPER({parameter})";

    public string EscapeLikePattern(string value) =>
        SqlLikePatterns.Escape(value, escapeBrackets: false);

    public string ApplyPagination(string sql, int offset, int limit) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{sql} ROWS {offset + 1} TO {offset + limit}");
}

public sealed class FirebirdDatabaseProvider : SqlDatabaseProviderBase<FirebirdAppDbContext>
{
    public override DatabaseType ProviderType => DatabaseType.Firebird;

    public override int DefaultPort => 3050;

    public override ISqlDialect Dialect { get; } = new FirebirdDialect();

    public override SqlCatalogReader CatalogReader { get; } = new FirebirdCatalogReader();

    public override string BuildConnectionString(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new FbConnectionStringBuilder
        {
            DataSource = connection.Host,
            Port = PortOf(connection),
            Database = connection.Database,
            UserID = connection.Username,
            Password = connection.Password,
            CommandTimeout = connection.TimeoutSeconds,
            MaxPoolSize = connection.Sql.MaxPoolSize,
            Charset = "UTF8",
            Pooling = true,
        }.ConnectionString;
    }

    public override DbConnection CreateConnection(string connectionString) =>
        new FbConnection(connectionString);

    protected override void ConfigureProvider(
        DbContextOptionsBuilder builder,
        SqlProviderRegistration registration) =>
        builder.UseFirebird(registration.ConnectionString, provider =>
        {
            provider.MigrationsAssembly(MigrationsAssemblyName);

            provider.CommandTimeout(registration.Connection.TimeoutSeconds);
        });
}
