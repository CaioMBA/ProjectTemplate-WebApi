using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AutoSchemaMongoTests(ApiFactory factory) : IAsyncLifetime
{
    private const string DatabaseName = "autoschema_docs";

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:8.0").Build();

    private ObjectId _anaId;

    public async Task InitializeAsync()
    {
        await _mongo.StartAsync();

        var people = new MongoClient(_mongo.GetConnectionString()).GetDatabase(DatabaseName).GetCollection<BsonDocument>("people");

        var ana = new BsonDocument { { "name", "Ana Silva" }, { "age", 30 }, { "address", new BsonDocument("city", "Lisbon") }, { "tags", new BsonArray { "a" } } };

        await people.InsertManyAsync(
        [
            ana,
            new BsonDocument { { "name", "Bruno Costa" }, { "age", 25 }, { "address", new BsonDocument("city", "Porto") } },
            new BsonDocument { { "name", "Carla Dias" }, { "age", 41 }, { "address", new BsonDocument("city", "Lisbon") } },
            new BsonDocument { { "name", "50% Off_Store" }, { "age", 2 } },
        ]);

        _anaId = ana["_id"].AsObjectId;
    }

    public Task DisposeAsync() => _mongo.DisposeAsync().AsTask();

    [Fact]
    public async Task CollectionsAreSampledFilteredSortedAndAggregated()
    {
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Api:GraphQlServer:AutoSchema:ExcludeDatabases:0", "DEFAULT");
            builder.UseSetting("Settings:Databases:1:Id", "DOCS");
            builder.UseSetting("Settings:Databases:1:Type", "MongoDb");
            builder.UseSetting("Settings:Databases:1:ConnectionString", _mongo.GetConnectionString());
            builder.UseSetting("Settings:Databases:1:Database", DatabaseName);
        });

        var data = await QueryAsync(
            host,
            $$"""
            {
              docs {
                lisbon: people(where: { address: { city: { eq: "Lisbon" } }, name: { contains: "SILVA" } }) {
                  totalCount
                  items { id name address { city } tags }
                }
                literal: people(where: { name: { startsWith: "50%" } }) { items { name } }
                missingAddress: people(where: { address: { city: { isNull: true } } }) { totalCount }
                ordered: people(order: [{ age: DESC }], skip: 1, take: 2) {
                  items { name }
                  aggregate { count max { age } avg { age } sum { age } }
                }
                peopleById(id: "{{_anaId}}") { name }
              }
            }
            """);

        var docs = data.GetProperty("docs");

        var lisbon = docs.GetProperty("lisbon");
        lisbon.GetProperty("totalCount").GetInt64().ShouldBe(1);
        lisbon.GetProperty("items")[0].GetProperty("address").GetProperty("city").GetString().ShouldBe("Lisbon");
        lisbon.GetProperty("items")[0].GetProperty("id").GetString().ShouldBe(_anaId.ToString());
        lisbon.GetProperty("items")[0].GetProperty("tags").GetString().ShouldBe("[\"a\"]");

        docs.GetProperty("literal").GetProperty("items").GetArrayLength().ShouldBe(1);
        docs.GetProperty("missingAddress").GetProperty("totalCount").GetInt64().ShouldBe(1);

        var ordered = docs.GetProperty("ordered");
        ordered.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ShouldBe(["Ana Silva", "Bruno Costa"]);

        var aggregate = ordered.GetProperty("aggregate");
        aggregate.GetProperty("count").GetInt64().ShouldBe(4);
        aggregate.GetProperty("max").GetProperty("age").GetInt64().ShouldBe(41);
        aggregate.GetProperty("sum").GetProperty("age").GetInt64().ShouldBe(98);
        aggregate.GetProperty("avg").GetProperty("age").GetDouble().ShouldBe(24.5);

        docs.GetProperty("peopleById").GetProperty("name").GetString().ShouldBe("Ana Silva");
    }

    private static async Task<JsonElement> QueryAsync(WebApplicationFactory<Program> host, string query)
    {
        var client = host.CreateClient();

        using var content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(new Uri("/graphql", UriKind.Relative), content);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        if (payload.TryGetProperty("errors", out var errors))
        {
            throw new InvalidOperationException(errors.ToString());
        }

        return payload.GetProperty("data");
    }
}
