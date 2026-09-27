using Domain.Entities;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Domain.Models.Requests.Products;
using Domain.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Features.Products;

public sealed class SetProductAvailabilityCommandValidator
    : AbstractValidator<SetProductAvailabilityCommand>
{
    public SetProductAvailabilityCommandValidator() =>
        RuleFor(command => command.ProductId).NotEmpty();
}

public sealed class SetProductAvailabilityCommandHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] IRepository<ProductEntity, Guid> repository,
    [FromKeyedServices(ProductsStore.DatabaseId)] IUnitOfWork unitOfWork) : ICommandHandler<SetProductAvailabilityCommand>
{
    public async Task<Result> Handle(
        SetProductAvailabilityCommand request,
        CancellationToken cancellationToken)
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

        if (request.IsActive)
        {
            product.Activate();
        }
        else
        {
            product.Deactivate();
        }

        repository.Update(product);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
