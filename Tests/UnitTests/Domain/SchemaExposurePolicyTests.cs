using Domain.Abstractions;
using Domain.Enums;
using Domain.Models.Configuration;
using Domain.Models.DynamicData;
using Domain.Setup;

namespace UnitTests.Domain;

public sealed class SchemaExposurePolicyTests
{
    [Theory]
    [InlineData("customer_id", "customerId")]
    [InlineData("CustomerID", "customerId")]
    [InlineData("__EFMigrationsHistory", "efMigrationsHistory")]
    [InlineData("DEFAULT", "default")]
    [InlineData("2fa_code", "_2faCode")]
    [InlineData("preço", "preO")]
    [InlineData("---", "field")]
    public void CamelProducesValidGraphQlNames(string identifier, string expected)
    {
        var name = GraphQlNames.Camel(identifier);

        name.ShouldBe(expected);
        GraphQlNames.IsValid(name).ShouldBeTrue();
    }

    [Theory]
    [InlineData("order_lines", "OrderLines")]
    [InlineData("reporting-db", "ReportingDb")]
    [InlineData("HTTPServer", "HttpServer")]
    public void PascalSplitsOnSeparatorsAndCaseBoundaries(string identifier, string expected) =>
        GraphQlNames.Pascal(identifier).ShouldBe(expected);

    [Fact]
    public void TheNameScopeSuffixesCollisionsAndReservesDerivedNames()
    {
        var scope = new GraphQlNameScope(["products"]);

        scope.Claim("products").ShouldBe("products2");
        scope.Claim("Orders", ["Filter"]).ShouldBe("Orders");
        scope.IsClaimed("OrdersFilter").ShouldBeTrue();
        scope.Claim("OrdersFilter").ShouldBe("OrdersFilter2");
    }

    [Fact]
    public void BuiltInSystemTablesAndSchemasAreNeverExposed()
    {
        var options = new GraphQlAutoSchemaOptions();

        SchemaExposurePolicy.IsTableExcluded("DEFAULT", Set("__EFMigrationsHistory"), options).ShouldBeTrue();
        SchemaExposurePolicy.IsTableExcluded("DEFAULT", Set("outbox_messages"), options).ShouldBeTrue();
        SchemaExposurePolicy.IsTableExcluded("DEFAULT", Set("hangfire.job"), options).ShouldBeTrue();
        SchemaExposurePolicy.IsTableExcluded("DEFAULT", Set("customers", "pg_catalog"), options).ShouldBeTrue();
        SchemaExposurePolicy.IsTableExcluded("DEFAULT", Set("customers", "HangFire"), options).ShouldBeTrue();
        SchemaExposurePolicy.IsTableExcluded("DEFAULT", Set("customers", "public"), options).ShouldBeFalse();
    }

    [Fact]
    public void ExclusionPatternsMatchByNameQualifiedNameAndDatabase()
    {
        var options = new GraphQlAutoSchemaOptions
        {
            ExcludeTables = ["audit_*", "REPORTING:public.secrets"],
            ExcludeColumns = ["*.password*", "customers.ssn"],
            ExcludeDatabases = ["LEGACY*"],
        };

        SchemaExposurePolicy.IsTableExcluded("DEFAULT", Set("audit_log", "public"), options).ShouldBeTrue();
        SchemaExposurePolicy.IsTableExcluded("DEFAULT", Set("secrets", "public"), options).ShouldBeFalse();
        SchemaExposurePolicy.IsTableExcluded("REPORTING", Set("secrets", "public"), options).ShouldBeTrue();
        SchemaExposurePolicy.IsColumnExcluded("DEFAULT", Set("users"), "password_hash", options).ShouldBeTrue();
        SchemaExposurePolicy.IsColumnExcluded("DEFAULT", Set("customers"), "ssn", options).ShouldBeTrue();
        SchemaExposurePolicy.IsColumnExcluded("DEFAULT", Set("orders"), "ssn", options).ShouldBeFalse();
        SchemaExposurePolicy.IsDatabaseExcluded("legacy_crm", options).ShouldBeTrue();
    }

