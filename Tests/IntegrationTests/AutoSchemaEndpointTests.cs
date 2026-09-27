using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AutoSchemaEndpointTests(ApiFactory factory)
{
    private const string PostgresFixture =
        """
        CREATE SCHEMA IF NOT EXISTS shop;
        DROP TABLE IF EXISTS shop.orders;
        DROP TABLE IF EXISTS shop.customers;
        DROP TABLE IF EXISTS shop.type_zoo;
        CREATE TABLE shop.customers (
            id integer PRIMARY KEY,
            full_name text NOT NULL,
            city text NULL,
            credit numeric(12,2) NULL,
            is_active boolean NOT NULL,
            secret text NULL,
            created_at timestamptz NOT NULL);
        CREATE TABLE shop.orders (
            id bigint PRIMARY KEY,
            customer_id integer NOT NULL REFERENCES shop.customers(id),
            total numeric(12,2) NOT NULL,
            placed_on date NOT NULL);
        CREATE TABLE shop.type_zoo (
            id uuid PRIMARY KEY,
            small smallint, big bigint, real_value real, dbl double precision, flag boolean,
            day date, clock time, stamp timestamp, stamp_tz timestamptz,
            doc jsonb, raw bytea, span interval, label varchar(20));
        INSERT INTO shop.customers VALUES
            (1, 'Ana Silva', 'Lisbon', 100.50, true, 's1', '2026-01-01T10:00:00Z'),
            (2, 'Bruno Costa', 'Porto', NULL, true, 's2', '2026-01-02T10:00:00Z'),
            (3, 'Carla Dias', NULL, 250.00, false, 's3', '2026-01-03T10:00:00Z'),
            (4, '50% Off_Store', 'Lisbon', 10.00, true, 's4', '2026-01-04T10:00:00Z');
        INSERT INTO shop.orders VALUES
            (10, 1, 20.00, '2026-02-01'),
            (11, 1, 35.50, '2026-02-03'),
            (12, 1, 5.00, '2026-02-05'),
            (13, 2, 99.99, '2026-03-01');
        INSERT INTO shop.type_zoo VALUES (
            '0f8fad5b-d9cb-469f-a165-70867728950e', 7, 9000000000, 1.5, 2.25, true,
            '2026-05-06', '13:14:15', '2026-05-06T07:08:09', '2026-05-06T07:08:09Z',
            '{"a":1}', '\x0102', '1 day', 'zoo');
        """;

    private const string SqliteFixture =
        """
        CREATE TABLE IF NOT EXISTS notes (id INTEGER PRIMARY KEY, title TEXT NOT NULL, score REAL);
        CREATE TABLE IF NOT EXISTS tags (id INTEGER PRIMARY KEY, note_id INTEGER REFERENCES notes(id), label TEXT);
        DELETE FROM tags;
        DELETE FROM notes;
        INSERT INTO notes VALUES (1, 'First note', 1.5), (2, 'Second note', 3.0);
        INSERT INTO tags VALUES (1, 1, 'red'), (2, 1, 'blue'), (3, 2, 'red');
        """;

    [Fact]
    public async Task ListsFilterWithEveryOperatorFamilyAndPage()
    {
        var data = await QueryAsync(
            """
            {
              default {
                customers(
                  where: {
                    or: [{ fullName: { contains: "SILVA" } }, { city: { in: ["Porto"] } }, { credit: { gte: 200 } }]
                    not: { isActive: { eq: false } }
                  }
                  order: [{ fullName: DESC }]
                  take: 10) {
                  totalCount
                  items { id fullName city }
                }
              }
            }
            """);

        var page = data.GetProperty("default").GetProperty("customers");

        page.GetProperty("totalCount").GetInt64().ShouldBe(2);
        Names(page).ShouldBe(["Bruno Costa", "Ana Silva"]);
    }

    [Fact]
    public async Task PatternOperatorsTreatWildcardsInValuesLiterally()
    {
        var data = await QueryAsync(
            """
            {
              default {
                literal: customers(where: { fullName: { startsWith: "50%" } }) { items { fullName } }
                wildcard: customers(where: { fullName: { like: "%a%" } }) { totalCount }
                missing: customers(where: { city: { isNull: true } }) { items { fullName } }
                notPorto: customers(where: { city: { neq: "Porto" } }) { totalCount }
                hostile: customers(where: { fullName: { eq: "'; DROP TABLE shop.customers; --" } }) { totalCount }
              }
            }
            """);

        var root = data.GetProperty("default");

        Names(root.GetProperty("literal")).ShouldBe(["50% Off_Store"]);
        root.GetProperty("wildcard").GetProperty("totalCount").GetInt64().ShouldBe(3);
        Names(root.GetProperty("missing")).ShouldBe(["Carla Dias"]);
        root.GetProperty("notPorto").GetProperty("totalCount").GetInt64().ShouldBe(3);
        root.GetProperty("hostile").GetProperty("totalCount").GetInt64().ShouldBe(0);

        var survived = await QueryAsync("{ default { customers { totalCount } } }");
        survived.GetProperty("default").GetProperty("customers").GetProperty("totalCount").GetInt64().ShouldBe(4);
    }

    [Fact]
    public async Task ByIdAndBothForeignKeyDirectionsResolve()
    {
        var data = await QueryAsync(
            """
            {
              default {
                customersById(id: 1) {
                  fullName
                  orders(order: [{ total: DESC }], take: 2) {
                    totalCount
                    items { id total placedOn }
                    aggregate { sum { total } }
                  }
                }
                orders(where: { total: { lt: 50 } }, order: [{ id: ASC }]) {
                  items { id customer { fullName } }
                }
                missing: customersById(id: 999) { fullName }
              }
            }
            """);

        var root = data.GetProperty("default");
        var customer = root.GetProperty("customersById");

        customer.GetProperty("fullName").GetString().ShouldBe("Ana Silva");

        var orders = customer.GetProperty("orders");
        orders.GetProperty("totalCount").GetInt64().ShouldBe(3);
        orders.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("total").GetDecimal()).ShouldBe([35.50m, 20.00m]);
        orders.GetProperty("items")[0].GetProperty("placedOn").GetString().ShouldBe("2026-02-03");
        orders.GetProperty("aggregate").GetProperty("sum").GetProperty("total").GetDecimal().ShouldBe(60.50m);

        root.GetProperty("orders").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("customer").GetProperty("fullName").GetString())
            .ShouldBe(["Ana Silva", "Ana Silva", "Ana Silva"]);

        root.GetProperty("missing").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task NestedRelationsArePagedPerParent()
    {
        var data = await QueryAsync(
            """
            {
              default {
                customers(order: [{ id: ASC }], take: 3) {
                  items { id orders(order: [{ id: ASC }], skip: 1, take: 1) { items { id } } }
                }
              }
            }
            """);

        var ids = data.GetProperty("default").GetProperty("customers").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("orders").GetProperty("items").EnumerateArray().Select(order => order.GetProperty("id").GetInt64()).ToList())
            .ToList();

        ids[0].ShouldBe([11L]);
        ids[1].ShouldBeEmpty();
        ids[2].ShouldBeEmpty();
    }

    [Fact]
    public async Task AggregatesCoverCountMinMaxSumAndAverage()
    {
        var data = await QueryAsync(
            """
            {
              default {
                orders {
                  aggregate {
                    count
                    min { total placedOn }
                    max { total placedOn }
                    sum { total }
                    avg { total }
                  }
                }
              }
            }
            """);

        var aggregate = data.GetProperty("default").GetProperty("orders").GetProperty("aggregate");

        aggregate.GetProperty("count").GetInt64().ShouldBe(4);
        aggregate.GetProperty("min").GetProperty("total").GetDecimal().ShouldBe(5.00m);
        aggregate.GetProperty("max").GetProperty("placedOn").GetString().ShouldBe("2026-03-01");
        aggregate.GetProperty("sum").GetProperty("total").GetDecimal().ShouldBe(160.49m);
        aggregate.GetProperty("avg").GetProperty("total").GetDouble().ShouldBe(40.1225, 0.0001);
    }

    [Fact]
    public async Task EveryPostgresqlTypeRoundTripsAndUnsupportedOnesAreHidden()
    {
        var data = await QueryAsync(
            """
            {
              default {
                typeZoo(where: { day: { gte: "2026-01-01" }, id: { eq: "0f8fad5b-d9cb-469f-a165-70867728950e" } }) {
                  items { id small big realValue dbl flag day clock stamp stampTz doc raw label }
                }
              }
              zoo: __type(name: "DefaultTypeZoo") { fields { name } }
            }
            """);

        var row = data.GetProperty("default").GetProperty("typeZoo").GetProperty("items")[0];

        row.GetProperty("small").GetInt32().ShouldBe(7);
        row.GetProperty("big").GetInt64().ShouldBe(9_000_000_000);
        row.GetProperty("flag").GetBoolean().ShouldBeTrue();
        row.GetProperty("day").GetString().ShouldBe("2026-05-06");
        row.GetProperty("clock").GetString()!.ShouldStartWith("13:14:15");
        row.GetProperty("stamp").GetString()!.ShouldStartWith("2026-05-06T07:08:09");
        row.GetProperty("stampTz").GetString()!.ShouldStartWith("2026-05-06T07:08:09");
        row.GetProperty("doc").GetString()!.ShouldContain("\"a\"");
        row.GetProperty("raw").GetString().ShouldBe(Convert.ToBase64String([1, 2]));

        var fields = data.GetProperty("zoo").GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()).ToList();
        fields.ShouldNotContain("span");
    }

    [Fact]
    public async Task ExcludedColumnsAndSystemTablesAreNotInTheSchema()
    {
        var data = await QueryAsync(
            """
            {
              customer: __type(name: "DefaultCustomers") { fields { name } }
              database: __type(name: "DefaultDatabase") { fields { name } }
            }
            """);

        Fields(data.GetProperty("customer")).ShouldNotContain("secret");
        Fields(data.GetProperty("customer")).ShouldContain("orders");

        var tables = Fields(data.GetProperty("database"));
        tables.ShouldContain("products");
        tables.ShouldNotContain(name => name.Contains("outbox", StringComparison.OrdinalIgnoreCase));
        tables.ShouldNotContain(name => name.Contains("migration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SqliteIsExposedNextToPostgresqlAndAnUnreachableDatabaseIsSkipped()
    {
        var data = await QueryAsync(
            """
            {
              local {
                notes(where: { title: { endsWith: "NOTE" } }, order: [{ score: DESC }]) {
                  items { title tags(where: { label: { eq: "red" } }) { items { label } } }
                }
              }
              query: __type(name: "Query") { fields { name } }
            }
            """);

        var notes = data.GetProperty("local").GetProperty("notes").GetProperty("items");

        notes.EnumerateArray().Select(note => note.GetProperty("title").GetString()).ShouldBe(["Second note", "First note"]);
        notes[1].GetProperty("tags").GetProperty("items").GetArrayLength().ShouldBe(1);

        var rootFields = Fields(data.GetProperty("query"));
        rootFields.ShouldContain("default");
        rootFields.ShouldNotContain("broken");
    }

    [Fact]
    public async Task LimitsAreEnforcedAsGraphQlErrors()
    {
        var tooMany = await SendAsync("{ default { customers(take: 1000) { totalCount } } }");
        Message(tooMany).ShouldContain("take must be between 0 and 100");

        var tooDeep = await SendAsync(
            "{ default { customers(where: { not: { not: { not: { not: { not: { city: { eq: \"x\" } } } } } } }) { totalCount } } }");
        Message(tooDeep).ShouldContain("nested");

        var unknownOperator = await SendAsync("{ default { customers(where: { isActive: { contains: \"x\" } }) { totalCount } } }");
        unknownOperator.TryGetProperty("errors", out _).ShouldBeTrue();
    }

    private static List<string?> Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("fullName").GetString()).ToList();

    private static List<string> Fields(JsonElement type) =>
        type.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()!).ToList();

    private static string Message(JsonElement payload) =>
        payload.GetProperty("errors")[0].GetProperty("message").GetString()!;

    private async Task<JsonElement> QueryAsync(string query)
    {
        var payload = await SendAsync(query);

        if (payload.TryGetProperty("errors", out var errors))
        {
            throw new InvalidOperationException(errors.ToString());
        }

        return payload.GetProperty("data");
    }

    private async Task<JsonElement> SendAsync(string query)
    {
        var host = await factory.SharedDerivedAsync("auto-schema", CreateHostAsync);
        var client = host.CreateClient();

        using var content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(new Uri("/graphql", UriKind.Relative), content);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<WebApplicationFactory<Program>> CreateHostAsync()
    {
        await using (var connection = new NpgsqlConnection(factory.PostgresConnectionString))
        {
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(PostgresFixture, connection);
            await command.ExecuteNonQueryAsync();
        }

        var sqlitePath = Path.Combine(Path.GetTempPath(), $"autoschema-{Guid.NewGuid():N}.db");

        await using (var connection = new SqliteConnection($"Data Source={sqlitePath}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = SqliteFixture;
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();

        var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Api:GraphQlServer:AutoSchema:ExcludeColumns:0", "customers.secret");
            builder.UseSetting("Settings:Databases:1:Id", "LOCAL");
            builder.UseSetting("Settings:Databases:1:Type", "Sqlite");
            builder.UseSetting("Settings:Databases:1:ConnectionString", $"Data Source={sqlitePath}");
            builder.UseSetting("Settings:Databases:2:Id", "BROKEN");
            builder.UseSetting("Settings:Databases:2:Type", "Postgresql");
            builder.UseSetting("Settings:Databases:2:ConnectionString", "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2");
        });

        _ = host.Server;

        return host;
    }
}
