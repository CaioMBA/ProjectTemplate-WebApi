using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Scheduling.Jobs;

public sealed class SchedulerHealthCheck(JobStorage storage, TimeProvider clock) : IHealthCheck
{
    private static readonly TimeSpan _heartbeatWindow = TimeSpan.FromMinutes(2);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var monitoring = storage.GetMonitoringApi();
        var now = clock.GetUtcNow().UtcDateTime;

        var alive = monitoring.Servers().Count(server => server.Heartbeat is { } beat && now - beat < _heartbeatWindow);
        var statistics = monitoring.GetStatistics();

        var data = new Dictionary<string, object>
        {
            ["servers"] = alive,
            ["enqueued"] = statistics.Enqueued,
            ["failed"] = statistics.Failed,
            ["recurring"] = statistics.Recurring,
        };

        return Task.FromResult(alive > 0
            ? HealthCheckResult.Healthy($"{alive} job server(s) alive.", data)
            : new HealthCheckResult(context.Registration.FailureStatus, "No job server has sent a heartbeat in the last 2 minutes.", data: data));
    }
}