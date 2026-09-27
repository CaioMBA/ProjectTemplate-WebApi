using Domain.Abstractions;
using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using OpenTelemetry.Resources;

namespace Observability.Resources;

public static class ObservabilityResourceBuilder
{
    public static ResourceBuilder Build(
        ObservabilityOptions options,
        string environmentName,
        IEnvironmentAccessor environment,
        ISystemInfo systemInfo)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(systemInfo);

        var fromEnvironment = ParseResourceAttributes(
            environment.GetVariable(ObservabilityEnvironment.ResourceAttributes));

        var serviceName =
            environment.GetVariable(ObservabilityEnvironment.ServiceName)
            ?? Resolve(fromEnvironment, "service.name")
            ?? options.ServiceName;

        var serviceNamespace =
            Resolve(fromEnvironment, "service.namespace")
            ?? options.ServiceNamespace;

        var serviceInstanceId =
            Resolve(fromEnvironment, "service.instance.id")
            ?? systemInfo.HostName;

        var deploymentEnvironment =
            Resolve(fromEnvironment, "deployment.environment.name")
            ?? options.DeploymentEnvironment
            ?? environmentName;

        var serviceVersion =
            Resolve(fromEnvironment, "service.version")
            ?? options.ServiceVersion
            ?? systemInfo.ApplicationVersion
            ?? "0.0.0";

        var builder = ResourceBuilder
            .CreateEmpty()
            .AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: serviceInstanceId)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("service.namespace", serviceNamespace),
                new KeyValuePair<string, object>("deployment.environment.name", deploymentEnvironment),
            ]);

        var extra = fromEnvironment
            .Where(pair => !IsPromotedAttribute(pair.Key))
            .Select(pair => new KeyValuePair<string, object>(pair.Key, pair.Value))
            .ToArray();

        if (extra.Length > 0)
        {
            builder.AddAttributes(extra);
        }

        return builder;
    }

    private static bool IsPromotedAttribute(string key) =>
        key is "service.name"
            or "service.namespace"
            or "service.instance.id"
            or "service.version"
            or "deployment.environment.name";

    private static string? Resolve(Dictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static Dictionary<string, string> ParseResourceAttributes(string? raw)
    {
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return parsed;
        }

        foreach (var pair in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator <= 0 || separator == pair.Length - 1)
            {
                continue;
            }

            parsed[pair[..separator].Trim()] = pair[(separator + 1)..].Trim();
        }

        return parsed;
    }
}
