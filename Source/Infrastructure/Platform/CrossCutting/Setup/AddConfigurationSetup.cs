using CrossCutting.Configuration;
using CrossCutting.Services;
using Domain.Interfaces.Configuration;
using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using Domain.Setup;
using Microsoft.Extensions.Configuration;

namespace CrossCutting.Setup;

public static class ConfigurationSetup
{
    public static ISecretResolver CreateSecretResolver(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(SecretResolutionOptions.SectionName)
            .Get<SecretResolutionOptions>() ?? new SecretResolutionOptions();

        return new SecretResolver(new PhysicalFileSystem(), options);
    }

    public static StartupSettings GetStartupSettings(
        this IConfiguration configuration,
        string environmentName,
        IEnvironmentAccessor environment,
        ISecretResolver secretResolver)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(secretResolver);

        return new StartupSettings(
            configuration.GetAppSettings(secretResolver),
            configuration.GetApiOptions(),
            configuration.GetObservabilityOptions(environmentName, environment),
            environmentName);
    }

    public static AppSettings GetAppSettings(
        this IConfiguration configuration,
        ISecretResolver secretResolver)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secretResolver);

        var settings = configuration.GetSection(AppSettings.SectionName).Get<AppSettings>()
            ?? new AppSettings();

        return AppSettingsPreparation.Prepare(settings, secretResolver);
    }

    public static ApiOptions GetApiOptions(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()
            ?? new ApiOptions();

        return OptionsValidation.ThrowIfInvalid(options, ApiOptionsPreparation.Validate(options));
    }

    public static ObservabilityOptions GetObservabilityOptions(
        this IConfiguration configuration,
        string environmentName,
        IEnvironmentAccessor environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();

        ObservabilityPreparation.ApplyEnvironment(options, environmentName, environment);

        return OptionsValidation.ThrowIfInvalid(options, ObservabilityPreparation.Validate(options));
    }
}