using Data.Sql.DynamicData;
using Data.Sql.Providers;
using Domain.Enums;

namespace UnitTests.Data;

public sealed class SqlCatalogReaderTests
{
    [Theory]
    [InlineData("timestamp with time zone", FieldKind.TimestampOffset)]
    [InlineData("timestamp without time zone", FieldKind.Timestamp)]
    [InlineData("uuid", FieldKind.Uuid)]
    [InlineData("jsonb", FieldKind.Json)]
    [InlineData("interval", FieldKind.Unknown)]
    [InlineData("integer", FieldKind.Integer32)]
    public void PostgresqlTypesMapToFieldKinds(string dataType, FieldKind expected) =>
        new PostgresqlCatalogReader().MapColumn("c", true, new SqlCatalogReader.CatalogColumnType(dataType)).Kind.ShouldBe(expected);

    [Fact]
    public void PostgresqlCitextIsText() =>
        new PostgresqlCatalogReader()
            .MapColumn("c", true, new SqlCatalogReader.CatalogColumnType("USER-DEFINED", extra: "citext"))
            .Kind.ShouldBe(FieldKind.Text);

    [Fact]
    public void SqlServerLegacyTextIsLongText()
    {
        var field = new SqlServerCatalogReader().MapColumn("c", true, new SqlCatalogReader.CatalogColumnType("ntext"));

        field.Kind.ShouldBe(FieldKind.Text);
        field.IsLongText.ShouldBeTrue();
    }

    [Theory]
    [InlineData("tinyint", "tinyint(1)", null, FieldKind.Flag)]
    [InlineData("int", "int unsigned", null, FieldKind.Integer64)]
    [InlineData("char", "char(36)", 36, FieldKind.Uuid)]
    [InlineData("datetime", "datetime(6)", null, FieldKind.Timestamp)]
    public void MysqlTypesMapToFieldKinds(string dataType, string columnType, int? length, FieldKind expected) =>
        new MysqlCatalogReader()
            .MapColumn("c", true, new SqlCatalogReader.CatalogColumnType(dataType, length: length, extra: columnType))
            .Kind.ShouldBe(expected);

    [Theory]
    [InlineData("NUMBER", 9, 0, null, FieldKind.Integer32)]
    [InlineData("NUMBER", 18, 0, null, FieldKind.Integer64)]
    [InlineData("NUMBER", 18, 2, null, FieldKind.Fixed)]
    [InlineData("RAW", null, null, 16, FieldKind.Uuid)]
    [InlineData("TIMESTAMP(6) WITH TIME ZONE", null, null, null, FieldKind.TimestampOffset)]
    [InlineData("TIMESTAMP(6) WITH LOCAL TIME ZONE", null, null, null, FieldKind.Timestamp)]
    [InlineData("TIMESTAMP(7)", null, null, null, FieldKind.Timestamp)]
    public void OracleTypesMapToFieldKinds(string dataType, int? precision, int? scale, int? length, FieldKind expected) =>
        new OracleCatalogReader()
            .MapColumn("c", true, new SqlCatalogReader.CatalogColumnType(dataType, precision, scale, length))
            .Kind.ShouldBe(expected);

    [Theory]
    [InlineData("14", 16, null, "1", FieldKind.Uuid)]
    [InlineData("37", 100, null, "4", FieldKind.Text)]
    [InlineData("16", null, -2, null, FieldKind.Fixed)]
    [InlineData("261", null, null, null, FieldKind.Binary)]
    [InlineData("29", null, null, null, FieldKind.TimestampOffset)]
    public void FirebirdTypeCodesMapToFieldKinds(string code, int? length, int? scale, string? charset, FieldKind expected) =>
        new FirebirdCatalogReader()
            .MapColumn("c", true, new SqlCatalogReader.CatalogColumnType(code, scale: scale, length: length, extra: charset))
            .Kind.ShouldBe(expected);

    [Theory]
    [InlineData("INTEGER", FieldKind.Integer64)]
    [InlineData("TEXT", FieldKind.Text)]
    [InlineData("REAL", FieldKind.Floating)]
    [InlineData("BLOB", FieldKind.Binary)]
    [InlineData("", FieldKind.Unknown)]
    public void SqliteAffinitiesMapToFieldKinds(string dataType, FieldKind expected) =>
        new SqliteCatalogReader().MapColumn("c", true, new SqlCatalogReader.CatalogColumnType(dataType)).Kind.ShouldBe(expected);

    [Fact]
    public void AssembleOrdersColumnsKeysAndResolvesImplicitForeignKeyTargets()
    {
        var reader = new SqliteCatalogReader();

        var sets = reader.Assemble(
            columns:
            [
                [null, "orders", "customer_id", 1L, 0L, "INTEGER", null, null, null, null, null],
                [null, "orders", "id", 0L, 0L, "INTEGER", null, null, null, null, null],
                [null, "customers", "id", 0L, 0L, "INTEGER", null, null, null, null, null],
            ],
            keys:
            [
                [null, "orders", "id", 1L],
                [null, "customers", "id", 1L],
            ],
            foreignKeys:
            [
                ["orders_fk_0", null, "orders", "customer_id", 0L, null, "customers", null],
            ]);

        sets.Select(set => set.Name).ShouldBe(["customers", "orders"]);

        var orders = sets.Single(set => set.Name == "orders");
        orders.Fields.Select(field => field.Name).ShouldBe(["id", "customer_id"]);
        orders.KeyFields.ShouldBe(["id"]);

        var foreignKey = orders.ForeignKeys.ShouldHaveSingleItem();
        foreignKey.Columns.ShouldBe(["customer_id"]);
        foreignKey.TargetTable.ShouldBe("customers");
        foreignKey.TargetColumns.ShouldBe(["id"]);
    }

    [Fact]
    public void EveryAvailableProviderShipsACatalogReader()
    {
        foreach (var type in Enum.GetValues<DatabaseType>().Where(type => type.Family() == DatabaseFamily.Relational && type != DatabaseType.Mariadb))
        {
            var provider = SqlProviderRegistry.Resolve(type);

            provider.CatalogReader.ShouldNotBeNull($"{type} must be introspectable.");
        }
    }
}
