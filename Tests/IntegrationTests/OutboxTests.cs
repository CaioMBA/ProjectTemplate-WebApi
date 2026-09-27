using System.Net;
using System.Net.Http.Json;
using Data.Sql.EntityFrameworkContexts;
using Domain.Events.Integration;
using Domain.Models.Requests.Products;
using Domain.Models.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OutboxTests(ApiFactory factory)
{
    [Fact]
    public async Task CreatingAProduct_EnqueuesAnIntegrationEventInTheSameTransaction()
    {
        var client = factory.CreateClient();
        var sku = UniqueSku();

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            sku,
            name = "Outbox Widget",
            description = (string?)null,
            priceAmount = 4.5m,
            priceCurrency = "USD",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ProductResponse>();
        created.ShouldNotBeNull();

        var message = await FindOutboxMessageAsync(ProductCreatedIntegrationEvent.Name, created.Id);

        message.ShouldNotBeNull();
        message.ProcessedOnUtc.ShouldBeNull();
        message.AttemptCount.ShouldBe(0);
        message.Payload.ShouldContain(sku);
    }

    [Fact]
    public async Task ChangingAPrice_EnqueuesAPriceChangedIntegrationEvent()
    {
        var client = factory.CreateClient();
        var sku = UniqueSku();

        var created = await client.PostAsJsonAsync("/api/v1/products", new
        {
            sku,
            name = "Repriced Widget",
            description = (string?)null,
            priceAmount = 10m,
            priceCurrency = "USD",
        });

        var product = await created.Content.ReadFromJsonAsync<ProductResponse>();
        product.ShouldNotBeNull();

        var changed = await client.PutAsJsonAsync(
            $"/api/v1/products/{product.Id}/price",
            new { priceAmount = 15m, priceCurrency = "USD" });

        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var message = await FindOutboxMessageAsync(ProductPriceChangedIntegrationEvent.Name, product.Id);

        message.ShouldNotBeNull();
        message.Payload.ShouldContain("15");
    }

    [Fact]
    public async Task AFailedCommand_LeavesNoOutboxRow()
    {
        var client = factory.CreateClient();
        var sku = UniqueSku();

        var payload = new
        {
            sku,
            name = "Duplicate Widget",
            description = (string?)null,
            priceAmount = 1m,
            priceCurrency = "USD",
        };

        var first = await client.PostAsJsonAsync("/api/v1/products", payload);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var beforeCount = await CountOutboxMessagesAsync(ProductCreatedIntegrationEvent.Name);

        var duplicate = await client.PostAsJsonAsync("/api/v1/products", payload);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var afterCount = await CountOutboxMessagesAsync(ProductCreatedIntegrationEvent.Name);

        afterCount.ShouldBe(beforeCount);
    }

    private async Task<Domain.Entities.OutboxMessageEntity?> FindOutboxMessageAsync(
        string eventType,
        Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredKeyedService<AppDbContext>(ProductsStore.DatabaseId);

        return await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.EventType == eventType)
            .Where(message => message.Payload.Contains(productId.ToString()))
            .SingleOrDefaultAsync();
    }

    private async Task<int> CountOutboxMessagesAsync(string eventType)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredKeyedService<AppDbContext>(ProductsStore.DatabaseId);

        return await context.OutboxMessages
            .AsNoTracking()
            .CountAsync(message => message.EventType == eventType);
    }

    private static string UniqueSku() => $"SKU-{Guid.CreateVersion7():N}"[..20];
}