    [Fact]
    public void OperatorsFollowTheFieldKind()
    {
        SchemaExposurePolicy.OperatorsFor(Field("name", FieldKind.Text))
            .ShouldContain(FilterOperator.Contains);
        SchemaExposurePolicy.OperatorsFor(Field("notes", FieldKind.Text) with { IsLongText = true })
            .ShouldNotContain(FilterOperator.Eq);
        SchemaExposurePolicy.OperatorsFor(Field("total", FieldKind.Fixed))
            .ShouldBe([FilterOperator.Eq, FilterOperator.Neq, FilterOperator.In, FilterOperator.NotIn, FilterOperator.IsNull, FilterOperator.Gt, FilterOperator.Gte, FilterOperator.Lt, FilterOperator.Lte]);
        SchemaExposurePolicy.OperatorsFor(Field("active", FieldKind.Flag))
            .ShouldNotContain(FilterOperator.Gt);
        SchemaExposurePolicy.OperatorsFor(Field("payload", FieldKind.Json))
            .ShouldBe([FilterOperator.IsNull]);
    }

    [Fact]
    public void AggregatesRespectKindAndCapabilities()
    {
        var number = Field("total", FieldKind.Fixed);
        var flag = Field("active", FieldKind.Flag);
        var noAggregates = new DataSourceCapabilities(Relations: false, MinMax: false, SumAverage: false);

        SchemaExposurePolicy.SupportsSumAverage(number, DataSourceCapabilities.Relational).ShouldBeTrue();
        SchemaExposurePolicy.SupportsMinMax(flag, DataSourceCapabilities.Relational).ShouldBeFalse();
        SchemaExposurePolicy.SupportsMinMax(number, noAggregates).ShouldBeFalse();
        SchemaExposurePolicy.SumKind(FieldKind.Integer32).ShouldBe(FieldKind.Integer64);
    }

    [Fact]
    public void ExposeBuildsNamesKeysAndBothRelationDirections()
    {
        var exposure = SchemaExposurePolicy.Expose(
            [Shop()],
            new GraphQlAutoSchemaOptions { ExcludeColumns = ["customers.secret"] },
            ["product", "products"],
            ["Query"]);

        var source = exposure.Sources.ShouldHaveSingleItem();
        source.FieldName.ShouldBe("default");
        source.TypeName.ShouldBe("DefaultDatabase");

        var customers = source.Sets.Single(set => set.Model.Name == "customers");
        customers.TypeName.ShouldBe("DefaultCustomers");
        customers.ListField.ShouldBe("customers");
        customers.ByKeyField.ShouldBe("customersById");
        customers.Fields.Select(field => field.Name).ShouldBe(["id", "fullName"]);
        customers.Model.Fields.ShouldNotContain(field => field.Name == "secret");
        customers.Relations.ShouldHaveSingleItem().ShouldBe(
            new ExposedRelation("orders", RelationKind.OneToMany, "DefaultOrders", "id", "customer_id"));

        var orders = source.Sets.Single(set => set.Model.Name == "orders");
        orders.Relations.ShouldHaveSingleItem().ShouldBe(
            new ExposedRelation("customer", RelationKind.ManyToOne, "DefaultCustomers", "customer_id", "id"));

        source.Sets.ShouldNotContain(set => set.Model.Name == "outbox_messages");
    }

    [Fact]
    public void SameNamedTablesInTwoSchemasArePrefixedWithTheirSchema()
    {
        var schema = new DataSourceSchema(
            "DEFAULT",
            DatabaseType.Postgresql,
            [Set("orders", "public", Field("id", FieldKind.Integer32)), Set("orders", "sales", Field("id", FieldKind.Integer32))],
            DataSourceCapabilities.Relational);

        var source = SchemaExposurePolicy.Expose([schema], new GraphQlAutoSchemaOptions(), [], []).Sources.Single();

        source.Sets.Select(set => set.ListField).ShouldBe(["publicOrders", "salesOrders"]);
    }

