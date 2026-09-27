using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.DTOs;
using Domain.Models.Responses;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProductLifecycleEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Put_RenamesTheProduct()
    {
        var client = factory.CreateClient();

        var created = await CreateProductAsync(client, priceAmount: 10m);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/products/{created.Id}",
            new { name = "Renamed Widget", description = "Renamed by a test." });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var reloaded = await client.GetFromJsonAsync<ProductResponse>(
            $"/api/v1/products/{created.Id}",
            _jsonOptions);

        reloaded.ShouldNotBeNull();
        reloaded.Name.ShouldBe("Renamed Widget");
        reloaded.Description.ShouldBe("Renamed by a test.");
    }

    [Fact]
    public async Task Put_OnAMissingProductIsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/products/{Guid.CreateVersion7()}",
            new { name = "Nobody", description = (string?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_WithAnInvalidNameIsRejected()
    {
        var client = factory.CreateClient();

        var created = await CreateProductAsync(client, priceAmount: 10m);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/products/{created.Id}",
            new { name = string.Empty, description = (string?)null });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patch_TogglesAvailabilityInBothDirections()
    {
        var client = factory.CreateClient();

        var created = await CreateProductAsync(client, priceAmount: 10m);

        var deactivated = await client.PatchAsJsonAsync(
            $"/api/v1/products/{created.Id}/availability",
            new { isActive = false });

        deactivated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterDeactivation = await client.GetFromJsonAsync<ProductResponse>(
            $"/api/v1/products/{created.Id}",
            _jsonOptions);

        afterDeactivation.ShouldNotBeNull();
        afterDeactivation.IsActive.ShouldBeFalse();

        var reactivated = await client.PatchAsJsonAsync(
            $"/api/v1/products/{created.Id}/availability",
            new { isActive = true });

        reactivated.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterReactivation = await client.GetFromJsonAsync<ProductResponse>(
            $"/api/v1/products/{created.Id}",
            _jsonOptions);

        afterReactivation.ShouldNotBeNull();
        afterReactivation.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Search_ComposesSpecificationsWithAnd()
    {
        var client = factory.CreateClient();

        var marker = $"SPEC{Guid.CreateVersion7():N}"[..16];

        var cheap = await CreateProductAsync(client, priceAmount: 1m, name: $"{marker} cheap");
        var pricey = await CreateProductAsync(client, priceAmount: 500m, name: $"{marker} pricey");

        var matches = await client.GetFromJsonAsync<IReadOnlyList<ProductDto>>(
            $"/api/v1/products/search?term={marker}&minimumPrice=100&activeOnly=true",
            _jsonOptions);

        matches.ShouldNotBeNull();
        matches.Select(product => product.Id).ShouldContain(pricey.Id);
        matches.Select(product => product.Id).ShouldNotContain(cheap.Id);
    }

    [Fact]
    public async Task Search_AppliesTheNotCombinator()
    {
        var client = factory.CreateClient();

        var marker = $"NOT{Guid.CreateVersion7():N}"[..16];

        var included = await CreateProductAsync(client, priceAmount: 20m, name: $"{marker} keep");

        var excludedName = $"{marker}zzz drop";
        var excluded = await CreateProductAsync(client, priceAmount: 20m, name: excludedName);

        var matches = await client.GetFromJsonAsync<IReadOnlyList<ProductDto>>(
            $"/api/v1/products/search?term={excludedName}&excludeMatches=true&activeOnly=true",
            _jsonOptions);

        matches.ShouldNotBeNull();
        matches.Select(product => product.Id).ShouldNotContain(excluded.Id);
        matches.Select(product => product.Id).ShouldContain(included.Id);
    }

    [Fact]
    public async Task Search_ExcludesDeactivatedProductsWhenActiveOnly()
    {
        var client = factory.CreateClient();

        var marker = $"ACT{Guid.CreateVersion7():N}"[..16];

        var product = await CreateProductAsync(client, priceAmount: 30m, name: $"{marker} widget");

        await client.PatchAsJsonAsync(
            $"/api/v1/products/{product.Id}/availability",
            new { isActive = false });

        var matches = await client.GetFromJsonAsync<IReadOnlyList<ProductDto>>(
            $"/api/v1/products/search?term={marker}&activeOnly=true",
            _jsonOptions);

        matches.ShouldNotBeNull();
        matches.Select(item => item.Id).ShouldNotContain(product.Id);
    }

    private static async Task<ProductResponse> CreateProductAsync(
        HttpClient client,
        decimal priceAmount,
        string name = "Lifecycle Widget")
    {
        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            sku = $"LIFE-{Guid.CreateVersion7():N}"[..24],
            name,
            description = (string?)null,
            priceAmount,
            priceCurrency = "USD",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<ProductResponse>(_jsonOptions);

        return created.ShouldNotBeNull();
    }
}
