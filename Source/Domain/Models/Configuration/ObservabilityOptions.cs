using System.Diagnostics.CodeAnalysis;

namespace Domain.Models.Configuration;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool Enabled { get; set; } = true;

    [SuppressMessage(
        "Security",
        "S5332:Using http protocol is insecure. Use https instead.",
        Justification = "Alloy is reachable only on the encrypted `shared-network` Docker " +
                        "overlay and terminates no TLS; it is never exposed to a host port. " +
                        "Forcing https here would break telemetry export outright.")]
    public Uri OtlpEndpoint { get; set; } = new("http://monitoring_alloy:4317");

    public string OtlpProtocol { get; set; } = "grpc";

    public string ServiceName { get; set; } = "webapi-template";

    public string ServiceNamespace { get; set; } = "homelab";

    public string? ServiceVersion { get; set; }

    public string? DeploymentEnvironment { get; set; }

    public bool EnableTracing { get; set; } = true;

    public bool EnableMetrics { get; set; } = true;

    public bool EnableLogging { get; set; } = true;

    public double TraceSamplingRatio { get; set; } = 1.0;

    public bool RecordDatabaseStatements { get; set; }

    public PyroscopeOptions Pyroscope { get; set; } = new();

    public HealthCheckOptionsModel HealthChecks { get; set; } = new();
}

public sealed class PyroscopeOptions
{
    public bool Enabled { get; set; }

    [SuppressMessage(
        "Security",
        "S5332:Using http protocol is insecure. Use https instead.",
        Justification = "Internal-only service on the encrypted `shared-network` overlay, " +
                        "not published to any host port and serving no TLS listener.")]
    public Uri ServerAddress { get; set; } = new("http://monitoring_pyroscope:4040");
}

public sealed class HealthCheckOptionsModel
{
    public string Path { get; set; } = "/health";

    public string LivenessPath { get; set; } = "/live";

    public string ReadinessPath { get; set; } = "/ready";

    public bool ExposeDetails { get; set; }

    public int TimeoutSeconds { get; set; } = 5;

    public bool UiEnabled { get; set; }

    public string UiPath { get; set; } = "/health-ui";

    public string UiApiPath { get; set; } = "/health-ui-api";

    public int UiEvaluationSeconds { get; set; } = 30;
}
