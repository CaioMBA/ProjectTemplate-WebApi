using System.ComponentModel;
using Domain.Interfaces.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Jobs;

public sealed class RecurringJobRunner(IServiceProvider services, RecurringJobCatalog catalog, IJobProgress progress)
{
    [DisplayName("{0}")]
    public Task RunAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = (IRecurringJob)services.GetRequiredService(catalog.TypeOf(jobId));

        return job.ExecuteAsync(progress, cancellationToken);
    }
}

public sealed class BackgroundJobRunner(IServiceProvider services, IJobProgress progress)
{
    [DisplayName("{0}")]
    public Task RunAsync(string jobType, CancellationToken cancellationToken)
    {
        var type = Type.GetType(jobType, throwOnError: true)!;

        if (!typeof(IBackgroundJob).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"'{jobType}' is not an {nameof(IBackgroundJob)}.");
        }

        var job = (IBackgroundJob)ActivatorUtilities.GetServiceOrCreateInstance(services, type);

        return job.ExecuteAsync(progress, cancellationToken);
    }
}