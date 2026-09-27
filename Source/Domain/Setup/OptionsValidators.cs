using Domain.Models.Configuration;
using Microsoft.Extensions.Options;

namespace Domain.Setup;

public sealed class ApiOptionsValidator : IValidateOptions<ApiOptions>
{
    public ValidateOptionsResult Validate(string? name, ApiOptions options) =>
        OptionsValidation.Result(ApiOptionsPreparation.Validate(options));
}

public sealed class ObservabilityOptionsValidator : IValidateOptions<ObservabilityOptions>
{
    public ValidateOptionsResult Validate(string? name, ObservabilityOptions options) =>
        OptionsValidation.Result(ObservabilityPreparation.Validate(options));
}

public static class OptionsValidation
{
    public static ValidateOptionsResult Result(IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static T ThrowIfInvalid<T>(T options, IReadOnlyList<string> errors)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(errors);

        return errors.Count == 0
            ? options
            : throw new OptionsValidationException(typeof(T).Name, typeof(T), errors);
    }
}