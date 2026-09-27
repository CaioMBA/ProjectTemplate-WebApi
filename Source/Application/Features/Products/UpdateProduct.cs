using Domain.Entities;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Domain.Models.Requests.Products;
using Domain.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Features.Products;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(ProductEntity.NameMaxLength);
        RuleFor(command => command.Description).MaximumLength(ProductEntity.DescriptionMaxLength);
    }
}

public sealed class UpdateProductCommandHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] IRepository<ProductEntity, Guid> repository,
    [FromKeyedServices(ProductsStore.DatabaseId)] IUnitOfWork unitOfWork) : ICommandHandler<UpdateProductCommand>
{
    public async Task<Result> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
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

        var renamed = product.Rename(request.Name, request.Description);

        if (renamed.IsFailure)
        {
            return renamed;
        }

        repository.Update(product);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
