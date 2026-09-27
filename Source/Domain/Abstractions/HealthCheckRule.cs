using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Domain.Abstractions;

public sealed record HealthCheckRule(HealthStatus FailureStatus, IReadOnlyList<string> Tags)
{
    public bool IsReady => Tags.Contains(HealthCheckTags.Ready);
}