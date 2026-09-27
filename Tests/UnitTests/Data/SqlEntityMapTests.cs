using Data.Sql.DatabaseAccess;
using Data.Sql.Providers;
using Domain.Attributes;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace UnitTests.Data;

public sealed class SqlEntityMapTests
{
    private static readonly SqlEntityMapFactory _factory =
        new(new ModelBuilder().FinalizeModel());

    [Fact]
    public void Convention_DerivesSnakeCaseColumnsAndAPluralTableName()
    {
        var map = _factory.For<WarehouseBin>();

        map.Source.ShouldBe(SqlMapSource.Convention);
        map.TableName.ShouldBe("warehouse_bins");

        map.Columns.Select(column => column.ColumnName).ShouldBe(
            ["id", "bin_code", "shelf_number", "is_full"],
            ignoreOrder: true);
    }

    [Fact]
    public void Convention_TreatsIdAsTheKeyWithoutAnAttribute()
    {
        var map = _factory.For<WarehouseBin>();

        map.SingleKeyColumn.ColumnName.ShouldBe("id");
        map.UpdatableColumns.ShouldNotContain(column => column.ColumnName == "id");
        map.InsertableColumns.ShouldContain(column => column.ColumnName == "id");
    }

    [Fact]
    public void Attributes_OverrideTheTableAndColumnNames()
    {
        var map = _factory.For<LegacyOrder>();

        map.TableName.ShouldBe("ORDERS_LEGACY");
        map.Schema.ShouldBe("dbo");

        map.Columns.Select(column => column.ColumnName).ShouldBe(
            ["ORDER_ID", "CUSTOMER_REF"],
            ignoreOrder: true);

        map.SingleKeyColumn.ColumnName.ShouldBe("ORDER_ID");
    }

    [Fact]
    public void SqlIgnore_RemovesAPropertyFromEveryStatement()
    {
        var map = _factory.For<LegacyOrder>();

        map.Columns.ShouldNotContain(column => column.PropertyPath == "ComputedTotal");
    }

    [Fact]
    public void DatabaseGeneratedKey_IsExcludedFromInserts()
    {
        var map = _factory.For<TicketWithIdentity>();

        map.InsertableColumns.ShouldNotContain(column => column.ColumnName == "id");
        map.SingleKeyColumn.IsDatabaseGenerated.ShouldBeTrue();
    }

    [Fact]
    public void GetValue_ReadsThroughToThePropertyValue()
    {
        var map = _factory.For<WarehouseBin>();

        var bin = new WarehouseBin { Id = Guid.Empty, BinCode = "A-12", ShelfNumber = 3, IsFull = true };

        var column = map.Columns.Single(candidate => candidate.ColumnName == "bin_code");

        column.GetValue(bin).ShouldBe("A-12");
    }

    [Fact]
    public void SingleKeyColumn_ThrowsWhenThereIsNoUsableKey()
    {
        var map = _factory.For<KeylessReading>();

        Should.Throw<InvalidOperationException>(() => map.SingleKeyColumn)
            .Message.ShouldContain("exactly one");
    }

    [Theory]
    [InlineData(DatabaseType.Postgresql, "\"warehouse_bins\"", "@Id")]
    [InlineData(DatabaseType.SqlServer, "[warehouse_bins]", "@Id")]
    [InlineData(DatabaseType.Mysql, "`warehouse_bins`", "@Id")]
    [InlineData(DatabaseType.Oracle, "\"warehouse_bins\"", ":Id")]
    public void CrudStatements_UseTheDialectQuotingAndParameterPrefix(
        DatabaseType provider,
        string quotedTable,
        string keyParameter)
    {
        var statements = new SqlCrudStatementFactory(SqlProviderRegistry.ResolveDialect(provider))
            .For(_factory.For<WarehouseBin>());

        statements.Select.ShouldStartWith("SELECT ");
        statements.Select.ShouldContain($"FROM {quotedTable}");
        statements.Select.ShouldEndWith($"= {keyParameter}");

        statements.Insert.ShouldStartWith($"INSERT INTO {quotedTable} (");
        statements.Update.ShouldStartWith($"UPDATE {quotedTable} SET ");
        statements.DeleteById.ShouldBe($"DELETE FROM {quotedTable} WHERE \"id\" = {keyParameter}"
            .Replace("\"id\"", QuoteFor(provider, "id"), StringComparison.Ordinal));
    }

    [Fact]
    public void CrudStatements_QualifyTheTableWithItsSchema()
    {
        var statements = new SqlCrudStatementFactory(
                SqlProviderRegistry.ResolveDialect(DatabaseType.SqlServer))
            .For(_factory.For<LegacyOrder>());

        statements.SelectAll.ShouldContain("[dbo].[ORDERS_LEGACY]");
    }

    [Fact]
    public void CrudStatements_UpdateNeverAssignsTheKey()
    {
        var statements = new SqlCrudStatementFactory(
                SqlProviderRegistry.ResolveDialect(DatabaseType.Postgresql))
            .For(_factory.For<WarehouseBin>());

        var assignments = statements.Update[..statements.Update.IndexOf(" WHERE ", StringComparison.Ordinal)];

        assignments.ShouldNotContain("\"id\" =");
    }

    private static string QuoteFor(DatabaseType provider, string name) =>
        SqlProviderRegistry.ResolveDialect(provider).QuoteIdentifier(name);

    internal sealed class WarehouseBin
    {
        public Guid Id { get; set; }

        public required string BinCode { get; set; }

        public int ShelfNumber { get; set; }

        public bool IsFull { get; set; }
    }

    [SqlTable("ORDERS_LEGACY", Schema = "dbo")]
    internal sealed class LegacyOrder
    {
        [SqlKey]
        [SqlColumn("ORDER_ID")]
        public long OrderNumber { get; set; }

        [SqlColumn("CUSTOMER_REF")]
        public required string CustomerReference { get; set; }

        [SqlIgnore]
        public decimal ComputedTotal { get; set; }
    }

    internal sealed class TicketWithIdentity
    {
        [SqlKey(DatabaseGenerated = true)]
        public int Id { get; set; }

        public required string Subject { get; set; }
    }

    internal sealed class KeylessReading
    {
        public double Value { get; set; }

        public DateTime TakenAtUtc { get; set; }
    }
}
