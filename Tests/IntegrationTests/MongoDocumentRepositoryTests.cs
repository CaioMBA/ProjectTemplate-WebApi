using Data.NoSql.HealthChecks;
using Data.NoSql.Providers;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Shouldly;
using Testcontainers.MongoDb;

namespace IntegrationTests;

public sealed class MongoDocumentRepositoryTests : IAsyncLifetime, IDisposable
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0").Build();

    private readonly CancellationTokenSource _cancellation = new(TimeSpan.FromMinutes(2));

    private ServiceProvider _services = null!;

    private CancellationToken Token => _cancellation.Token;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _container.StartAsync(Token).ConfigureAwait(false);

        var collection = new ServiceCollection();

        NoSqlProviderRegistry.Resolve(DatabaseType.MongoDb).Register(
            collection,
            new DatabaseSettings
            {
                Id = "DOCUMENTS",
                Type = DatabaseType.MongoDb,
                ConnectionString = _container.GetConnectionString(),
                Database = "templatetests",
            });

        _services = collection.BuildServiceProvider();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _services.DisposeAsync().ConfigureAwait(false);
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose() => _cancellation.Dispose();

    private IDocumentRepository<CatalogDocument> Repository() =>
        _services.CreateScope().ServiceProvider.GetRequiredKeyedService<IDocumentRepository<CatalogDocument>>("DOCUMENTS");

    [Fact]
    public async Task InsertThenGetByIdRoundTrips()
    {
        var repository = Repository();

        var document = NewDocument("round-trip", "Widget", 10);

        await repository.InsertAsync(document, Token);

        var loaded = await repository.GetByIdAsync("round-trip", Token);

        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("Widget");
        loaded.Quantity.ShouldBe(10);
    }

    [Fact]
    public async Task GetByIdReturnsNullWhenMissing()
    {
        var loaded = await Repository()
            .GetByIdAsync("does-not-exist", Token);

        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task InsertManyThenListFiltersServerSide()
    {
        var repository = Repository();

        await repository.InsertManyAsync(
            [
                NewDocument("bulk-1", "Alpha", 1),
                NewDocument("bulk-2", "Beta", 5),
                NewDocument("bulk-3", "Gamma", 9),
            ],
            Token);

        var matches = await repository.ListAsync(
            document => document.Quantity >= 5 && document.Id.StartsWith("bulk-"),
            Token);

        matches.Count.ShouldBe(2);
        matches.Select(document => document.Name).OrderBy(name => name)
            .ShouldBe(["Beta", "Gamma"]);
    }

    [Fact]
    public async Task ReplaceOverwritesExistingDocument()
    {
        var repository = Repository();

        await repository.InsertAsync(NewDocument("replace-me", "Before", 1), Token);

        await repository.ReplaceAsync(
            "replace-me",
            NewDocument("replace-me", "After", 42),
            Token);

        var loaded = await repository.GetByIdAsync("replace-me", Token);

        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("After");
        loaded.Quantity.ShouldBe(42);
    }

    [Fact]
    public async Task DeleteReportsWhetherADocumentWasRemoved()
    {
        var repository = Repository();

        await repository.InsertAsync(NewDocument("delete-me", "Doomed", 1), Token);

        (await repository.DeleteAsync("delete-me", Token)).ShouldBeTrue();
        (await repository.DeleteAsync("delete-me", Token)).ShouldBeFalse();
    }

    [Fact]
    public async Task CountAppliesThePredicate()
    {
        var repository = Repository();

        await repository.InsertManyAsync(
            [
                NewDocument("count-1", "One", 3),
                NewDocument("count-2", "Two", 7),
            ],
            Token);

        var count = await repository.CountAsync(
            document => document.Id.StartsWith("count-") && document.Quantity > 5,
            Token);

        count.ShouldBe(1);
    }

    [Fact]
    public async Task HealthCheckReportsHealthyAgainstARunningServer()
    {
        var check = new MongoDbHealthCheck(_services.GetRequiredKeyedService<IMongoDatabase>("DOCUMENTS"));

        var result = await check.CheckHealthAsync(
            new HealthCheckContext(),
            Token);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public void CollectionNameComesFromTheDocumentTypeName() =>
        DocumentNaming.CollectionFor<CatalogDocument>().ShouldBe("catalogs");

    private static CatalogDocument NewDocument(string id, string name, int quantity) =>
        new() { Id = id, Name = name, Quantity = quantity };
}

public sealed class CatalogDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }
}
