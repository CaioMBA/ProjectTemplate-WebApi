using Domain.Interfaces.Scheduling;
using Hangfire;

namespace Scheduling.Jobs;

public sealed class HangfireJobScheduler(
    IBackgroundJobClient client,
    IRecurringJobManager recurring) : IJobScheduler
{
    public string Enqueue<TJob>()
        where TJob : IBackgroundJob =>
        client.Enqueue<BackgroundJobRunner>(runner => runner.RunAsync(NameOf<TJob>(), CancellationToken.None));

    public string Schedule<TJob>(TimeSpan delay)
        where TJob : IBackgroundJob =>
        client.Schedule<BackgroundJobRunner>(runner => runner.RunAsync(NameOf<TJob>(), CancellationToken.None), delay);

    public void Trigger(string recurringJobId) => recurring.Trigger(recurringJobId);

    private static string NameOf<TJob>() =>
        typeof(TJob).AssemblyQualifiedName
        ?? throw new InvalidOperationException($"{typeof(TJob)} has no assembly-qualified name.");
}