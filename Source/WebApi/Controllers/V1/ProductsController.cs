using Domain.DTOs;
using Domain.Interfaces.Messaging;
using Domain.Models.Requests;
using Domain.Models.Requests.Products;
using Domain.Models.Responses;
using MapsterMapper;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers.V1;

public sealed class ProductsController(ISender sender, IMapper mapper) : ApiControllerBase(sender)
{
    [HttpGet(Name = nameof(ListProducts))]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResult<ProductResponse>))]
    public async Task<IActionResult> ListProducts(
        [FromQuery] ProductQueryRequest query,
        CancellationToken cancellationToken)
    {
        var result = await Sender
            .Send(new ListProductsQuery(query), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        var page = PagedResult.Create(
            result.Value.Items.Select(mapper.Map<ProductResponse>).ToList(),
            result.Value.PageNumber,
            result.Value.PageSize,
            result.Value.TotalCount);

        return Ok(page);
    }

    [HttpGet("{id:guid}", Name = nameof(GetProduct))]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ProductResponse))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender
            .Send(new GetProductByIdQuery(id), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure
            ? ToActionResult(result)
            : Ok(mapper.Map<ProductResponse>(result.Value));
    }

    [HttpPost(Name = nameof(CreateProduct))]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ProductResponse))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> CreateProduct(
        [FromBody] CreateProductRequest request,
        [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateProductCommand(
            request.Sku,
            request.Name,
            request.Description,
            request.PriceAmount,
            request.PriceCurrency,

            idempotencyKey ?? Guid.CreateVersion7());

        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        var response = mapper.Map<ProductResponse>(result.Value);

        return CreatedAtAction(nameof(GetProduct), new { id = response.Id, version = HttpContext.RequestedApiVersion?.ToString() }, response);
    }

    [HttpPut("{id:guid}/price", Name = nameof(ChangeProductPrice))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> ChangeProductPrice(
        Guid id,
        [FromBody] ChangeProductPriceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Sender
            .Send(
                new ChangeProductPriceCommand(id, request.PriceAmount, request.PriceCurrency),
                cancellationToken)
            .ConfigureAwait(false);

        return ToActionResult(result);
    }

    [HttpPut("{id:guid}", Name = nameof(UpdateProduct))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> UpdateProduct(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Sender
            .Send(new UpdateProductCommand(id, request.Name, request.Description), cancellationToken)
            .ConfigureAwait(false);

        return ToActionResult(result);
    }

    [HttpPatch("{id:guid}/availability", Name = nameof(SetProductAvailability))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<IActionResult> SetProductAvailability(
        Guid id,
        [FromBody] SetProductAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Sender
            .Send(new SetProductAvailabilityCommand(id, request.IsActive), cancellationToken)
            .ConfigureAwait(false);

        return ToActionResult(result);
    }

    [HttpGet("search", Name = nameof(SearchProducts))]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyList<ProductDto>))]
    public async Task<IActionResult> SearchProducts(
        [FromQuery] ProductSearchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Sender
            .Send(
                new SearchProductsQuery(
                    request.Term,
                    request.MinimumPrice,
                    request.ActiveOnly,
                    request.ExcludeMatches),
                cancellationToken)
            .ConfigureAwait(false);

        return ToActionResult(result);
    }
}
