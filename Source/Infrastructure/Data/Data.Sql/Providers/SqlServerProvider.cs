using System.Data.Common;
using System.Globalization;
using Data.Sql.DynamicData;
using Data.Sql.EntityFrameworkContexts.Contexts;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Data.Sql.Providers;

public sealed class SqlServerDialect : ISqlDialect
{
    public DatabaseType ProviderType => DatabaseType.SqlServer;

    public string ParameterPrefix => "@";

    public string JsonColumnType => "nvarchar(max)";

    public int MaxIdentifierLength => 128;

    public bool SupportsSavepoints => true;

    public string QuoteIdentifier(string name) => $"[{name}]";

    public string Parameter(string name) => $"{ParameterPrefix}{name}";

    public string BooleanLiteral(bool value) => value ? "1" : "0";

    public string CaseInsensitiveLike(string column, string parameter) =>
        $"{column} LIKE {parameter}";

    public string EscapeLikePattern(string value) =>
        SqlLikePatterns.Escape(value, escapeBrackets: true);

    public string ApplyPagination(string sql, int offset, int limit) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{sql} OFFSET {offset} ROWS FETCH NEXT {limit} ROWS ONLY");
}

public sealed class SqlServerDatabaseProvider : SqlDatabaseProviderBase<SqlServerAppDbContext>
{
    public override DatabaseType ProviderType => DatabaseType.SqlServer;

    public override int DefaultPort => 1433;

    public override ISqlDialect Dialect { get; } = new SqlServerDialect();

    public override SqlCatalogReader CatalogReader { get; } = new SqlServerCatalogReader();

    public override string BuildConnectionString(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new SqlConnectionStringBuilder
        {
            DataSource = string.Create(
                CultureInfo.InvariantCulture,
                $"{connection.Host},{PortOf(connection)}"),
            InitialCatalog = connection.Database ?? string.Empty,
            UserID = connection.Username ?? string.Empty,
            Password = connection.Password ?? string.Empty,
            CommandTimeout = connection.TimeoutSeconds,
            MaxPoolSize = connection.Sql.MaxPoolSize,
            Encrypt = connection.UseSsl ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = !connection.UseSsl,
            MultipleActiveResultSets = true,
        }.ConnectionString;
    }

    public override DbConnection CreateConnection(string connectionString) =>
        new SqlConnection(connectionString);

    protected override void ConfigureProvider(
        DbContextOptionsBuilder builder,
        SqlProviderRegistration registration) =>
        builder.UseSqlServer(registration.ConnectionString, provider =>
        {
            provider.MigrationsAssembly(MigrationsAssemblyName);

            provider.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);

            provider.CommandTimeout(registration.Connection.TimeoutSeconds);
        });
}
