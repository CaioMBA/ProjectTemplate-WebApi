using Domain.Models.Configuration;
using Microsoft.Extensions.Options;

namespace Domain.Setup;

public sealed class AppSettingsValidator : IValidateOptions<AppSettings>
{
    public ValidateOptionsResult Validate(string? name, AppSettings options) =>
        OptionsValidation.Result(AppSettingsPreparation.Validate(options));
}