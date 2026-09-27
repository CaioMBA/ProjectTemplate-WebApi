using System.Text.Json;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.DynamicData;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebApi.GraphQL.AutoSchema;

namespace IntegrationTests;

public sealed class AutoSchemaBatchingTests
{
    [Fact]
    public async Task NestedRelationsAndLookupsAreBatchedIntoOneCallPerLevel()
    {
        var source = new RecordingSource();
        var executor = await ExecutorAsync(source);

        var result = await executor.ExecuteAsync(
            """
            {
              default {
                customers {
                  items {
                    id
                    orders(take: 2) { items { id } }
                  }
                }
                a: customersById(id: 1) { name }
                b: customersById(id: 2) { name }
              }
            }
            """);

        using var json = JsonDocument.Parse(result.ToJson());
        json.RootElement.TryGetProperty("errors", out var errors).ShouldBeFalse(errors.ToString());

        var customers = json.RootElement.GetProperty("data").GetProperty("default").GetProperty("customers").GetProperty("items");
        customers.GetArrayLength().ShouldBe(3);
        customers[0].GetProperty("orders").GetProperty("items").GetArrayLength().ShouldBe(2);
        customers[2].GetProperty("orders").GetProperty("items").GetArrayLength().ShouldBe(0);

        source.RelatedCalls.ShouldBe(1);
        source.RelatedValues.ShouldBe([1, 2, 3], ignoreOrder: true);
        source.LookupCalls.ShouldBe(1);

        var lookups = await executor.ExecuteAsync("{ default { orders { items { id customer { name } } } } }");

        lookups.ToJson().ShouldNotContain("\"errors\"");
        source.LookupCalls.ShouldBe(2);
    }

    [Fact]
    public async Task AFailingDatabaseIsSkippedWhileAnotherIsExposed()
    {
        var executor = await ExecutorAsync(
            new GraphQlAutoSchemaOptions(),
            ("DEFAULT", new RecordingSource()),
            ("BROKEN", new RecordingSource { FailDescribe = true }));

        (await executor.ExecuteAsync("{ default { customers { totalCount } } }")).ToJson().ShouldNotContain("\"errors\"");
        (await executor.ExecuteAsync("{ broken { customers { totalCount } } }")).ToJson().ShouldContain("does not exist");
    }

    [Fact]
    public async Task WhenNothingCanBeExposedTheServerStartsWithOnlyTheStatusField()
    {
        var executor = await ExecutorAsync(
            new GraphQlAutoSchemaOptions { ExcludeDatabases = ["OTHER"] },
            ("DEFAULT", new RecordingSource { FailDescribe = true }),
            ("OTHER", new RecordingSource()));

        var result = (await executor.ExecuteAsync("{ autoSchemaStatus { databases { id exposed reason } } }")).ToJson();

        using var json = JsonDocument.Parse(result);
        json.RootElement.TryGetProperty("errors", out _).ShouldBeFalse(result);

        var databases = json.RootElement.GetProperty("data").GetProperty("autoSchemaStatus").GetProperty("databases");
        databases[0].GetProperty("id").GetString().ShouldBe("DEFAULT");
        databases[0].GetProperty("exposed").GetBoolean().ShouldBeFalse();
        databases[0].GetProperty("reason").GetString().ShouldBe("unreachable");
        databases[1].GetProperty("reason").GetString().ShouldBe("excluded");
        result.ShouldNotContain("InvalidOperationException");

        (await executor.ExecuteAsync("{ default { customers { totalCount } } }")).ToJson().ShouldContain("does not exist");
    }

