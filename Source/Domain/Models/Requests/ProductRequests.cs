using System.ComponentModel.DataAnnotations;

namespace Domain.Models.Requests;

public sealed record CreateProductRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Sku { get; init; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Range(0, double.MaxValue)]
    public decimal PriceAmount { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string PriceCurrency { get; init; } = "USD";
}

public sealed record UpdateProductRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }
}

public sealed record SetProductAvailabilityRequest
{
    public bool IsActive { get; init; }
}

public sealed record ProductSearchRequest
{
    [StringLength(200)]
    public string? Term { get; init; }

    [Range(0, double.MaxValue)]
    public decimal? MinimumPrice { get; init; }

    public bool ActiveOnly { get; init; } = true;

    public bool ExcludeMatches { get; init; }
}

public sealed record ChangeProductPriceRequest
{
    [Range(0, double.MaxValue)]
    public decimal PriceAmount { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string PriceCurrency { get; init; } = "USD";
}

public sealed record ProductQueryRequest
{
    [Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; init; } = 25;

    public const int MaxPageSize = 100;

    [StringLength(200)]
    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    [StringLength(50)]
    public string? SortBy { get; init; }

    public bool SortDescending { get; init; }
}