    [Fact]
    public void ADatabaseIdCollidingWithAnExistingRootFieldIsSuffixed()
    {
        var schema = new DataSourceSchema("products", DatabaseType.Sqlite, [Set("items")], DataSourceCapabilities.Relational);
        var empty = new DataSourceSchema("JOBS", DatabaseType.MongoDb, [Set("hangfire.job")], DataSourceCapabilities.Relational);

        var exposure = SchemaExposurePolicy.Expose([schema, empty], new GraphQlAutoSchemaOptions(), ["products"], []);

        exposure.Sources.Single().FieldName.ShouldBe("products2");
        exposure.Notes.ShouldContain(note => note.Contains("'JOBS' has no exposable", StringComparison.Ordinal));
    }

    [Fact]
    public void CompositeKeysAndUnknownColumnsProduceNotesInsteadOfFields()
    {
        var lines = new EntitySetModel(
            null,
            "order_lines",
            [Field("order_id", FieldKind.Integer32), Field("line", FieldKind.Integer32), Field("shape", FieldKind.Unknown)],
            ["order_id", "line"],
            [new ForeignKeyModel("fk_pair", ["order_id", "line"], null, "orders", ["id", "line"])]);

        var exposure = SchemaExposurePolicy.Expose(
            [new DataSourceSchema("DEFAULT", DatabaseType.Sqlite, [lines], DataSourceCapabilities.Relational)],
            new GraphQlAutoSchemaOptions(),
            [],
            []);

        var set = exposure.Sources.Single().Sets.Single();
        set.ByKeyField.ShouldBeNull();
        set.Fields.ShouldNotContain(field => field.Path == "shape");
        exposure.Notes.ShouldContain(note => note.Contains("unsupported type", StringComparison.Ordinal));
        exposure.Notes.ShouldContain(note => note.Contains("single-column key", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedDocumentFieldsGetTheirOwnTypesAndDottedPaths()
    {
        var address = new FieldModel("address", FieldKind.Document, true, "object")
        {
            Children = [Field("city", FieldKind.Text)],
        };

        var schema = new DataSourceSchema(
            "DOCS",
            DatabaseType.MongoDb,
            [Set("people", null, Field("_id", FieldKind.Text), address)],
            new DataSourceCapabilities(Relations: false, MinMax: true, SumAverage: true));

        var set = SchemaExposurePolicy.Expose([schema], new GraphQlAutoSchemaOptions(), [], []).Sources.Single().Sets.Single();

        var nested = set.Fields.Single(field => field.Name == "address");
        nested.ObjectTypeName.ShouldBe("DocsPeopleAddress");
        nested.Children.ShouldHaveSingleItem().Path.ShouldBe("address.city");
    }

    [Fact]
    public void TheNothingExposedMessageNamesEveryDatabaseAndWhy()
    {
        var message = SchemaExposurePolicy.NothingExposedMessage(
            ["DEFAULT", "JOBS"],
            new Dictionary<string, string> { ["DEFAULT"] = "introspection failed (timeout)" });

        message.ShouldContain("DEFAULT: introspection failed (timeout)");
        message.ShouldContain("JOBS: not exposed");
        SchemaExposurePolicy.NothingExposedMessage([], new Dictionary<string, string>()).ShouldContain("Settings:Databases is empty");
    }

    [Theory]
    [InlineData(DatabaseExposure.Exposed, "exposed")]
    [InlineData(DatabaseExposure.Excluded, "excluded")]
    [InlineData(DatabaseExposure.Unreachable, "unreachable")]
    [InlineData(DatabaseExposure.NothingToExpose, "nothing to expose")]
    [InlineData(DatabaseExposure.NoDataSource, "no data source")]
    public void ExposureReasonsAreShortCategories(DatabaseExposure exposure, string reason) =>
        SchemaExposurePolicy.ReasonOf(exposure).ShouldBe(reason);

    [Fact]
    public void TheStatusFieldAndTypesAreReserved()
    {
        var schema = new DataSourceSchema("autoSchemaStatus", DatabaseType.Sqlite, [Set("items")], DataSourceCapabilities.Relational);

        var source = SchemaExposurePolicy.Expose([schema], new GraphQlAutoSchemaOptions(), [], []).Sources.Single();

        source.FieldName.ShouldBe("autoSchemaStatus2");
        new DatabaseExposureStatus("DEFAULT", DatabaseExposure.Exposed).Exposed.ShouldBeTrue();
    }

    [Fact]
    public void AutoSchemaLimitsAreValidated()
    {
        var options = new ApiOptions();
        options.GraphQlServer.AutoSchema.MaxPageSize = 0;
        options.GraphQlServer.AutoSchema.MaxInValues = 5000;
        options.GraphQlServer.AutoSchema.ExcludeTables = [" "];

        var errors = ApiOptionsPreparation.Validate(options);

        errors.ShouldContain(error => error.Contains("AutoSchema:MaxPageSize", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Contains("AutoSchema:MaxInValues", StringComparison.Ordinal));
        errors.ShouldContain(error => error.Contains("must not be blank", StringComparison.Ordinal));
    }

    [Fact]
    public void FilterDepthCountsNesting()
    {
        var condition = new ConditionFilter("a", FilterOperator.Eq, 1);

        condition.Depth.ShouldBe(1);
        new AndFilter([condition, new NotFilter(new OrFilter([condition]))]).Depth.ShouldBe(4);
    }

    [Fact]
    public void FieldValuesNormaliseProviderTypes()
    {
        FieldValues.ToCanonical(FieldKind.Integer32, 5L).ShouldBe(5);
        FieldValues.ToCanonical(FieldKind.Flag, 1m).ShouldBe(true);
        FieldValues.ToCanonical(FieldKind.Uuid, "0f8fad5b-d9cb-469f-a165-70867728950e").ShouldBeOfType<Guid>();
        FieldValues.ToCanonical(FieldKind.Date, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified)).ShouldBe(new DateOnly(2026, 1, 2));
        FieldValues.ToCanonical(FieldKind.Time, TimeSpan.FromHours(3)).ShouldBe(new TimeOnly(3, 0));
        FieldValues.ToCanonical(FieldKind.Text, DBNull.Value).ShouldBeNull();
        FieldValues.FromInput(FieldKind.Timestamp, new DateTimeOffset(2026, 1, 2, 3, 0, 0, TimeSpan.FromHours(2)))
            .ShouldBe(new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Unspecified));
    }

    private static FieldModel Field(string name, FieldKind kind) => new(name, kind, IsNullable: true, kind.ToString());

    private static EntitySetModel Set(string name, string? schema = null, params FieldModel[] fields) =>
        new(schema, name, fields.Length == 0 ? [Field("id", FieldKind.Integer32)] : fields, ["id"], []);

    private static DataSourceSchema Shop() =>
        new(
            "DEFAULT",
            DatabaseType.Postgresql,
            [
                new EntitySetModel(
                    "public",
                    "customers",
                    [Field("id", FieldKind.Integer32), Field("full_name", FieldKind.Text), Field("secret", FieldKind.Text)],
                    ["id"],
                    []),
                new EntitySetModel(
                    "public",
                    "orders",
                    [Field("id", FieldKind.Integer32), Field("customer_id", FieldKind.Integer32), Field("total", FieldKind.Fixed)],
                    ["id"],
                    [new ForeignKeyModel("fk_orders_customers", ["customer_id"], "public", "customers", ["id"])]),
                new EntitySetModel("public", "outbox_messages", [Field("id", FieldKind.Uuid)], ["id"], []),
            ],
            DataSourceCapabilities.Relational);
}
