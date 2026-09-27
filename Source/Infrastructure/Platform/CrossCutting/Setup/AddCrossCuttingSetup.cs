using Application.Setup;
using CrossCutting.Configuration;
using Domain.Models.Configuration;
using Domain.Setup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrossCutting.Setup;

public static class CrossCuttingSetup
{
    public static IServiceCollection AddCrossCuttingSetup(
        this IServiceCollection services,
        IConfiguration configuration,
        StartupSettings startup,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(startup);

        services.AddSingleton(startup);

        services.AddHostedService<RestartRequiredSettingsWatcher>();

        return services

            .AddDomainSetup(configuration, startup.EnvironmentName)

            .AddApplicationSetup()

            .AddServicesSetup(configuration)

            .AddHttpClientsSetup()

            .AddModulesSetup(startup.Settings, startup.Observability, logger);
    }
}