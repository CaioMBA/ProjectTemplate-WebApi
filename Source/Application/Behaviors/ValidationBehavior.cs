using Application.Dispatching;
using Domain.Interfaces.Messaging;
using Domain.Results;
using FluentValidation;

namespace Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var applicable = validators as IValidator<TRequest>[] ?? validators.ToArray();

        if (applicable.Length == 0)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);

        var results = await Task
            .WhenAll(applicable.Select(validator => validator.ValidateAsync(context, cancellationToken)))
            .ConfigureAwait(false);

        var failures = results
            .Where(result => !result.IsValid)
            .SelectMany(result => result.Errors)
            .ToArray();

        if (failures.Length == 0)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        var description = string.Join(
            "; ",
            failures.Select(failure => $"{failure.PropertyName}: {failure.ErrorMessage}"));

        var error = Error.Validation($"{typeof(TRequest).Name}.Validation", description);

        return ResultFactory.Failure<TResponse>(error);
    }
}
