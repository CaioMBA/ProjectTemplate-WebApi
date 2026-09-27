using System.Net;

namespace Domain.Models.Responses;

public sealed class RestApiResponseModel
{
    public HttpStatusCode StatusCode { get; set; }

    public bool IsSuccessStatusCode { get; set; }

    public string? Content { get; set; }

    public string? ContentType { get; set; }

    public Dictionary<string, string> Headers { get; set; } = [];

    public TimeSpan Elapsed { get; set; }
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];

    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}

public static class PagedResult
{
    public static PagedResult<T> Create<T>(
        IReadOnlyList<T> items,
        int pageNumber,
        int pageSize,
        int totalCount) =>
        new()
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
        };

    public static PagedResult<T> Empty<T>(int pageNumber, int pageSize) =>
        new() { Items = [], PageNumber = pageNumber, PageSize = pageSize, TotalCount = 0 };
}
