namespace Domain.Abstractions;

public static class ObservabilityEnvironment
{
    public const string OtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public const string OtlpProtocol = "OTEL_EXPORTER_OTLP_PROTOCOL";

    public const string ServiceName = "OTEL_SERVICE_NAME";

    public const string ResourceAttributes = "OTEL_RESOURCE_ATTRIBUTES";

    public const string HealthCheckPath = "HEALTHCHECK_PATH";

    public const string HostName = "HOSTNAME";

    public const string RunningInContainer = "DOTNET_RUNNING_IN_CONTAINER";
}
