using Domain.DTOs;
using Domain.Entities;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Domain.Models.Requests.Products;
using Domain.Results;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Features.Products;

public sealed class GetProductByIdQueryHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] IRepository<ProductEntity, Guid> repository,
    IMapper mapper) : IQueryHandler<GetProductByIdQuery, ProductDto>
{
    public async Task<Result<ProductDto>> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await repository
            .GetByIdAsync(request.ProductId, cancellationToken)
            .ConfigureAwait(false);

        return product is null
            ? Result.Failure<ProductDto>(Error.NotFound(
                "Product.NotFound",
                $"No product exists with id '{request.ProductId}'."))
            : Result.Success(mapper.Map<ProductDto>(product));
    }
}
