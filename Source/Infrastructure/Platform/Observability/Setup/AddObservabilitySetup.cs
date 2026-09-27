using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using Microsoft.Extensions.Hosting;

namespace Observability.Setup;

public static class ObservabilitySetup
{
    public static IHostApplicationBuilder AddObservabilitySetup(
        this IHostApplicationBuilder builder,
        StartupSettings startup,
        IEnvironmentAccessor environment,
        ISystemInfo systemInfo)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(systemInfo);

        var options = startup.Observability;

        builder.Logging.AddLoggingSetup(options, startup.EnvironmentName, environment, systemInfo);

        builder.Services.AddOpenTelemetrySetup(options, startup.EnvironmentName, environment, systemInfo);
        builder.Services.AddHealthChecksSetup(startup.Settings, options.HealthChecks);

        return builder;
    }
}