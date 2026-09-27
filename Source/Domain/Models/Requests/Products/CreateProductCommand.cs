using Domain.DTOs;
using Domain.Interfaces.Messaging;

namespace Domain.Models.Requests.Products;

public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string? Description,
    decimal PriceAmount,
    string PriceCurrency,
    Guid RequestId) : ICommand<ProductDto>, IIdempotentRequest, IDatabaseRequest
{
    public string DatabaseId => ProductsStore.DatabaseId;

    public string CacheId => ProductsStore.CacheId;
}
