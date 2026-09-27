using Domain.DTOs;
using Domain.Interfaces.Messaging;
using Domain.Interfaces.Persistence;
using Domain.Models.Requests;
using Domain.Models.Requests.Products;
using Domain.Models.Responses;
using Domain.Results;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Features.Products;

public sealed class ListProductsQueryValidator : AbstractValidator<ListProductsQuery>
{
    public ListProductsQueryValidator()
    {
        RuleFor(query => query.Filter.PageNumber).GreaterThanOrEqualTo(1);

        RuleFor(query => query.Filter.PageSize)
            .InclusiveBetween(1, ProductQueryRequest.MaxPageSize);

        RuleFor(query => query.Filter.SortBy)
            .Must(sortBy => sortBy is null || ListProductsQueryHandler.SortableColumns.ContainsKey(sortBy))
            .WithMessage($"SortBy must be one of: {string.Join(", ", ListProductsQueryHandler.SortableColumns.Keys)}.");
    }
}

public sealed class ListProductsQueryHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] ISqlDatabaseAccess database,
    [FromKeyedServices(ProductsStore.DatabaseId)] ISqlSyntax syntax)
    : IQueryHandler<ListProductsQuery, PagedResult<ProductDto>>
{
    internal static readonly IReadOnlyDictionary<string, string> SortableColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sku"] = "sku",
            ["name"] = "name",
            ["price"] = "price_amount",
            ["created"] = "created_at_utc",
            ["updated"] = "updated_at_utc",
        };

    public async Task<Result<PagedResult<ProductDto>>> Handle(
        ListProductsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var filter = request.Filter;

        var sortColumn = filter.SortBy is not null && SortableColumns.TryGetValue(filter.SortBy, out var mapped)
            ? mapped
            : "created_at_utc";

        var sortDirection = filter.SortDescending ? "DESC" : "ASC";

        var searchParameter = syntax.Parameter("SearchPattern");

        var whereClause = $"""
            WHERE is_deleted = {syntax.BooleanLiteral(false)}
              AND ({syntax.Parameter("Search")} IS NULL
                   OR {syntax.CaseInsensitiveLike("sku", searchParameter)}
                   OR {syntax.CaseInsensitiveLike("name", searchParameter)})
              AND ({syntax.Parameter("IsActive")} IS NULL OR is_active = {syntax.Parameter("IsActive")})
            """;

        var parameters = new
        {
            Search = filter.Search,
            SearchPattern = filter.Search is null ? null : $"%{filter.Search}%",
            IsActive = filter.IsActive,
            Take = filter.PageSize,
            Skip = (filter.PageNumber - 1) * filter.PageSize,
        };

        var totalCount = await database
            .ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM products {whereClause}", parameters, transaction: null, cancellationToken)
            .ConfigureAwait(false);

        if (totalCount == 0)
        {
            return Result.Success(PagedResult.Empty<ProductDto>(filter.PageNumber, filter.PageSize));
        }

        var pagedSql = $"""
            SELECT id            AS {nameof(ProductDto.Id)},
                   sku           AS {nameof(ProductDto.Sku)},
                   name          AS {nameof(ProductDto.Name)},
                   description   AS {nameof(ProductDto.Description)},
                   price_amount  AS {nameof(ProductDto.PriceAmount)},
                   price_currency AS {nameof(ProductDto.PriceCurrency)},
                   is_active     AS {nameof(ProductDto.IsActive)},
                   created_at_utc AS {nameof(ProductDto.CreatedAtUtc)},
                   updated_at_utc AS {nameof(ProductDto.UpdatedAtUtc)}
            FROM products
            {whereClause}
            ORDER BY {sortColumn} {sortDirection}
            """;

        var sql = syntax.ApplyPagination(
            pagedSql,
            offset: (filter.PageNumber - 1) * filter.PageSize,
            limit: filter.PageSize);

        var items = await database
            .QueryAsync<ProductDto>(sql, parameters, transaction: null, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(
            PagedResult.Create(items, filter.PageNumber, filter.PageSize, totalCount));
    }
}
