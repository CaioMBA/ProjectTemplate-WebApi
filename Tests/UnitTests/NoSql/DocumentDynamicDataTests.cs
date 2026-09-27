using Amazon.DynamoDBv2.Model;
using Data.NoSql.DynamicData;
using Domain.Enums;
using Domain.Models.DynamicData;
using SortDirection = Domain.Enums.SortDirection;

namespace UnitTests.NoSql;

public sealed class DocumentDynamicDataTests
{
    private static readonly EntitySetModel _people = new(
        null,
        "people",
        [
            new FieldModel("_id", FieldKind.Text, false, MongoDynamicDataSource.ObjectIdType),
            new FieldModel("name", FieldKind.Text, true, "Text"),
            new FieldModel("age", FieldKind.Integer64, true, "Integer64"),
            new FieldModel("address", FieldKind.Document, true, "object")
            {
                Children = [new FieldModel("city", FieldKind.Text, true, "Text")],
            },
        ],
        ["_id"],
        []);

    private static readonly List<IReadOnlyDictionary<string, object?>> _rows =
    [
        Row(("_id", "a"), ("name", "Ana"), ("age", 30L), ("address", Row(("city", "Lisbon")))),
        Row(("_id", "b"), ("name", "Bruno"), ("age", 25L), ("address", Row(("city", "Porto")))),
        Row(("_id", "c"), ("name", null), ("age", 41L), ("address", null)),
    ];

    [Fact]
    public void InferenceMergesKindsAcrossSamples()
    {
        var fields = DocumentSchemaInference.Infer(
        [
            Row(("name", "Ana"), ("score", 1L), ("tags", new List<object?> { "x" }), ("meta", Row(("city", "Lisbon"))), ("_id", "1")),
            Row(("name", 5L), ("score", 2.5), ("meta", Row(("zip", 1000L))), ("_id", "2")),
        ],
            "_id");

        fields[0].Name.ShouldBe("_id");
        fields[0].IsNullable.ShouldBeFalse();
        fields.Single(field => field.Name == "name").Kind.ShouldBe(FieldKind.Json);
        fields.Single(field => field.Name == "score").Kind.ShouldBe(FieldKind.Floating);

        var tags = fields.Single(field => field.Name == "tags");
        tags.Kind.ShouldBe(FieldKind.Array);
        tags.IsNullable.ShouldBeTrue();

        var meta = fields.Single(field => field.Name == "meta");
        meta.Kind.ShouldBe(FieldKind.Document);
        meta.Children.Select(child => child.Name).ShouldBe(["city", "zip"]);
        meta.Children.ShouldAllBe(child => child.IsNullable);
    }

    [Fact]
    public void ProjectionKeepsOnlyExposedFieldsAndToleratesBadValues()
    {
        var set = _people with { Fields = [_people.Fields[0], _people.Fields[2]] };

        var row = DocumentSchemaInference.Project(set.Fields, Row(("_id", "a"), ("age", "not a number"), ("secret", "x")));

        row.Keys.ShouldBe(["_id", "age"]);
        row["age"].ShouldBeNull();
    }

    [Fact]
    public void InMemoryFilteringMatchesTheSqlSemantics()
    {
        Ids(new ConditionFilter("name", FilterOperator.Contains, "AN")).ShouldBe(["a"]);
        Ids(new ConditionFilter("name", FilterOperator.Neq, "Ana")).ShouldBe(["b", "c"]);
        Ids(new ConditionFilter("name", FilterOperator.NotContains, "an")).ShouldBe(["b", "c"]);
        Ids(new ConditionFilter("age", FilterOperator.In, new List<object?> { 25, 41 })).ShouldBe(["b", "c"]);
        Ids(new ConditionFilter("age", FilterOperator.Gte, 30)).ShouldBe(["a", "c"]);
        Ids(new ConditionFilter("address.city", FilterOperator.Like, "l%n")).ShouldBe(["a"]);
        Ids(new ConditionFilter("address.city", FilterOperator.IsNull, true)).ShouldBe(["c"]);
        Ids(new NotFilter(new OrFilter([new ConditionFilter("age", FilterOperator.Lt, 26), new ConditionFilter("age", FilterOperator.Gt, 40)]))).ShouldBe(["a"]);
    }

    [Fact]
    public void InMemorySortingPagingAndAggregates()
    {
        InMemoryQuery.Apply(_people, _rows, null, [new SortTerm("age", SortDirection.Descending)], 1, 1)
            .Single()["_id"].ShouldBe("a");

        var max = InMemoryQuery.Aggregate(_people, _rows, AggregateFunction.Max, ["age"]);
        var sum = InMemoryQuery.Aggregate(_people, _rows, AggregateFunction.Sum, ["age"]);
        var average = InMemoryQuery.Aggregate(_people, _rows, AggregateFunction.Average, ["age"]);

        max["age"].ShouldBe(41L);
        sum["age"].ShouldBe(96L);
        average["age"].ShouldBe(32.0);
    }

