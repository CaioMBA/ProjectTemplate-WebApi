using Domain.DTOs;
using Domain.Extensions;
using Domain.Models.Responses;
using Mapster;

namespace Domain.Mappings;

public sealed class DtoToResponseMapping : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<ProductDto, ProductResponse>()
            .Map(destination => destination.Id, source => source.Id)
            .Map(destination => destination.Sku, source => source.Sku)
            .Map(destination => destination.Name, source => source.Name)
            .Map(destination => destination.Description, source => source.Description)
            .Map(destination => destination.PriceAmount, source => source.PriceAmount)
            .Map(destination => destination.PriceCurrency, source => source.PriceCurrency)
            .Map(
                destination => destination.PriceDisplay,
                source => source.PriceAmount.ToMoneyString() + " " + source.PriceCurrency)
            .Map(destination => destination.IsActive, source => source.IsActive)
            .Map(destination => destination.CreatedAtUtc, source => source.CreatedAtUtc)
            .Map(destination => destination.UpdatedAtUtc, source => source.UpdatedAtUtc);
    }
}
