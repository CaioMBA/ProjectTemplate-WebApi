namespace Domain.Interfaces.Scheduling;

public interface IBackgroundJob
{
    Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken);
}

public interface IRecurringJob : IBackgroundJob
{
    string Id { get; }

    string Cron { get; }
}

public interface IJobProgress
{
    void Report(double percent);
}

public interface IJobScheduler
{
    string Enqueue<TJob>()
        where TJob : IBackgroundJob;

    string Schedule<TJob>(TimeSpan delay)
        where TJob : IBackgroundJob;

    void Trigger(string recurringJobId);
}