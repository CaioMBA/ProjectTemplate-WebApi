using CrossCutting.Modules;
using Domain.Interfaces.Modules;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrossCutting.Setup;

public static class ModulesSetup
{
    public static IServiceCollection AddModulesSetup(
        this IServiceCollection services,
        AppSettings settings,
        ObservabilityOptions observability,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(observability);
        ArgumentNullException.ThrowIfNull(logger);

        new InfrastructureModuleLoader().Load(
            services,
            new InfrastructureModuleContext(settings, observability),
            logger);

        return services;
    }
}