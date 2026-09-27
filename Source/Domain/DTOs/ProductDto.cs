namespace Domain.DTOs;

public sealed record ProductDto
{
    public Guid Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public decimal PriceAmount { get; init; }

    public string PriceCurrency { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }
}