    [Fact]
    public async Task ThePartialStatusIsReportedAndStrictModeStillFailsStartup()
    {
        var executor = await ExecutorAsync(
            new GraphQlAutoSchemaOptions(),
            ("DEFAULT", new RecordingSource()),
            ("BROKEN", new RecordingSource { FailDescribe = true }));

        var result = (await executor.ExecuteAsync("{ autoSchemaStatus { databases { id exposed } } }")).ToJson();
        result.ShouldContain("\"exposed\": true");
        result.ShouldContain("\"exposed\": false");

        var strict = await Should.ThrowAsync<Exception>(() => ExecutorAsync(
            new GraphQlAutoSchemaOptions { FailOnIntrospectionError = true },
            ("DEFAULT", new RecordingSource()),
            ("BROKEN", new RecordingSource { FailDescribe = true })));
        Flatten(strict).ShouldContain("unreachable");
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : exception.Message + " | " + Flatten(exception.InnerException);

    private static Task<IRequestExecutor> ExecutorAsync(RecordingSource source) =>
        ExecutorAsync(new GraphQlAutoSchemaOptions(), ("DEFAULT", source));

    private static async Task<IRequestExecutor> ExecutorAsync(
        GraphQlAutoSchemaOptions options,
        params (string Id, RecordingSource Source)[] sources)
    {
        var services = new ServiceCollection();

        services.AddLogging();

        foreach (var (id, source) in sources)
        {
            services.AddKeyedScoped<IDynamicDataSource>(id, (_, _) => source);
        }

        services.AddDataLoader<AutoSchemaRowLoader>();

        services.AddGraphQLServer()
            .AddQueryType(descriptor => descriptor.Name("Query"))
            .AddTypeModule(provider => new AutoSchemaTypeModule(
                provider,
                options,
                sources.Select(source => source.Id).ToList(),
                "Query",
                NullLogger<AutoSchemaTypeModule>.Instance));

        return await services.BuildServiceProvider().GetRequestExecutorAsync();
    }

    private sealed class RecordingSource : IDynamicDataSource
    {
        private static readonly EntitySetModel _customers = new(
            null,
            "customers",
            [new FieldModel("id", FieldKind.Integer32, false, "int"), new FieldModel("name", FieldKind.Text, false, "text")],
            ["id"],
            []);

        private static readonly EntitySetModel _orders = new(
            null,
            "orders",
            [new FieldModel("id", FieldKind.Integer64, false, "bigint"), new FieldModel("customer_id", FieldKind.Integer64, false, "bigint")],
            ["id"],
            [new ForeignKeyModel("fk", ["customer_id"], null, "customers", ["id"])]);

        private static readonly List<IReadOnlyDictionary<string, object?>> _customerRows =
        [
            Row(("id", 1), ("name", "Ana")),
            Row(("id", 2), ("name", "Bruno")),
            Row(("id", 3), ("name", "Carla")),
        ];

        private static readonly List<IReadOnlyDictionary<string, object?>> _orderRows =
        [
            Row(("id", 10L), ("customer_id", 1L)),
            Row(("id", 11L), ("customer_id", 1L)),
            Row(("id", 12L), ("customer_id", 1L)),
            Row(("id", 13L), ("customer_id", 2L)),
        ];

        public bool FailDescribe { get; init; }

        public int RelatedCalls { get; private set; }

        public int LookupCalls { get; private set; }

        public List<int> RelatedValues { get; } = [];

        public Task<DataSourceSchema> DescribeAsync(CancellationToken cancellationToken = default) =>
            FailDescribe
                ? throw new InvalidOperationException("unreachable")
                : Task.FromResult(new DataSourceSchema("DEFAULT", DatabaseType.Sqlite, [_customers, _orders], DataSourceCapabilities.Relational));

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(DynamicQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(query.Set.Name == "customers" ? _customerRows : _orderRows);

        public Task<long> CountAsync(EntitySetModel set, FilterNode? filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);

        public Task<IReadOnlyDictionary<string, object?>> AggregateAsync(
            EntitySetModel set,
            FilterNode? filter,
            AggregateFunction function,
            IReadOnlyList<string> fields,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetByValuesAsync(
            EntitySetModel set,
            string field,
            IReadOnlyList<object> values,
            CancellationToken cancellationToken = default)
        {
            LookupCalls++;

            var rows = (set.Name == "customers" ? _customerRows : _orderRows)
                .Where(row => values.Any(value => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) == Convert.ToInt64(row[field], System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();

            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(rows);
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRelatedAsync(RelatedQuery query, CancellationToken cancellationToken = default)
        {
            RelatedCalls++;
            RelatedValues.AddRange(query.Values.Select(value => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)));

            var rows = _orderRows
                .Where(row => query.Values.Any(value => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) == (long)row["customer_id"]!))
                .GroupBy(row => row["customer_id"])
                .SelectMany(group => group.Skip(query.Skip).Take(query.Take))
                .ToList();

            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(rows);
        }

        private static Dictionary<string, object?> Row(params (string Key, object? Value)[] values) =>
            values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
