using Domain.DTOs;
using Domain.Entities;
using Domain.Models.Responses;
using Domain.Setup;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace UnitTests.Domain;

public sealed class MapsterConfigurationTests
{
    [Fact]
    public void AddMapsterSetup_CompilesEveryRegisteredMapping()
    {
        var services = new ServiceCollection();

        var act = () => services.AddMapsterSetup(compileEagerly: true);

        act.ShouldNotThrow();
    }

    [Fact]
    public void AddMapsterSetup_RegistersMapperAsScoped()
    {
        var services = new ServiceCollection();
        services.AddMapsterSetup();

        var descriptor = services.Single(service => service.ServiceType == typeof(IMapper));

        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void ProductEntity_MapsToProductDto_FlatteningMoney()
    {
        var mapper = BuildMapper();
        var product = CreateProduct(price: 19.99m, currency: "EUR");

        var dto = mapper.Map<ProductDto>(product);

        dto.Id.ShouldBe(product.Id);
        dto.Sku.ShouldBe("SKU-001");
        dto.Name.ShouldBe("Test Product");
        dto.PriceAmount.ShouldBe(19.99m);
        dto.PriceCurrency.ShouldBe("EUR");
        dto.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void ProductDto_MapsToProductResponse_FormattingPriceDisplay()
    {
        var mapper = BuildMapper();

        var dto = new ProductDto
        {
            Id = Guid.CreateVersion7(),
            Sku = "SKU-002",
            Name = "Formatted",
            PriceAmount = 5m,
            PriceCurrency = "USD",
            IsActive = true,
        };

        var response = mapper.Map<ProductResponse>(dto);

        response.PriceDisplay.ShouldBe("5.00 USD");
        response.Sku.ShouldBe("SKU-002");
    }

    private static IMapper BuildMapper()
    {
        var services = new ServiceCollection();
        services.AddMapsterSetup();

        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }

    private static ProductEntity CreateProduct(decimal price, string currency)
    {
        var money = Money.Create(price, currency);
        money.IsSuccess.ShouldBeTrue();

        var product = ProductEntity.Create("SKU-001", "Test Product", "A description", money.Value);
        product.IsSuccess.ShouldBeTrue();

        return product.Value;
    }
}
