using System.Data;
using Data.Sql.DatabaseAccess;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Requests.Products;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SqlDatabaseAccessTests(ApiFactory factory)
{
    [Fact]
    public async Task EntityMap_ForAnEntityFrameworkAggregate_ComesFromTheEntityFrameworkModel()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var map = scope.ServiceProvider.GetRequiredKeyedService<SqlEntityMapFactory>(ProductsStore.DatabaseId).For<ProductRow>();

        map.Source.ShouldBe(SqlMapSource.Convention);
        map.TableName.ShouldBe("products");
    }

    [Fact]
    public async Task EntityMap_FlattensOwnedTypeColumnsFromTheEntityFrameworkModel()
    {
        await using var scope = factory.Services.CreateAsyncScope();

        var map = scope.ServiceProvider
            .GetRequiredKeyedService<SqlEntityMapFactory>(ProductsStore.DatabaseId)
            .For<Domain.Entities.ProductEntity>();

        map.Source.ShouldBe(SqlMapSource.EntityFrameworkModel);
        map.TableName.ShouldBe("products");

        var columns = map.Columns.Select(column => column.ColumnName).ToArray();

        columns.ShouldContain("price_amount");
        columns.ShouldContain("price_currency");
        columns.ShouldContain("created_at_utc");

        map.SingleKeyColumn.ColumnName.ShouldBe("id");
    }

    [Fact]
    public async Task Crud_RoundTripsAConventionMappedRow()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var row = NewRow();

        (await database.InsertAsync(row)).ShouldBe(1);

        var loaded = await database.GetAsync<ProductRow>(row.Id);

        loaded.ShouldNotBeNull();
        loaded.Sku.ShouldBe(row.Sku);
        loaded.PriceAmount.ShouldBe(row.PriceAmount);

        loaded.Name = "Renamed";

        (await database.UpdateAsync(loaded)).ShouldBeTrue();

        (await database.GetAsync<ProductRow>(row.Id))!.Name.ShouldBe("Renamed");

        (await database.DeleteByIdAsync<ProductRow>(row.Id)).ShouldBeTrue();

        (await database.GetAsync<ProductRow>(row.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Transaction_RollbackDiscardsEverything()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var row = NewRow();

        await using (var transaction = await database.BeginTransactionAsync())
        {
            await database.InsertAsync(row, transaction.Transaction);

            (await database.GetAsync<ProductRow>(row.Id, transaction.Transaction)).ShouldNotBeNull();

            await transaction.RollbackAsync();
        }

        (await database.GetAsync<ProductRow>(row.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Transaction_CommitPersists()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var row = NewRow();

        await using (var transaction = await database.BeginTransactionAsync())
        {
            await database.InsertAsync(row, transaction.Transaction);

            await transaction.CommitAsync();
        }

        (await database.GetAsync<ProductRow>(row.Id)).ShouldNotBeNull();

        await database.DeleteByIdAsync<ProductRow>(row.Id);
    }

    [Fact]
    public async Task Transaction_DisposeWithoutCommitRollsBack()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var row = NewRow();

        await using (var transaction = await database.BeginTransactionAsync())
        {
            await database.InsertAsync(row, transaction.Transaction);
        }

        (await database.GetAsync<ProductRow>(row.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task Savepoint_RollsBackOnlyTheWorkAfterIt()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var kept = NewRow();
        var discarded = NewRow();

        await using var transaction = await database.BeginTransactionAsync();

        transaction.SupportsSavepoints.ShouldBeTrue();

        await database.InsertAsync(kept, transaction.Transaction);

        await transaction.CreateSavepointAsync("after_first");

        await database.InsertAsync(discarded, transaction.Transaction);

        await transaction.RollbackToSavepointAsync("after_first");

        (await database.GetAsync<ProductRow>(kept.Id, transaction.Transaction)).ShouldNotBeNull();
        (await database.GetAsync<ProductRow>(discarded.Id, transaction.Transaction)).ShouldBeNull();

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Stream_YieldsEveryRowWithoutBufferingTheWholeSet()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var rows = Enumerable.Range(0, 7).Select(_ => NewRow()).ToArray();

        foreach (var row in rows)
        {
            await database.InsertAsync(row);
        }

        var streamed = new List<string>();

        await foreach (var sku in database.StreamAsync<string>(
            "SELECT sku FROM products WHERE sku = ANY(@Skus) ORDER BY sku",
            new { Skus = rows.Select(row => row.Sku).ToArray() }))
        {
            streamed.Add(sku);
        }

        streamed.Count.ShouldBe(rows.Length);

        foreach (var row in rows)
        {
            await database.DeleteByIdAsync<ProductRow>(row.Id);
        }
    }

    [Fact]
    public async Task QueryMultiple_KeepsResultSetBoundariesSeparate()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var row = NewRow();
        await database.InsertAsync(row);

        await using var sets = await database.QueryMultipleAsync(
            """
            SELECT sku FROM products WHERE id = @Id;
            SELECT price_currency FROM products WHERE id = @Id;
            """,
            new { row.Id });

        var first = await sets.ReadAsync<string>();
        var second = await sets.ReadAsync<string>();

        first.ShouldBe([row.Sku]);
        second.ShouldBe([row.PriceCurrency]);
        sets.IsConsumed.ShouldBeTrue();

        await database.DeleteByIdAsync<ProductRow>(row.Id);
    }

    [Fact]
    public async Task QueryMultiple_ReadingBeyondTheLastSetThrows()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        await using var sets = await database.QueryMultipleAsync("SELECT 1;");

        await sets.ReadAsync<int>();

        await Should.ThrowAsync<InvalidOperationException>(() => sets.ReadAsync<int>());
    }

    [Fact]
    public async Task PagedCache_ReturnsTheRequestedPageAndServesTheSecondCallFromCache()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var rows = Enumerable.Range(0, 9).Select(_ => NewRow()).ToArray();

        foreach (var row in rows)
        {
            await database.InsertAsync(row);
        }

        var scopeKey = Guid.CreateVersion7().ToString();

        var query = new SqlPagedQuery
        {
            Sql = "SELECT sku FROM products WHERE sku = ANY(@Skus) ORDER BY sku",
            Parameters = new { Skus = rows.Select(row => row.Sku).ToArray() },
            Page = 2,
            PageSize = 4,
            CacheScope = scopeKey,
            CacheTtl = TimeSpan.FromMinutes(1),
        };

        var firstCall = await database.QueryPagedCachedAsync<string>(query);

        firstCall.Count.ShouldBe(4);

        var secondCall = await database.QueryPagedCachedAsync<string>(query);

        secondCall.ShouldBe(firstCall);

        var lastPage = await database.QueryPagedCachedAsync<string>(query with { Page = 3 });

        lastPage.Count.ShouldBe(1);

        var beyondTheEnd = await database.QueryPagedCachedAsync<string>(query with { Page = 9 });

        beyondTheEnd.ShouldBeEmpty();

        foreach (var row in rows)
        {
            await database.DeleteByIdAsync<ProductRow>(row.Id);
        }
    }

    [Fact]
    public async Task PagedCache_WithACallerTransaction_StaysInlineAndStillReturnsThePage()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var rows = Enumerable.Range(0, 5).Select(_ => NewRow()).ToArray();

        await using var transaction = await database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        foreach (var row in rows)
        {
            await database.InsertAsync(row, transaction.Transaction);
        }

        var page = await database.QueryPagedCachedAsync<string>(new SqlPagedQuery
        {
            Sql = "SELECT sku FROM products WHERE sku = ANY(@Skus) ORDER BY sku",
            Parameters = new { Skus = rows.Select(row => row.Sku).ToArray() },
            Page = 1,
            PageSize = 2,
            CacheScope = Guid.CreateVersion7().ToString(),
            Transaction = transaction.Transaction,
        });

        page.Count.ShouldBe(2);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ExecuteScalar_AndExecute_WorkAgainstTheSameConnectionContract()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(ProductsStore.DatabaseId);

        var row = NewRow();

        await database.InsertAsync(row);

        (await database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM products WHERE id = @Id",
            new { row.Id })).ShouldBe(1);

        (await database.ExecuteAsync(
            "UPDATE products SET name = @Name WHERE id = @Id",
            new { row.Id, Name = "Executed" })).ShouldBe(1);

        (await database.QueryFirstOrDefaultAsync<string>(
            "SELECT name FROM products WHERE id = @Id",
            new { row.Id })).ShouldBe("Executed");

        await database.DeleteByIdAsync<ProductRow>(row.Id);
    }

    private static ProductRow NewRow()
    {
        var id = Guid.CreateVersion7();

        return new ProductRow
        {
            Id = id,
            Sku = $"ACC-{id:N}"[..20],
            Name = "Access Widget",
            Description = null,
            PriceAmount = 12.34m,
            PriceCurrency = "USD",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = null,
            UpdatedAtUtc = null,
            UpdatedBy = null,
            IsDeleted = false,
            DeletedAtUtc = null,
            DeletedBy = null,
        };
    }
}
