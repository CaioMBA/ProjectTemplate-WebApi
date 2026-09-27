using Domain.Attributes;

namespace IntegrationTests;

[SqlTable("products")]
public sealed class ProductRow
{
    [SqlKey]
    public Guid Id { get; set; }

    public required string Sku { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public decimal PriceAmount { get; set; }

    public required string PriceCurrency { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? CreatedBy { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string? DeletedBy { get; set; }
}
