using Domain.Abstractions;
using Domain.Interfaces.Platform;
using Domain.Models.Configuration;

namespace Domain.Setup;

public static class ObservabilityPreparation
{
    private static readonly string[] _protocols = ["grpc", "http/protobuf", "http"];

    public static void ApplyEnvironment(
        ObservabilityOptions options,
        string environmentName,
        IEnvironmentAccessor environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var endpoint = environment.GetVariable(ObservabilityEnvironment.OtlpEndpoint);

        if (endpoint is not null && Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed))
        {
            options.OtlpEndpoint = parsed;
        }

        var protocol = environment.GetVariable(ObservabilityEnvironment.OtlpProtocol);

        if (!string.IsNullOrWhiteSpace(protocol))
        {
            options.OtlpProtocol = protocol;
        }

        var serviceName = environment.GetVariable(ObservabilityEnvironment.ServiceName);

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            options.ServiceName = serviceName;
        }

        var healthPath = environment.GetVariable(ObservabilityEnvironment.HealthCheckPath);

        if (!string.IsNullOrWhiteSpace(healthPath))
        {
            options.HealthChecks.Path = healthPath;
        }

        options.DeploymentEnvironment ??= environmentName;
    }

    public static IReadOnlyList<string> Validate(ObservabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        const string root = ObservabilityOptions.SectionName;
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            errors.Add($"{root}:ServiceName is required.");
        }

        if (!_protocols.Contains(options.OtlpProtocol, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"{root}:OtlpProtocol '{options.OtlpProtocol}' must be one of: {string.Join(", ", _protocols)}.");
        }

        if (options.TraceSamplingRatio is < 0 or > 1)
        {
            errors.Add($"{root}:TraceSamplingRatio must be between 0 and 1.");
        }

        var health = options.HealthChecks;

        if (health.TimeoutSeconds < 1)
        {
            errors.Add($"{root}:HealthChecks:TimeoutSeconds must be at least 1.");
        }

        if (health.UiEvaluationSeconds < 1)
        {
            errors.Add($"{root}:HealthChecks:UiEvaluationSeconds must be at least 1.");
        }

        var paths = new List<(string Key, string Value)>
        {
            ("Path", health.Path),
            ("LivenessPath", health.LivenessPath),
            ("ReadinessPath", health.ReadinessPath),
        };

        if (health.UiEnabled)
        {
            paths.Add(("UiPath", health.UiPath));
            paths.Add(("UiApiPath", health.UiApiPath));
        }

        foreach (var (key, value) in paths.Where(path => string.IsNullOrWhiteSpace(path.Value) || path.Value[0] != '/'))
        {
            errors.Add($"{root}:HealthChecks:{key} '{value}' must start with '/'.");
        }

        foreach (var clash in paths.GroupBy(path => path.Value, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            errors.Add(
                $"{root}:HealthChecks:{string.Join(" and ", clash.Select(path => path.Key))} " +
                $"all map '{clash.Key}'; each endpoint needs its own path.");
        }

        return errors;
    }
}