using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Domain.Models.Requests.Products;
using Domain.Models.Responses;

namespace IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProductsEndpointTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Post_CreatesProduct_AndReturnsCreatedWithLocation()
    {
        var client = factory.CreateClient();
        var sku = UniqueSku();

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            sku,
            name = "Integration Widget",
            description = "Created by an integration test.",
            priceAmount = 12.34m,
            priceCurrency = "USD",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        response.Headers.Location.ShouldNotBeNull();

        var created = await response.Content.ReadFromJsonAsync<ProductResponse>(_jsonOptions);

        created.ShouldNotBeNull();
        created.Sku.ShouldBe(sku);
        created.PriceAmount.ShouldBe(12.34m);

        created.PriceDisplay.ShouldBe("12.34 USD");

        created.Id.ShouldNotBe(Guid.Empty);
        created.Id.ToString()[14].ShouldBe('7');
    }

    [Fact]
    public async Task Post_WithSameIdempotencyKey_ReplaysOriginalResponse()
    {
        var client = factory.CreateClient();
        var sku = UniqueSku();
        var idempotencyKey = Guid.CreateVersion7().ToString();

        var payload = new
        {
            sku,
            name = "Idempotent Widget",
            description = (string?)null,
            priceAmount = 5m,
            priceCurrency = "EUR",
        };

        using var first = BuildPost(payload, idempotencyKey);
        var firstResponse = await client.SendAsync(first);

        using var second = BuildPost(payload, idempotencyKey);
        var secondResponse = await client.SendAsync(second);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var firstBody = await firstResponse.Content.ReadFromJsonAsync<ProductResponse>(_jsonOptions);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<ProductResponse>(_jsonOptions);

        firstBody.ShouldNotBeNull();
        secondBody.ShouldNotBeNull();

        secondBody.Id.ShouldBe(firstBody.Id);
    }

    [Fact]
    public async Task Post_WithDuplicateSku_ReturnsConflictProblemDetails()
    {
        var client = factory.CreateClient();
        var sku = UniqueSku();

        var payload = new
        {
            sku,
            name = "Original",
            description = (string?)null,
            priceAmount = 1m,
            priceCurrency = "USD",
        };

        (await client.PostAsJsonAsync("/api/v1/products", payload))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync("/api/v1/products", payload);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        duplicate.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        problem.GetProperty("title").GetString().ShouldBe("Product.SkuAlreadyExists");

        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Post_WithInvalidPayload_ReturnsValidationProblem()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/products", new
        {
            sku = string.Empty,
            name = string.Empty,
            description = (string?)null,
            priceAmount = -1m,
            priceCurrency = "TOOLONG",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        problem.GetProperty("title").GetString().ShouldBe("CreateProductCommand.Validation");

        var detail = problem.GetProperty("detail").GetString();

        detail.ShouldNotBeNull();
        detail.ShouldContain("Sku");
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFoundProblem()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/api/v1/products/{Guid.CreateVersion7()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        problem.GetProperty("title").GetString().ShouldBe("Product.NotFound");
    }

    [Fact]
    public async Task Get_UnversionedRoute_ReturnsNotFound()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/products", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_List_ReturnsPagedEnvelope()
    {
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/v1/products", new
        {
            sku = UniqueSku(),
            name = "Listed Widget",
            description = (string?)null,
            priceAmount = 3m,
            priceCurrency = "GBP",
        });

        var response = await client.GetAsync(new Uri("/api/v1/products?pageNumber=1&pageSize=5", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<ProductResponse>>(_jsonOptions);

        page.ShouldNotBeNull();
        page.PageSize.ShouldBe(5);
        page.TotalCount.ShouldBeGreaterThan(0);
        page.Items.Count.ShouldBeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task Get_List_CapsPageSize()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/products?pageSize=10000", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_Health_ReportsHealthyWithDatabaseUp()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        payload.GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact]
    public async Task Get_Live_IgnoresDependencies()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_SwaggerDocument_IsServedOnceWithVersionedPaths()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var document = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        document.GetProperty("paths").TryGetProperty("/api/v1/products", out _).ShouldBeTrue();

        var native = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        native.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_GraphQl_SharesTheApplicationLayerWithRest()
    {
        var client = factory.CreateClient();
        var sku = UniqueSku();

        await client.PostAsJsonAsync("/api/v1/products", new
        {
            sku,
            name = "GraphQL Widget",
            description = (string?)null,
            priceAmount = 7.5m,
            priceCurrency = "USD",
        });

        var query = JsonSerializer.Serialize(new
        {
            query = "query($sku: String) { default { products(where: { sku: { eq: $sku } }) { totalCount items { sku name priceAmount } } } }",
            variables = new { sku },
        });

        using var content = new StringContent(query, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(new Uri("/graphql", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);

        payload.TryGetProperty("errors", out _).ShouldBeFalse();

        var items = payload.GetProperty("data").GetProperty("default").GetProperty("products").GetProperty("items");

        items.GetArrayLength().ShouldBe(1);
        items[0].GetProperty("sku").GetString().ShouldBe(sku);
    }

    [Fact]
    public async Task Response_CarriesCorrelationIdHeader()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative));

        response.Headers.TryGetValues("X-Correlation-Id", out var values).ShouldBeTrue();
        values.ShouldNotBeNull();
        values.First().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Response_HonoursSuppliedCorrelationId()
    {
        var client = factory.CreateClient();
        var supplied = $"test-{Guid.CreateVersion7():N}";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        request.Headers.Add("X-Correlation-Id", supplied);

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").First().ShouldBe(supplied);
    }

    private static HttpRequestMessage BuildPost(object payload, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, _jsonOptions),
                Encoding.UTF8,
                "application/json"),
        };

        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return request;
    }

    private static string UniqueSku() => $"SKU-{Guid.CreateVersion7():N}"[..20];
}
