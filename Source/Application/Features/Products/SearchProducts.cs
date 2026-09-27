using Domain.DTOs;
using Domain.Entities;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Domain.Models.Requests.Products;
using Domain.Results;
using Domain.Specifications;
using FluentValidation;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Features.Products;

public sealed class SearchProductsQueryValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsQueryValidator()
    {
        RuleFor(query => query.Term).MaximumLength(ProductEntity.NameMaxLength);
        RuleFor(query => query.MinimumPrice).GreaterThanOrEqualTo(0).When(query => query.MinimumPrice.HasValue);
    }
}

public sealed class SearchProductsQueryHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] IRepository<ProductEntity, Guid> repository,
    IMapper mapper) : IQueryHandler<SearchProductsQuery, IReadOnlyList<ProductDto>>
{
    public async Task<Result<IReadOnlyList<ProductDto>>> Handle(
        SearchProductsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var products = await repository
            .ListAsync(BuildSpecification(request), cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<ProductDto>>(
            products.Select(mapper.Map<ProductDto>).ToList());
    }

    private static Specification<ProductEntity> BuildSpecification(SearchProductsQuery request)
    {
        Specification<ProductEntity> specification = new AllSpecification<ProductEntity>();

        if (request.ActiveOnly)
        {
            specification = specification.And(new ActiveProductSpecification());
        }

        if (!string.IsNullOrWhiteSpace(request.Term))
        {
            var match = new ProductSearchSpecification(request.Term);

            specification = specification.And(request.ExcludeMatches ? match.Not() : match);
        }

        if (request.MinimumPrice.HasValue)
        {
            specification = specification.And(
                new ProductPricedAtLeastSpecification(request.MinimumPrice.Value));
        }

        return specification;
    }
}
