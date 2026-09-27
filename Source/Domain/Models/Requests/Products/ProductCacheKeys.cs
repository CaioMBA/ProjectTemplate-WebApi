using Domain.Extensions;

namespace Domain.Models.Requests.Products;

public static class ProductCacheKeys
{
    public const string Prefix = "product";

    public static string ForProduct(Guid productId) =>
        DistributedCacheExtensions.BuildKey(Prefix, productId.ToString("N"));
}
