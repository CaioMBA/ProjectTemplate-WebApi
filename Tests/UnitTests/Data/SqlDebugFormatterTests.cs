using Dapper;
using Data.Sql.DatabaseAccess;
using Data.Sql.Providers;
using Domain.Enums;
using Domain.Models.Configuration;

namespace UnitTests.Data;

public sealed class SqlDebugFormatterTests
{
    private const string Sql = "SELECT * FROM products WHERE sku = @Sku AND is_active = @IsActive";

    [Fact]
    public void Describe_WhenDisabled_ReturnsTheSqlUntouched()
    {
        var formatter = Build(enabled: false);

        formatter.InterpolationEnabled.ShouldBeFalse();
        formatter.Describe(Sql, new { Sku = "A", IsActive = true }).ShouldBe(Sql);
    }

    [Fact]
    public void Describe_InlinesParameterValues()
    {
        var formatter = Build(enabled: true);

        formatter
            .Describe(Sql, new { Sku = "SKU-1", IsActive = true })
            .ShouldBe("SELECT * FROM products WHERE sku = 'SKU-1' AND is_active = TRUE");
    }

    [Fact]
    public void Describe_EscapesEmbeddedQuotes()
    {
        Build(enabled: true)
            .Describe("SELECT @Name", new { Name = "O'Brien" })
            .ShouldBe("SELECT 'O''Brien'");
    }

    [Fact]
    public void Describe_RendersNullAsSqlNull()
    {
        Build(enabled: true)
            .Describe("SELECT @Name", new { Name = (string?)null })
            .ShouldBe("SELECT NULL");
    }

    [Fact]
    public void Describe_UsesTheDialectBooleanLiteral()
    {
        Build(enabled: true, DatabaseType.SqlServer)
            .Describe("SELECT @Flag", new { Flag = false })
            .ShouldBe("SELECT 0");
    }

    [Fact]
    public void Describe_ExpandsSequencesIntoAnInList()
    {
        Build(enabled: true)
            .Describe("SELECT @Ids", new { Ids = new[] { 1, 2, 3 } })
            .ShouldBe("SELECT (1, 2, 3)");
    }

    [Fact]
    public void Describe_ReadsDynamicParameters()
    {
        var parameters = new DynamicParameters();
        parameters.Add("Sku", "DYN");

        Build(enabled: true)
            .Describe("SELECT @Sku", parameters)
            .ShouldBe("SELECT 'DYN'");
    }

    [Fact]
    public void Describe_LeavesUnmatchedTokensAlone()
    {
        Build(enabled: true)
            .Describe("SELECT @Missing", new { Present = 1 })
            .ShouldBe("SELECT @Missing");
    }

    [Fact]
    public void Describe_DoesNotCorruptEmailLiteralsThatContainAnAtSign()
    {
        Build(enabled: true)
            .Describe("SELECT 'a@b.com' WHERE x = @X", new { X = 1 })
            .ShouldBe("SELECT 'a@b.com' WHERE x = 1");
    }

    [Fact]
    public void Describe_HandlesOracleColonParameters()
    {
        Build(enabled: true, DatabaseType.Oracle)
            .Describe("SELECT * FROM products WHERE sku = :Sku", new { Sku = "ORA" })
            .ShouldBe("SELECT * FROM products WHERE sku = 'ORA'");
    }

    private static SqlDebugFormatter Build(
        bool enabled,
        DatabaseType provider = DatabaseType.Postgresql) =>
        new(
            SqlProviderRegistry.ResolveDialect(provider),
            new SqlDatabaseOptions { LogInterpolatedSql = enabled });
}
