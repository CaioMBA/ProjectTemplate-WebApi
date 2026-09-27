using Data.Sql.Providers;
using Domain.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Data.Sql.EntityFrameworkContexts.Contexts;

public sealed class PostgresqlAppDbContext(DbContextOptions<PostgresqlAppDbContext> options)
    : AppDbContext(options)
{
    protected override ISqlDialect Dialect { get; } = new PostgresqlDialect();
}

public sealed class SqlServerAppDbContext(DbContextOptions<SqlServerAppDbContext> options)
    : AppDbContext(options)
{
    protected override ISqlDialect Dialect { get; } = new SqlServerDialect();
}

public sealed class MysqlAppDbContext(DbContextOptions<MysqlAppDbContext> options)
    : AppDbContext(options)
{
    protected override ISqlDialect Dialect { get; } = new MysqlDialect();
}

public sealed class OracleAppDbContext(DbContextOptions<OracleAppDbContext> options)
    : AppDbContext(options)
{
    protected override ISqlDialect Dialect { get; } = new OracleDialect();
}

public sealed class FirebirdAppDbContext(DbContextOptions<FirebirdAppDbContext> options)
    : AppDbContext(options)
{
    protected override ISqlDialect Dialect { get; } = new FirebirdDialect();
}

public sealed class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options)
    : AppDbContext(options)
{
    protected override ISqlDialect Dialect { get; } = new SqliteDialect();
}
