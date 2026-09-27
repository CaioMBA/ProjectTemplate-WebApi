using Domain.DTOs;
using Domain.Entities;
using Mapster;

namespace Domain.Mappings;

public sealed class EntityToDtoMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<ProductEntity, ProductDto>()
            .Map(destination => destination.Id, source => source.Id)
            .Map(destination => destination.Sku, source => source.Sku)
            .Map(destination => destination.Name, source => source.Name)
            .Map(destination => destination.Description, source => source.Description)
            .Map(destination => destination.PriceAmount, source => source.Price.Amount)
            .Map(destination => destination.PriceCurrency, source => source.Price.Currency)
            .Map(destination => destination.IsActive, source => source.IsActive)
            .Map(destination => destination.CreatedAtUtc, source => source.CreatedAtUtc)
            .Map(destination => destination.UpdatedAtUtc, source => source.UpdatedAtUtc);
    }
}
