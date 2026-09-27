using Data.Sql.DynamicData;
using Data.Sql.Providers;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.DynamicData;
using SortDirection = Domain.Enums.SortDirection;

namespace UnitTests.Data;

public sealed class SqlDynamicQueryBuilderTests
{
    private static readonly EntitySetModel _customers = new(
        "public",
        "customers",
        [
            new FieldModel("id", FieldKind.Integer32, false, "integer"),
            new FieldModel("name", FieldKind.Text, false, "text"),
            new FieldModel("city", FieldKind.Text, true, "text"),
            new FieldModel("credit", FieldKind.Fixed, true, "numeric"),
            new FieldModel("active", FieldKind.Flag, false, "boolean"),
            new FieldModel("notes", FieldKind.Text, true, "ntext", IsLongText: true),
            new FieldModel("payload", FieldKind.Json, true, "jsonb"),
        ],
        ["id"],
        []);

    [Fact]
    public void SelectRendersFilterSortAndPagingWithParametersOnly()
    {
        var builder = new SqlDynamicQueryBuilder(new PostgresqlDialect());

        var statement = builder.Select(new DynamicQuery(
            _customers,
            new AndFilter(
            [
                new ConditionFilter("name", FilterOperator.Contains, "an'a"),
                new OrFilter([new ConditionFilter("city", FilterOperator.In, new List<object?> { "Lisbon", "Porto" })]),
                new NotFilter(new ConditionFilter("active", FilterOperator.Eq, false)),
            ]),
            [new SortTerm("name", SortDirection.Descending)],
            Skip: 10,
            Take: 5));

        statement.Sql.ShouldBe(
            "SELECT t.\"id\", t.\"name\", t.\"city\", t.\"credit\", t.\"active\", t.\"notes\", t.\"payload\" "
            + "FROM \"public\".\"customers\" t "
            + "WHERE (t.\"name\" ILIKE @p0 ESCAPE '!' AND (t.\"city\" IN (@p1, @p2)) AND NOT (t.\"active\" = @p3)) "
            + "ORDER BY t.\"name\" DESC, t.\"id\" ASC LIMIT 5 OFFSET 10");

        statement.Parameters["p0"].ShouldBe("%an'a%");
        statement.Parameters["p1"].ShouldBe("Lisbon");
        statement.Parameters["p3"].ShouldBe(false);
    }

    [Theory]
    [InlineData("50%_off!", false, "50!%!_off!!")]
    [InlineData("[a]", true, "![a]")]
    [InlineData("[a]", false, "[a]")]
    public void LikePatternsEscapeWildcards(string value, bool brackets, string expected) =>
        SqlLikePatterns.Escape(value, brackets).ShouldBe(expected);

    [Fact]
    public void OracleUsesColonParametersAndLowerLike()
    {
        var statement = new SqlDynamicQueryBuilder(new OracleDialect()).Select(new DynamicQuery(
            _customers with { Schema = null },
            new ConditionFilter("name", FilterOperator.StartsWith, "a_b"),
            [],
            0,
            25));

        statement.Sql.ShouldContain("WHERE LOWER(t.\"name\") LIKE LOWER(:p0) ESCAPE '!'");
        statement.Sql.ShouldEndWith("ORDER BY t.\"id\" ASC OFFSET 0 ROWS FETCH NEXT 25 ROWS ONLY");
        statement.Parameters["p0"].ShouldBe("a!_b%");
    }

    [Fact]
    public void SqlServerEscapesBracketsAndAlwaysOrders()
    {
        var keyless = new EntitySetModel("dbo", "log", [new FieldModel("body", FieldKind.Text, true, "ntext", IsLongText: true)], [], []);

        var builder = new SqlDynamicQueryBuilder(new SqlServerDialect());
        var statement = builder.Select(new DynamicQuery(keyless, new ConditionFilter("body", FilterOperator.EndsWith, "[x]"), [], 0, 10));

        statement.Sql.ShouldBe(
            "SELECT t.[body] FROM [dbo].[log] t WHERE t.[body] LIKE @p0 ESCAPE '!' "
            + "ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY");
        statement.Parameters["p0"].ShouldBe("%![x]");
    }

    [Fact]
    public void FirebirdPagesWithRows()
    {
        var statement = new SqlDynamicQueryBuilder(new FirebirdDialect())
            .Select(new DynamicQuery(_customers with { Schema = null }, null, [], 20, 10));

        statement.Sql.ShouldEndWith("ORDER BY t.\"id\" ASC ROWS 21 TO 30");
    }

    [Fact]
    public void NegatedOperatorsKeepNullRows()
    {
        var builder = new SqlDynamicQueryBuilder(new SqliteDialect());

        Where(builder, new ConditionFilter("city", FilterOperator.Neq, "Porto"))
            .ShouldBe("(t.\"city\" <> @p0 OR t.\"city\" IS NULL)");
        Where(builder, new ConditionFilter("city", FilterOperator.NotContains, "x"))
            .ShouldBe("(t.\"city\" IS NULL OR NOT (t.\"city\" LIKE @p0 ESCAPE '!'))");
        Where(builder, new ConditionFilter("city", FilterOperator.NotIn, new object?[] { "a", null }))
            .ShouldBe("(NOT (t.\"city\" IN (@p0)) AND t.\"city\" IS NOT NULL)");
        Where(builder, new ConditionFilter("city", FilterOperator.In, Array.Empty<object>()))
            .ShouldBe("1 = 0");
        Where(builder, new ConditionFilter("city", FilterOperator.Eq, null))
            .ShouldBe("t.\"city\" IS NULL");
        Where(builder, new ConditionFilter("city", FilterOperator.IsNull, false))
            .ShouldBe("t.\"city\" IS NOT NULL");
        Where(builder, new OrFilter([]))
            .ShouldBe("1 = 0");
    }

