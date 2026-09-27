using Domain.Enums;
using Domain.Interfaces.Messaging;
using Domain.Results;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
[ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(ProblemDetails))]
public abstract class ApiControllerBase(ISender sender) : ControllerBase
{
    protected ISender Sender { get; } = sender;

    internal bool IsDeprecated => Deprecated;

    protected virtual bool Deprecated => false;

    protected IActionResult ToActionResult(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }

    protected IActionResult ToActionResult<TValue>(Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    protected IActionResult ToCreatedResult<TValue>(
        Result<TValue> result,
        string actionName,
        object routeValues)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? CreatedAtAction(actionName, routeValues, result.Value)
            : Problem(result.Error);
    }

    private ObjectResult Problem(Error error)
    {
        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Failure => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = error.Code,
            Detail = error.Description,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = $"{Request.Method} {Request.Path}",
        };

        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        problem.Extensions["errorCode"] = error.Code;

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }
}
