using Domain.DTOs;
using Domain.Interfaces.Messaging;

namespace Domain.Models.Requests.Products;

public sealed record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDto>, ICacheableRequest, IDatabaseRequest
{
    public string DatabaseId => ProductsStore.DatabaseId;

    public string CacheId => ProductsStore.CacheId;

    public string CacheKey => ProductCacheKeys.ForProduct(ProductId);

    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);
}
