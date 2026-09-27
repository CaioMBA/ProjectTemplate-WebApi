using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Observability.Resources;
using OpenTelemetry.Logs;

namespace Observability.Setup;

public static class LoggingSetup
{
    public static ILoggingBuilder AddLoggingSetup(
        this ILoggingBuilder builder,
        ObservabilityOptions options,
        string environmentName,
        IEnvironmentAccessor environment,
        ISystemInfo systemInfo)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(systemInfo);

        builder.AddConsole();

        if (!options.Enabled || !options.EnableLogging)
        {
            return builder;
        }

        var resourceBuilder = ObservabilityResourceBuilder.Build(
            options,
            environmentName,
            environment,
            systemInfo);

        builder.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resourceBuilder);

            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;

            logging.AddOtlpExporter();
        });

        return builder;
    }
}
