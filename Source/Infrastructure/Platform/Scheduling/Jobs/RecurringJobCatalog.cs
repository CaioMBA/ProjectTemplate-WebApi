using Domain.Interfaces.Scheduling;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Jobs;

public sealed class RecurringJobCatalog
{
    private readonly Dictionary<string, (Type Type, string Cron)> _jobs;

    public RecurringJobCatalog(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        using var scope = scopeFactory.CreateScope();

        var jobs = scope.ServiceProvider.GetServices<IRecurringJob>().ToList();

        var duplicates = jobs
            .GroupBy(job => job.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' ({string.Join(", ", group.Select(job => job.GetType().Name))})")
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"Recurring job ids must be unique. Duplicated: {string.Join("; ", duplicates)}.");
        }

        _jobs = jobs.ToDictionary(job => job.Id, job => (job.GetType(), job.Cron), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Ids => _jobs.Keys;

    public string DefaultCron(string jobId) => Entry(jobId).Cron;

    public Type TypeOf(string jobId) => Entry(jobId).Type;

    public bool Contains(string jobId) => _jobs.ContainsKey(jobId);

    private (Type Type, string Cron) Entry(string jobId) =>
        _jobs.TryGetValue(jobId, out var entry)
            ? entry
            : throw new InvalidOperationException(
                $"No recurring job has Id '{jobId}'. Known ids: {string.Join(", ", _jobs.Keys)}.");
}