    [Fact]
    public void MongoFiltersUseCaseInsensitiveEscapedRegexesAndObjectIds()
    {
        var filter = MongoDynamicDataSource.Render(
            _people,
            new AndFilter(
            [
                new ConditionFilter("name", FilterOperator.StartsWith, "a.b"),
                new ConditionFilter("_id", FilterOperator.Eq, "65a1f0c2e4b0a1b2c3d4e5f6"),
                new NotFilter(new ConditionFilter("address.city", FilterOperator.Eq, "Porto")),
            ]));

        var json = MongoDynamicDataSource.ToJson(filter);

        json.ShouldContain("\"pattern\" : \"^a\\\\.b\", \"options\" : \"i\"");
        json.ShouldContain("\"$oid\" : \"65a1f0c2e4b0a1b2c3d4e5f6\"");
        json.ShouldContain("\"address.city\"");

        MongoDynamicDataSource.ToJson(MongoDynamicDataSource.Render(_people, new ConditionFilter("age", FilterOperator.In, new List<object?> { 1, 2 })))
            .ShouldBe("{ \"age\" : { \"$in\" : [1, 2] } }");
    }

    [Fact]
    public void CosmosQueriesAreParameterisedAndCaseInsensitive()
    {
        var query = CosmosQueryBuilder.Select(new DynamicQuery(
            _people,
            new AndFilter([new ConditionFilter("address.city", FilterOperator.Contains, "lis"), new ConditionFilter("age", FilterOperator.NotIn, new List<object?> { 1 })]),
            [new SortTerm("age", SortDirection.Descending)],
            5,
            10));

        query.Text.ShouldBe(
            "SELECT * FROM c WHERE (CONTAINS(c[\"address\"][\"city\"], @p0, true) AND NOT ARRAY_CONTAINS(@p1, c[\"age\"])) "
            + "ORDER BY c[\"age\"] DESC OFFSET 5 LIMIT 10");
        query.Parameters["@p0"].ShouldBe("lis");

        CosmosQueryBuilder.Count(_people, new ConditionFilter("name", FilterOperator.IsNull, true)).Text
            .ShouldBe("SELECT VALUE COUNT(1) FROM c WHERE (NOT IS_DEFINED(c[\"name\"]) OR IS_NULL(c[\"name\"]))");

        Should.Throw<InvalidOperationException>(() => CosmosQueryBuilder.Count(_people, new ConditionFilter("name\"]) OR true --", FilterOperator.Eq, "x")));
    }

    [Fact]
    public void RavenQueriesUseRqlParametersAndIdFunction()
    {
        var set = _people with { Fields = [new FieldModel("id", FieldKind.Text, false, "Text"), .. _people.Fields.Skip(1)], KeyFields = ["id"] };

        var query = RavenQueryBuilder.Select(
            set,
            new OrFilter([new ConditionFilter("name", FilterOperator.EndsWith, "na"), new ConditionFilter("id", FilterOperator.Eq, "people/1")]),
            [new SortTerm("age", SortDirection.Ascending)]);

        query.Text.ShouldBe(
            "from 'people' as d where (regex(d.name, $p0) or id() = $p1) order by d.age as long "
            + "select { Id: id(d), Json: JSON.stringify(d) }");
        query.Parameters["p0"].ShouldBe("(?i)na$");

        RavenQueryBuilder.Count(set, new NotFilter(new ConditionFilter("address.city", FilterOperator.Eq, "Porto"))).Text
            .ShouldBe("from 'people' as d where (true and not d.address.city = $p0)");
    }

    [Fact]
    public void DynamoAttributeValuesBecomePlainValues()
    {
        DynamoDynamicDataSource.Plain(new AttributeValue { N = "42" }).ShouldBe(42L);
        DynamoDynamicDataSource.Plain(new AttributeValue { N = "4.5" }).ShouldBe(4.5m);
        DynamoDynamicDataSource.Plain(new AttributeValue { S = "x" }).ShouldBe("x");
        DynamoDynamicDataSource.Plain(new AttributeValue { NULL = true }).ShouldBeNull();
        DynamoDynamicDataSource.Plain(new AttributeValue { BOOL = true }).ShouldBe(true);

        var map = DynamoDynamicDataSource.Plain(new AttributeValue { M = new Dictionary<string, AttributeValue> { ["city"] = new() { S = "Lisbon" } } });
        map.ShouldBeAssignableTo<IReadOnlyDictionary<string, object?>>()!["city"].ShouldBe("Lisbon");
    }

    private static List<string> Ids(FilterNode filter) =>
        InMemoryQuery.Where(_people, _rows, filter).Select(row => (string)row["_id"]!).ToList();

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
