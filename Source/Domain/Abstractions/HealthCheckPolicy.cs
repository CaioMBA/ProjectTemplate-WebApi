using Domain.Enums;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Domain.Abstractions;

public static class HealthCheckPolicy
{
    public static HealthCheckRule For(DependencyKind kind) => kind switch
    {
        DependencyKind.Self => new(
            HealthStatus.Unhealthy,
            [HealthCheckTags.Self, HealthCheckTags.Ready]),
        DependencyKind.Database => new(
            HealthStatus.Unhealthy,
            [HealthCheckTags.Database, HealthCheckTags.Ready, HealthCheckTags.Critical]),
        DependencyKind.DocumentDatabase => new(
            HealthStatus.Unhealthy,
            [HealthCheckTags.Database, HealthCheckTags.NoSql, HealthCheckTags.Ready, HealthCheckTags.Critical]),
        DependencyKind.Broker => new(
            HealthStatus.Unhealthy,
            [HealthCheckTags.Broker, HealthCheckTags.Ready, HealthCheckTags.Critical]),
        DependencyKind.Cache => new(
            HealthStatus.Degraded,
            [HealthCheckTags.Cache]),
        DependencyKind.Api => new(
            HealthStatus.Degraded,
            [HealthCheckTags.Api]),
        DependencyKind.Scheduler => new(
            HealthStatus.Degraded,
            [HealthCheckTags.Scheduler]),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No health check rule for this dependency kind."),
    };

    public static HealthCheckRegistration Registration(
        DependencyKind kind,
        string name,
        Func<IServiceProvider, IHealthCheck> probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(probe);

        var rule = For(kind);

        return new HealthCheckRegistration(
            name,
            services => new DeferredHealthCheck(() => probe(services)),
            rule.FailureStatus,
            rule.Tags);
    }

    private sealed class DeferredHealthCheck(Func<IHealthCheck> probe) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            IHealthCheck check;

            try
            {
                check = probe();
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                return new HealthCheckResult(
                    context.Registration.FailureStatus,
                    "The entry could not be built from its configuration.",
                    exception);
            }

            return await check.CheckHealthAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}