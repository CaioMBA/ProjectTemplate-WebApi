using System.Data.Common;
using System.Globalization;
using Dapper;
using Data.Sql.DapperHandlers;
using Data.Sql.DynamicData;
using Data.Sql.EntityFrameworkContexts.Contexts;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Data.Sql.Providers;

public sealed class SqliteDialect : ISqlDialect
{
    public DatabaseType ProviderType => DatabaseType.Sqlite;

    public string ParameterPrefix => "@";

    public string JsonColumnType => "TEXT";

    public int MaxIdentifierLength => 128;

    public bool SupportsSavepoints => true;

    public string QuoteIdentifier(string name) => $"\"{name}\"";

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

public sealed class SqliteDatabaseProvider : SqlDatabaseProviderBase<SqliteAppDbContext>
{
    public override DatabaseType ProviderType => DatabaseType.Sqlite;

    public override int DefaultPort => 0;

    public override ISqlDialect Dialect { get; } = new SqliteDialect();

    public override SqlCatalogReader CatalogReader { get; } = new SqliteCatalogReader();

    public override string BuildConnectionString(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new SqliteConnectionStringBuilder
        {
            DataSource = connection.Database ?? connection.Host,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ConnectionString;
    }

    public override DbConnection CreateConnection(string connectionString) =>
        new SqliteConnection(connectionString);

    public override void ConfigureDapperTypeHandlers()
    {
        SqlMapper.AddTypeHandler(new TextGuidTypeHandler());
        SqlMapper.AddTypeHandler(new TextDateTimeTypeHandler());
        SqlMapper.AddTypeHandler(new TextDecimalTypeHandler());
    }

    protected override void ConfigureProvider(
        DbContextOptionsBuilder builder,
        SqlProviderRegistration registration) =>
        builder.UseSqlite(registration.ConnectionString, provider =>
        {
            provider.MigrationsAssembly(MigrationsAssemblyName);

            provider.CommandTimeout(registration.Connection.TimeoutSeconds);
        });
}
