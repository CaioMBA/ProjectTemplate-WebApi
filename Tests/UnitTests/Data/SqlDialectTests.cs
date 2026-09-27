using Data.Sql.Providers;
using Domain.Enums;

namespace UnitTests.Data;

public sealed class SqlDialectTests
{
    [Fact]
    public void Registry_ExposesExactlyOneProviderPerRelationalDatabaseType()
    {
        var declared = Enum.GetValues<DatabaseType>()
            .Where(type => type.Family() == DatabaseFamily.Relational)
            .ToArray();

        SqlProviderRegistry.All.Count.ShouldBe(declared.Length);

        foreach (var type in declared)
        {
            SqlProviderRegistry.All
                .Count(provider => provider.ProviderType == type)
                .ShouldBe(1, $"DatabaseType.{type} must map to exactly one provider.");
        }
    }

    [Theory]
    [InlineData(DatabaseType.MongoDb)]
    [InlineData(DatabaseType.CosmosDb)]
    [InlineData(DatabaseType.DynamoDb)]
    [InlineData(DatabaseType.RavenDb)]
    public void Resolve_RejectsDocumentDatabaseTypes(DatabaseType type)
    {
        Should.Throw<NotSupportedException>(() => SqlProviderRegistry.Resolve(type))
            .Message.ShouldContain("Document");

        Should.Throw<NotSupportedException>(() => SqlProviderRegistry.ResolveDialect(type));
    }

