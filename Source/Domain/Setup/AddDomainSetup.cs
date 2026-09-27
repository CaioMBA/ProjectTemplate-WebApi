using Domain.Integration;
using Domain.Interfaces.Configuration;
using Domain.Interfaces.Integration;
using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Domain.Setup;

public static class DomainSetup
{
    public static IServiceCollection AddDomainSetup(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services
            .AddConfigurationModelsSetup(configuration, environmentName)
            .AddIntegrationEventRegistrySetup()
            .AddMapsterSetup();
    }

    public static IServiceCollection AddIntegrationEventRegistrySetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IIntegrationEventRegistry>(
            _ => IntegrationEventRegistry.FromAssemblies(typeof(DomainSetup).Assembly));

        return services;
    }

    public static IServiceCollection AddConfigurationModelsSetup(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<AppSettings>()
            .Bind(configuration.GetSection(AppSettings.SectionName))
            .PostConfigure<ISecretResolver>((settings, secretResolver) =>
                AppSettingsPreparation.ResolveSecrets(settings, secretResolver))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AppSettings>, AppSettingsValidator>());

        services
            .AddOptions<ApiOptions>()
            .Bind(configuration.GetSection(ApiOptions.SectionName))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ApiOptions>, ApiOptionsValidator>());

        services
            .AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.SectionName))
            .PostConfigure<IEnvironmentAccessor>((options, environment) =>
                ObservabilityPreparation.ApplyEnvironment(options, environmentName, environment))
            .ValidateOnStart();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ObservabilityOptions>, ObservabilityOptionsValidator>());

        return services;
    }
}