    [Fact]
    public void RequestsCanOnlyReferenceIntrospectedColumnsAndAllowedOperators()
    {
        var builder = new SqlDynamicQueryBuilder(new PostgresqlDialect());

        Should.Throw<InvalidOperationException>(() => Where(builder, new ConditionFilter("name\" OR 1=1 --", FilterOperator.Eq, "x")));
        Should.Throw<InvalidOperationException>(() => Where(builder, new ConditionFilter("notes", FilterOperator.Eq, "x")));
        Should.Throw<InvalidOperationException>(() => Where(builder, new ConditionFilter("payload", FilterOperator.Contains, "x")));
        Should.Throw<InvalidOperationException>(() => builder.Select(new DynamicQuery(_customers, null, [new SortTerm("notes", SortDirection.Ascending)], 0, 1)));
        Should.Throw<InvalidOperationException>(() => builder.Select(new DynamicQuery(
            _customers with { Name = "bad\"table" }, null, [], 0, 1)));
        Should.Throw<InvalidOperationException>(() => Where(builder, new ConditionFilter("city", FilterOperator.In, Enumerable.Repeat<object?>("x", 501).ToList())));
    }

    [Fact]
    public void HostileValuesNeverReachTheSqlText()
    {
        const string hostile = "'; DROP TABLE customers; --";

        var statement = new SqlDynamicQueryBuilder(new MysqlDialect()).Select(new DynamicQuery(
            _customers with { Schema = null },
            new AndFilter([new ConditionFilter("name", FilterOperator.Eq, hostile), new ConditionFilter("city", FilterOperator.Like, hostile)]),
            [],
            0,
            1));

        statement.Sql.ShouldNotContain("DROP");
        statement.Parameters.Values.ShouldContain(hostile);
    }

    [Fact]
    public void AggregatesAliasEachFieldAndCastAverages()
    {
        var builder = new SqlDynamicQueryBuilder(new MysqlDialect());

        builder.Aggregate(_customers with { Schema = null }, null, AggregateFunction.Average, ["credit"]).Sql
            .ShouldBe("SELECT AVG(CAST(t.`credit` AS DOUBLE)) AS `credit` FROM `customers` t");

        new SqlDynamicQueryBuilder(new PostgresqlDialect())
            .Aggregate(_customers, new ConditionFilter("active", FilterOperator.Eq, true), AggregateFunction.Max, ["name", "credit"]).Sql
            .ShouldBe("SELECT MAX(t.\"name\") AS \"name\", MAX(t.\"credit\") AS \"credit\" FROM \"public\".\"customers\" t WHERE t.\"active\" = @p0");

        Should.Throw<InvalidOperationException>(() => builder.Aggregate(_customers, null, AggregateFunction.Sum, ["name"]));
    }

    [Fact]
    public void CountKeepsTheFilter() =>
        new SqlDynamicQueryBuilder(new PostgresqlDialect())
            .Count(_customers, new ConditionFilter("credit", FilterOperator.Gte, 10m)).Sql
            .ShouldBe("SELECT COUNT(*) FROM \"public\".\"customers\" t WHERE t.\"credit\" >= @p0");

    [Fact]
    public void RelatedRowsArePagedPerParentWithRowNumber()
    {
        var orders = new EntitySetModel(
            null,
            "orders",
            [new FieldModel("id", FieldKind.Integer64, false, "INTEGER"), new FieldModel("customer_id", FieldKind.Integer64, false, "INTEGER")],
            ["id"],
            []);

        var statements = new SqlDynamicQueryBuilder(new SqliteDialect(), maxInValues: 2).Related(new RelatedQuery(
            orders,
            "customer_id",
            [1L, 2L, 3L, 1L],
            new ConditionFilter("id", FilterOperator.Gt, 0L),
            [],
            Skip: 1,
            Take: 2));

        statements.Count.ShouldBe(2);
        statements[0].Sql.ShouldBe(
            "SELECT x.\"id\", x.\"customer_id\" FROM (SELECT t.\"id\", t.\"customer_id\", "
            + "ROW_NUMBER() OVER (PARTITION BY t.\"customer_id\" ORDER BY t.\"id\" ASC) AS \"__rn\" "
            + "FROM \"orders\" t WHERE t.\"customer_id\" IN (@p0, @p1) AND t.\"id\" > @p2) x "
            + "WHERE x.\"__rn\" > 1 AND x.\"__rn\" <= 3 ORDER BY x.\"customer_id\", x.\"__rn\"");
    }

    [Fact]
    public void ByValuesChunksLongKeyLists()
    {
        var statements = new SqlDynamicQueryBuilder(new PostgresqlDialect(), maxInValues: 2)
            .ByValues(_customers, "id", [1, 2, 3]);

        statements.Count.ShouldBe(2);
        statements[1].Sql.ShouldEndWith("WHERE t.\"id\" IN (@p0)");
    }

    private static string Where(SqlDynamicQueryBuilder builder, FilterNode filter)
    {
        var sql = builder.Count(_customers with { Schema = null }, filter).Sql;

        return sql[(sql.IndexOf(" WHERE ", StringComparison.Ordinal) + 7)..];
    }

    [Fact]
    public void EveryDialectEscapesLikeValues()
    {
        ISqlDialect[] dialects =
        [
            new PostgresqlDialect(), new SqlServerDialect(), new MysqlDialect(), new MariadbDialect(),
            new OracleDialect(), new FirebirdDialect(), new SqliteDialect(),
        ];

        foreach (var dialect in dialects)
        {
            dialect.EscapeLikePattern("a%b").ShouldBe("a!%b", dialect.ProviderType.ToString());
        }
    }
}
