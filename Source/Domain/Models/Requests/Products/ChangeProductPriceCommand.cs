using Domain.Interfaces.Messaging;

namespace Domain.Models.Requests.Products;

public sealed record ChangeProductPriceCommand(
    Guid ProductId,
    decimal PriceAmount,
    string PriceCurrency) : ICommand, ICacheInvalidatingRequest, IDatabaseRequest
{
    public string DatabaseId => ProductsStore.DatabaseId;

    public string CacheId => ProductsStore.CacheId;

    public IReadOnlyCollection<string> CacheKeysToEvict => [ProductCacheKeys.ForProduct(ProductId)];
}