    [Theory]
    [InlineData(DatabaseType.Postgresql, 5432)]
    [InlineData(DatabaseType.SqlServer, 1433)]
    [InlineData(DatabaseType.Mysql, 3306)]
    [InlineData(DatabaseType.Oracle, 1521)]
    [InlineData(DatabaseType.Firebird, 3050)]
    public void BuildConnectionString_UsesTheEngineDefaultPortWhenNoneIsConfigured(
        DatabaseType type,
        int expectedPort)
    {
        var provider = SqlProviderRegistry.Resolve(type);

        provider.DefaultPort.ShouldBe(expectedPort);

        var connectionString = provider.BuildConnectionString(new()
        {
            Id = "DEFAULT",
            Type = type,
            Host = "db.internal",
            Database = "webapi_template",
            Username = "user",
            Password = "secret",
        });

        connectionString.ShouldContain(expectedPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BuildConnectionString_PrefersAnExplicitPort()
    {
        var connectionString = SqlProviderRegistry.Resolve(DatabaseType.Postgresql).BuildConnectionString(new()
        {
            Id = "DEFAULT",
            Type = DatabaseType.Postgresql,
            Host = "db.internal",
            Port = 6543,
            Database = "webapi_template",
        });

        connectionString.ShouldContain("6543");
        connectionString.ShouldNotContain("5432");
    }

    [Theory]
    [InlineData(DatabaseType.Postgresql)]
    [InlineData(DatabaseType.SqlServer)]
    [InlineData(DatabaseType.Mysql)]
    [InlineData(DatabaseType.Oracle)]
    [InlineData(DatabaseType.Firebird)]
    [InlineData(DatabaseType.Sqlite)]
    public void Resolve_ReturnsAUsableProviderForEveryShippedEngine(DatabaseType type)
    {
        var provider = SqlProviderRegistry.Resolve(type);

        provider.ProviderType.ShouldBe(type);
        provider.IsAvailable.ShouldBeTrue();
        provider.ContextType.ShouldNotBeNull();
        provider.Dialect.ProviderType.ShouldBe(type);
    }

    [Fact]
    public void Resolve_ThrowsForMariadbAndExplainsWhy()
    {
        var exception = Should.Throw<NotSupportedException>(() =>
            SqlProviderRegistry.Resolve(DatabaseType.Mariadb));

        exception.Message.ShouldContain("Pomelo");
        exception.Message.ShouldContain("Mysql");
    }

    [Fact]
    public void MariadbDialect_StillAnswersSyntaxQuestions()
    {
        var dialect = SqlProviderRegistry.ResolveDialect(DatabaseType.Mariadb);

        dialect.QuoteIdentifier("products").ShouldBe("`products`");
        dialect.ParameterPrefix.ShouldBe("@");
    }

    [Theory]
    [InlineData(DatabaseType.Postgresql, "\"products\"")]
    [InlineData(DatabaseType.SqlServer, "[products]")]
    [InlineData(DatabaseType.Mysql, "`products`")]
    [InlineData(DatabaseType.Oracle, "\"products\"")]
    [InlineData(DatabaseType.Firebird, "\"products\"")]
    [InlineData(DatabaseType.Sqlite, "\"products\"")]
    public void QuoteIdentifier_UsesTheEngineQuotingStyle(DatabaseType type, string expected) =>
        SqlProviderRegistry.ResolveDialect(type).QuoteIdentifier("products").ShouldBe(expected);

    [Theory]
    [InlineData(DatabaseType.Postgresql, "@Name")]
    [InlineData(DatabaseType.Oracle, ":Name")]
    [InlineData(DatabaseType.Sqlite, "@Name")]
    public void Parameter_UsesTheEngineParameterPrefix(DatabaseType type, string expected) =>
        SqlProviderRegistry.ResolveDialect(type).Parameter("Name").ShouldBe(expected);

    [Theory]
    [InlineData(DatabaseType.Postgresql, "FALSE")]
    [InlineData(DatabaseType.SqlServer, "0")]
    [InlineData(DatabaseType.Mysql, "0")]
    [InlineData(DatabaseType.Oracle, "0")]
    [InlineData(DatabaseType.Firebird, "FALSE")]
    [InlineData(DatabaseType.Sqlite, "0")]
    public void BooleanLiteral_MatchesEngineSupport(DatabaseType type, string expected) =>
        SqlProviderRegistry.ResolveDialect(type).BooleanLiteral(false).ShouldBe(expected);

    [Fact]
    public void CaseInsensitiveLike_UsesIlikeOnPostgresAndFoldsElsewhere()
    {
        SqlProviderRegistry
            .ResolveDialect(DatabaseType.Postgresql)
            .CaseInsensitiveLike("sku", "@Pattern")
            .ShouldBe("sku ILIKE @Pattern");

        SqlProviderRegistry
            .ResolveDialect(DatabaseType.Oracle)
            .CaseInsensitiveLike("sku", ":Pattern")
            .ShouldBe("LOWER(sku) LIKE LOWER(:Pattern)");

        SqlProviderRegistry
            .ResolveDialect(DatabaseType.Firebird)
            .CaseInsensitiveLike("sku", "@Pattern")
            .ShouldBe("UPPER(sku) LIKE UPPER(@Pattern)");

        SqlProviderRegistry
            .ResolveDialect(DatabaseType.Sqlite)
            .CaseInsensitiveLike("sku", "@Pattern")
            .ShouldBe("sku LIKE @Pattern");
    }

    [Theory]
    [InlineData(DatabaseType.Postgresql, "SELECT 1 LIMIT 25 OFFSET 50")]
    [InlineData(DatabaseType.SqlServer, "SELECT 1 OFFSET 50 ROWS FETCH NEXT 25 ROWS ONLY")]
    [InlineData(DatabaseType.Mysql, "SELECT 1 LIMIT 25 OFFSET 50")]
    [InlineData(DatabaseType.Oracle, "SELECT 1 OFFSET 50 ROWS FETCH NEXT 25 ROWS ONLY")]
    [InlineData(DatabaseType.Firebird, "SELECT 1 ROWS 51 TO 75")]
    [InlineData(DatabaseType.Sqlite, "SELECT 1 LIMIT 25 OFFSET 50")]
    public void ApplyPagination_UsesTheEngineSyntax(DatabaseType type, string expected) =>
        SqlProviderRegistry
            .ResolveDialect(type)
            .ApplyPagination("SELECT 1", offset: 50, limit: 25)
            .ShouldBe(expected);

    [Fact]
    public void ApplyPagination_OnFirebird_IsOneBasedInclusive() =>
        SqlProviderRegistry
            .ResolveDialect(DatabaseType.Firebird)
            .ApplyPagination("SELECT 1", offset: 0, limit: 10)
            .ShouldBe("SELECT 1 ROWS 1 TO 10");

    [Theory]
    [InlineData(DatabaseType.Postgresql, "jsonb")]
    [InlineData(DatabaseType.SqlServer, "nvarchar(max)")]
    [InlineData(DatabaseType.Mysql, "json")]
    [InlineData(DatabaseType.Oracle, "CLOB")]
    [InlineData(DatabaseType.Firebird, "BLOB SUB_TYPE TEXT")]
    [InlineData(DatabaseType.Sqlite, "TEXT")]
    public void JsonColumnType_MatchesTheEngineNativeType(DatabaseType type, string expected) =>
        SqlProviderRegistry.ResolveDialect(type).JsonColumnType.ShouldBe(expected);

    [Theory]
    [InlineData(DatabaseType.Postgresql)]
    [InlineData(DatabaseType.SqlServer)]
    [InlineData(DatabaseType.Mysql)]
    [InlineData(DatabaseType.Oracle)]
    [InlineData(DatabaseType.Firebird)]
    [InlineData(DatabaseType.Sqlite)]
    public void EveryShippedProvider_BuildsAConnectionStringAndConnection(DatabaseType type)
    {
        var provider = SqlProviderRegistry.Resolve(type);

        var connectionString = provider.BuildConnectionString(new()
        {
            Id = "DEFAULT",
            Type = type,
            Host = "localhost",
            Port = 1234,
            Database = "webapi_template",
            Username = "user",
            Password = "secret",
        });

        connectionString.ShouldNotBeNullOrWhiteSpace();

        using var connection = provider.CreateConnection(connectionString);

        connection.ShouldNotBeNull();
    }

    [Fact]
    public void EveryShippedProvider_MapsToItsOwnDerivedContextType()
    {
        var contextTypes = SqlProviderRegistry.All
            .Where(provider => provider.IsAvailable)
            .Select(provider => provider.ContextType)
            .ToArray();

        contextTypes.Distinct().Count().ShouldBe(contextTypes.Length);
    }
}
