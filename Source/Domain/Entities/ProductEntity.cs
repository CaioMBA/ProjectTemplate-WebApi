using Domain.Abstractions;
using Domain.Events;
using Domain.Guards;
using Domain.Results;

namespace Domain.Entities;

public sealed class ProductEntity : AggregateRoot<Guid>, IAuditable, ISoftDeletable
{
    private ProductEntity(Guid id, string sku, string name, string? description, Money price)
        : base(id)
    {
        Sku = sku;
        Name = name;
        Description = description;
        Price = price;
        IsActive = true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell",
        "S1144:Unused private types or members should be removed",
        Justification = "EF Core invokes this parameterless constructor reflectively when " +
                        "materialising the entity from a query. Removing it makes every read " +
                        "of this aggregate fail at runtime.")]
    private ProductEntity()
    {
        Sku = null!;
        Name = null!;
        Price = null!;
    }

    public string Sku { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public Money Price { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }

    public const int SkuMaxLength = 64;

    public const int NameMaxLength = 200;

    public const int DescriptionMaxLength = 2000;

    public static Result<ProductEntity> Create(string sku, string name, string? description, Money price)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Result.Failure<ProductEntity>(
                Error.Validation("Product.SkuRequired", "SKU is required."));
        }

        if (sku.Length > SkuMaxLength)
        {
            return Result.Failure<ProductEntity>(
                Error.Validation("Product.SkuTooLong", $"SKU must be {SkuMaxLength} characters or fewer."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<ProductEntity>(
                Error.Validation("Product.NameRequired", "Name is required."));
        }

        if (name.Length > NameMaxLength)
        {
            return Result.Failure<ProductEntity>(
                Error.Validation("Product.NameTooLong", $"Name must be {NameMaxLength} characters or fewer."));
        }

        if (description?.Length > DescriptionMaxLength)
        {
            return Result.Failure<ProductEntity>(
                Error.Validation(
                    "Product.DescriptionTooLong",
                    $"Description must be {DescriptionMaxLength} characters or fewer."));
        }

        if (price is null)
        {
            return Result.Failure<ProductEntity>(
                Error.Validation("Product.PriceRequired", "Price is required."));
        }

        var product = new ProductEntity(Guid.CreateVersion7(), sku.Trim(), name.Trim(), description?.Trim(), price);

        product.RaiseDomainEvent(new ProductCreatedDomainEvent(product.Id, product.Sku, product.Name));

        return Result.Success(product);
    }

    public Result Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("Product.NameRequired", "Name is required."));
        }

        if (name.Length > NameMaxLength)
        {
            return Result.Failure(
                Error.Validation("Product.NameTooLong", $"Name must be {NameMaxLength} characters or fewer."));
        }

        Name = name.Trim();
        Description = description?.Trim();

        return Result.Success();
    }

    public Result ChangePrice(Money newPrice)
    {
        Guard.AgainstNull(newPrice);

        if (newPrice.Currency != Price.Currency)
        {
            return Result.Failure(Error.Validation(
                "Product.CurrencyMismatch",
                $"Price currency cannot change from {Price.Currency} to {newPrice.Currency}."));
        }

        if (newPrice == Price)
        {
            return Result.Success();
        }

        var previous = Price;
        Price = newPrice;

        RaiseDomainEvent(new ProductPriceChangedDomainEvent(Id, Sku, previous.Amount, newPrice.Amount, newPrice.Currency));

        return Result.Success();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
