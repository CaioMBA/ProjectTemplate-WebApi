namespace Domain.Models.Configuration;

public sealed record StartupSettings(
    AppSettings Settings,
    ApiOptions Api,
    ObservabilityOptions Observability,
    string EnvironmentName);