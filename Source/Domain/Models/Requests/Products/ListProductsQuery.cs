using Domain.DTOs;
using Domain.Interfaces.Messaging;
using Domain.Models.Responses;

namespace Domain.Models.Requests.Products;

public sealed record ListProductsQuery(ProductQueryRequest Filter) : IQuery<PagedResult<ProductDto>>, IDatabaseRequest
{
    public string DatabaseId => ProductsStore.DatabaseId;
}
