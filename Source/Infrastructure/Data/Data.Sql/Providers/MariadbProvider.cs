using System.Data.Common;
using Data.Sql.DynamicData;
using Data.Sql.EntityFrameworkContexts;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Data.Sql.Providers;

public sealed class MariadbDialect : ISqlDialect
{
    private readonly MysqlDialect _wireCompatible = new();

    public DatabaseType ProviderType => DatabaseType.Mariadb;

    public string ParameterPrefix => _wireCompatible.ParameterPrefix;

    public string JsonColumnType => _wireCompatible.JsonColumnType;

    public int MaxIdentifierLength => _wireCompatible.MaxIdentifierLength;

    public bool SupportsSavepoints => _wireCompatible.SupportsSavepoints;

    public string QuoteIdentifier(string name) => _wireCompatible.QuoteIdentifier(name);

    public string Parameter(string name) => _wireCompatible.Parameter(name);

    public string BooleanLiteral(bool value) => _wireCompatible.BooleanLiteral(value);

    public string CaseInsensitiveLike(string column, string parameter) =>
        _wireCompatible.CaseInsensitiveLike(column, parameter);

    public string EscapeLikePattern(string value) =>
        SqlLikePatterns.Escape(value, escapeBrackets: false);

    public string ApplyPagination(string sql, int offset, int limit) =>
        _wireCompatible.ApplyPagination(sql, offset, limit);
}

public sealed class MariadbDatabaseProvider : SqlDatabaseProvider
{
    private const string UnavailableMessage =
        "DatabaseType.Mariadb has no Entity Framework Core 10 driver. "
        + "Pomelo.EntityFrameworkCore.MySql 9.0.0 pins Microsoft.EntityFrameworkCore.Relational to "
        + "[9.0.0, 9.0.999] and has no EF 10 build, so referencing it would force the whole solution "
        + "back to EF 9. Set the database entry's Type to Mysql instead - MariaDB is wire compatible "
        + "with the MySQL driver for mainstream schemas - or wait for Pomelo to ship EF 10. "
        + "See docs/adr/0008-multi-provider-sql.md.";

    public override DatabaseType ProviderType => DatabaseType.Mariadb;

    public override int DefaultPort => 3306;

    public override ISqlDialect Dialect { get; } = new MariadbDialect();

    public override Type ContextType => throw Unavailable();

    public override bool IsAvailable => false;

    public override string? UnavailableReason => UnavailableMessage;

    public override string BuildConnectionString(DatabaseSettings connection) => throw Unavailable();

    public override DbConnection CreateConnection(string connectionString) => throw Unavailable();

    public override void ConfigureOptions(DbContextOptionsBuilder builder, SqlProviderRegistration registration) =>
        throw Unavailable();

    public override AppDbContext CreateContext(IServiceProvider scope, SqlProviderRegistration registration) =>
        throw Unavailable();

    public override void ConfigureDapperTypeHandlers() => throw Unavailable();

    private static NotSupportedException Unavailable() => new(UnavailableMessage);
}
