using System.Globalization;
using Domain.Abstractions;
using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Observability.Resources;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Observability.Setup;

public static class OpenTelemetrySetup
{
    public static IServiceCollection AddOpenTelemetrySetup(
        this IServiceCollection services,
        ObservabilityOptions options,
        string environmentName,
        IEnvironmentAccessor environment,
        ISystemInfo systemInfo)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(systemInfo);

        if (!options.Enabled)
        {
            return services;
        }

        var builder = services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                var configured = ObservabilityResourceBuilder
                    .Build(options, environmentName, environment, systemInfo)
                    .Build();

                resource.AddAttributes(configured.Attributes);
            });

        if (options.EnableTracing)
        {
            builder.WithTracing(tracing => ConfigureTracing(tracing, options, environment));
        }

        if (options.EnableMetrics)
        {
            builder.WithMetrics(metrics => ConfigureMetrics(metrics, options, environment));
        }

        return services;
    }

    private static void ConfigureTracing(
        TracerProviderBuilder tracing,
        ObservabilityOptions options,
        IEnvironmentAccessor environment)
    {
        tracing
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.TraceSamplingRatio)))
            .AddAspNetCoreInstrumentation(instrumentation =>
            {
                instrumentation.RecordException = true;

                instrumentation.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                    && !context.Request.Path.StartsWithSegments("/live", StringComparison.OrdinalIgnoreCase)
                    && !context.Request.Path.StartsWithSegments("/ready", StringComparison.OrdinalIgnoreCase)
                    && !context.Request.Path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase);
            })
            .AddHttpClientInstrumentation(instrumentation => instrumentation.RecordException = true)

            .AddSource(TelemetryNames.ApplicationActivitySource)
            .AddSource(TelemetryNames.IntegrationActivitySource)
            .AddSource(TelemetryNames.OutboxActivitySource)

            .AddProfilingSetup(options);

        AddOtlpExporter(
            options,
            environment,
            (endpoint, protocol) => tracing.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = endpoint;
                exporter.Protocol = protocol;
            }));
    }

    private static void ConfigureMetrics(
        MeterProviderBuilder metrics,
        ObservabilityOptions options,
        IEnvironmentAccessor environment)
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()

            .AddProcessInstrumentation()

            .AddMeter(TelemetryNames.ApplicationMeter)
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("System.Net.Http");

        AddOtlpExporter(
            options,
            environment,
            (endpoint, protocol) => metrics.AddOtlpExporter((exporter, reader) =>
            {
                exporter.Endpoint = endpoint;
                exporter.Protocol = protocol;

                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 15_000;
            }));
    }

    private static void AddOtlpExporter(
        ObservabilityOptions options,
        IEnvironmentAccessor environment,
        Action<Uri, OtlpExportProtocol> configure)
    {
        var rawEndpoint = environment.GetVariable(ObservabilityEnvironment.OtlpEndpoint);

        var endpoint = Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var parsed)
            ? parsed
            : options.OtlpEndpoint;

        var rawProtocol =
            environment.GetVariable(ObservabilityEnvironment.OtlpProtocol)
            ?? options.OtlpProtocol;

        var protocol = rawProtocol.ToLower(CultureInfo.InvariantCulture) switch
        {
            "http/protobuf" or "http" => OtlpExportProtocol.HttpProtobuf,
            _ => OtlpExportProtocol.Grpc,
        };

        configure(endpoint, protocol);
    }
}
