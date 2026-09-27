using System.Diagnostics.CodeAnalysis;
using Data.Sql.EntityFrameworkContexts.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Data.Sql.EntityFrameworkContexts.DesignTime;

[SuppressMessage(
    "Security",
    "S2068:Hard-coded credentials are security-sensitive",
    Justification = "These are design-time placeholders for the `dotnet ef` tooling, which needs a " +
                    "syntactically valid connection string to build a model but never opens a " +
                    "connection for `migrations add`. Each is overridden by its environment " +
                    "variable. Runtime connection strings are assembled by the matching " +
                    "ISqlDatabaseProvider from a resolved secret.")]
internal static class DesignTimeConnectionStrings
{
    public const string Postgresql =
        "Host=localhost;Port=5432;Database=webapi_template;Username=postgres;Password=postgres";

    public const string SqlServer =
        "Server=localhost,1433;Database=webapi_template;User ID=sa;Password=Local_Dev_1;TrustServerCertificate=True";

    public const string Mysql =
        "Server=localhost;Port=3306;Database=webapi_template;User ID=root;Password=root";

    public const string Oracle =
        "Data Source=localhost:1521/FREEPDB1;User ID=system;Password=oracle";

    public const string Firebird =
        "DataSource=localhost;Port=3050;Database=webapi_template;User=SYSDBA;Password=masterkey;Charset=UTF8";

    public const string Sqlite = "Data Source=webapi_template.db";

    public static string Resolve(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) ?? fallback;
}

public sealed class PostgresqlDesignTimeFactory : IDesignTimeDbContextFactory<PostgresqlAppDbContext>
{
    public PostgresqlAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresqlAppDbContext>()
            .UseNpgsql(DesignTimeConnectionStrings.Resolve(
                "APPDBCONTEXT_POSTGRESQL",
                DesignTimeConnectionStrings.Postgresql))
            .Options);
}

public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerAppDbContext>
{
    public SqlServerAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqlServerAppDbContext>()
            .UseSqlServer(DesignTimeConnectionStrings.Resolve(
                "APPDBCONTEXT_SQLSERVER",
                DesignTimeConnectionStrings.SqlServer))
            .Options);
}

public sealed class MysqlDesignTimeFactory : IDesignTimeDbContextFactory<MysqlAppDbContext>
{
    public MysqlAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MysqlAppDbContext>()
            .UseMySQL(DesignTimeConnectionStrings.Resolve(
                "APPDBCONTEXT_MYSQL",
                DesignTimeConnectionStrings.Mysql))
            .Options);
}

public sealed class OracleDesignTimeFactory : IDesignTimeDbContextFactory<OracleAppDbContext>
{
    public OracleAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OracleAppDbContext>()
            .UseOracle(DesignTimeConnectionStrings.Resolve(
                "APPDBCONTEXT_ORACLE",
                DesignTimeConnectionStrings.Oracle))
            .Options);
}

public sealed class FirebirdDesignTimeFactory : IDesignTimeDbContextFactory<FirebirdAppDbContext>
{
    public FirebirdAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FirebirdAppDbContext>()
            .UseFirebird(DesignTimeConnectionStrings.Resolve(
                "APPDBCONTEXT_FIREBIRD",
                DesignTimeConnectionStrings.Firebird))
            .Options);
}

public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteAppDbContext>()
            .UseSqlite(DesignTimeConnectionStrings.Resolve(
                "APPDBCONTEXT_SQLITE",
                DesignTimeConnectionStrings.Sqlite))
            .Options);
}
