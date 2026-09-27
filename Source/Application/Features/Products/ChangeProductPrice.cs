using Domain.Entities;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Domain.Models.Requests.Products;
using Domain.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Features.Products;

public sealed class ChangeProductPriceCommandValidator : AbstractValidator<ChangeProductPriceCommand>
{
    public ChangeProductPriceCommandValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.PriceAmount).GreaterThanOrEqualTo(0);
        RuleFor(command => command.PriceCurrency).NotEmpty().Length(Money.CurrencyCodeLength);
    }
}

public sealed class ChangeProductPriceCommandHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] IRepository<ProductEntity, Guid> repository,
    [FromKeyedServices(ProductsStore.DatabaseId)] IUnitOfWork unitOfWork) : ICommandHandler<ChangeProductPriceCommand>
{
    public async Task<Result> Handle(ChangeProductPriceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await repository
            .GetByIdAsync(request.ProductId, cancellationToken)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Result.Failure(Error.NotFound(
                "Product.NotFound",
                $"No product exists with id '{request.ProductId}'."));
        }

        var price = Money.Create(request.PriceAmount, request.PriceCurrency);

        if (price.IsFailure)
        {
            return Result.Failure(price.Error);
        }

        var changed = product.ChangePrice(price.Value);

        if (changed.IsFailure)
        {
            return changed;
        }

        repository.Update(product);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
