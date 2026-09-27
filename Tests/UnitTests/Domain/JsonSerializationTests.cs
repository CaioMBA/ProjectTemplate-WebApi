using Domain.Events.Integration;
using Domain.Extensions;
using Domain.Interfaces.Integration;

namespace UnitTests.Domain;

public sealed class JsonSerializationTests
{
    [Fact]
    public void ToJson_SerializesTheRuntimeType_NotTheDeclaredType()
    {
        IIntegrationEvent integrationEvent = new ProductCreatedIntegrationEvent
        {
            ProductId = Guid.CreateVersion7(),
            Sku = "SKU-1",
            ProductName = "Widget",
        };

        var json = integrationEvent.ToJson();

        json.ShouldContain("productId");
        json.ShouldContain("sku");
        json.ShouldContain("productName");
    }

    [Fact]
    public void ToJsonBytes_SerializesTheRuntimeType_NotTheDeclaredType()
    {
        IIntegrationEvent integrationEvent = new ProductPriceChangedIntegrationEvent
        {
            ProductId = Guid.CreateVersion7(),
            Sku = "SKU-2",
            PreviousAmount = 1m,
            NewAmount = 2m,
            Currency = "USD",
        };

        var json = System.Text.Encoding.UTF8.GetString(integrationEvent.ToJsonBytes());

        json.ShouldContain("previousAmount");
        json.ShouldContain("newAmount");
        json.ShouldContain("currency");
    }

    [Fact]
    public void ToJson_OnANullValue_DoesNotThrow()
    {
        string? value = null;

        value.ToJson().ShouldBe("null");
    }

    [Fact]
    public void ToJson_RoundTripsThroughTheDeclaredInterface()
    {
        IIntegrationEvent original = new ProductCreatedIntegrationEvent
        {
            ProductId = Guid.CreateVersion7(),
            Sku = "SKU-3",
            ProductName = "Round Trip",
        };

        var json = original.ToJson();

        var restored = System.Text.Json.JsonSerializer.Deserialize<ProductCreatedIntegrationEvent>(
            json,
            JsonDefaults.Standard);

        restored.ShouldNotBeNull();
        restored.ProductId.ShouldBe(((ProductCreatedIntegrationEvent)original).ProductId);
        restored.Sku.ShouldBe("SKU-3");
        restored.ProductName.ShouldBe("Round Trip");
    }
}
