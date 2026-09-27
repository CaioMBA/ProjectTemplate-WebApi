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

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(command => command.Sku)
            .NotEmpty()
            .MaximumLength(ProductEntity.SkuMaxLength);

        RuleFor(command => command.Name)
            .NotEmpty()
            .MaximumLength(ProductEntity.NameMaxLength);

        RuleFor(command => command.Description)
            .MaximumLength(ProductEntity.DescriptionMaxLength);

        RuleFor(command => command.PriceAmount)
            .GreaterThanOrEqualTo(0);

        RuleFor(command => command.PriceCurrency)
            .NotEmpty()
            .Length(Money.CurrencyCodeLength);

        RuleFor(command => command.RequestId)
            .NotEmpty()
            .WithMessage("A RequestId is required so the command can be retried safely.");
    }
}

public sealed class CreateProductCommandHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] IRepository<ProductEntity, Guid> repository,
    [FromKeyedServices(ProductsStore.DatabaseId)] IUnitOfWork unitOfWork,
    IMapper mapper) : ICommandHandler<CreateProductCommand, ProductDto>
{
    public async Task<Result<ProductDto>> Handle(
        CreateProductCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var duplicate = await repository
            .AnyAsync(new ProductBySkuSpecification(request.Sku), cancellationToken)
            .ConfigureAwait(false);

        if (duplicate)
        {
            return Result.Failure<ProductDto>(Error.Conflict(
                "Product.SkuAlreadyExists",
                $"A product with SKU '{request.Sku}' already exists."));
        }

        var price = Money.Create(request.PriceAmount, request.PriceCurrency);

        if (price.IsFailure)
        {
            return Result.Failure<ProductDto>(price.Error);
        }

        var product = ProductEntity.Create(request.Sku, request.Name, request.Description, price.Value);

        if (product.IsFailure)
        {
            return Result.Failure<ProductDto>(product.Error);
        }

        await repository.AddAsync(product.Value, cancellationToken).ConfigureAwait(false);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(mapper.Map<ProductDto>(product.Value));
    }
}
