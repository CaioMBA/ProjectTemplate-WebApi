using Domain.DTOs;
using Domain.Extensions;
using Domain.Interfaces.Messaging;

namespace Domain.Models.Requests.Products;

public sealed record SearchProductsQuery(
    string? Term,
    decimal? MinimumPrice,
    bool ActiveOnly,
    bool ExcludeMatches) : IQuery<IReadOnlyList<ProductDto>>, ICacheableRequest, IDatabaseRequest
{
    public string DatabaseId => ProductsStore.DatabaseId;

    public string CacheId => ProductsStore.CacheId;

    public string CacheKey => DistributedCacheExtensions.BuildKey(
        "product-search",
        Term,
        MinimumPrice?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ActiveOnly.ToString(),
        ExcludeMatches.ToString());

    public TimeSpan? CacheDuration => null;
}
