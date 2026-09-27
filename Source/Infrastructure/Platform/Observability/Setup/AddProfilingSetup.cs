using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using Microsoft.Extensions.Logging;
using Observability.Profiling;
using OpenTelemetry.Trace;
using Pyroscope.OpenTelemetry;

namespace Observability.Setup;

public static class ProfilingSetup
{
    public static TracerProviderBuilder AddProfilingSetup(
        this TracerProviderBuilder builder,
        ObservabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Pyroscope.Enabled)
        {
            return builder;
        }

        return builder.AddProcessor(new PyroscopeSpanProcessor());
    }

    public static void ReportProfilingState(
        ObservabilityOptions options,
        IEnvironmentAccessor environment,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        if (!options.Pyroscope.Enabled)
        {
            return;
        }

        var profilerAttached = string.Equals(
            environment.GetVariable(PyroscopeEnvironment.EnableProfiling),
            "1",
            StringComparison.Ordinal);

        if (profilerAttached)
        {
            logger.LogInformation(
                "Pyroscope profiling is active; profiles are pushed directly to {ServerAddress}.",
                options.Pyroscope.ServerAddress);

            return;
        }

        logger.LogWarning(
            "Observability:Pyroscope:Enabled is true but the native profiler is not attached, "
            + "so no profiles will be produced. Set these container environment variables: "
            + "{RequiredEnvironmentVariables}.",
            string.Join(
                ", ",
                PyroscopeEnvironment.EnableProfiling,
                PyroscopeEnvironment.ProfilerGuid,
                PyroscopeEnvironment.ServerAddress,
                PyroscopeEnvironment.ApplicationName));
    }
}
