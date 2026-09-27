using Domain.Interfaces.Messaging;

namespace Domain.Models.Requests.Products;

public sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string? Description) : ICommand, ITransactionalRequest, ICacheInvalidatingRequest, IDatabaseRequest
{
    public string DatabaseId => ProductsStore.DatabaseId;

    public string CacheId => ProductsStore.CacheId;

    public bool UseTransaction => true;

    public IReadOnlyCollection<string> CacheKeysToEvict => [ProductCacheKeys.ForProduct(ProductId)];
}
