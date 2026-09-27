using System.Data;

namespace Domain.Interfaces.Persistence;

public sealed record SqlPagedQuery
{
    public required string Sql { get; init; }

    public object? Parameters { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 100;

    public TimeSpan? CacheTtl { get; init; }

    public IDbTransaction? Transaction { get; init; }

    public bool WarmRemainingPages { get; init; } = true;

    public string? CacheScope { get; init; }
}
