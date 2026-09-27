using Domain.Interfaces.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Application.Setup;

public static class JobsSetup
{
    public static IServiceCollection AddJobsSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var jobs = typeof(JobsSetup).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IBackgroundJob).IsAssignableFrom(type));

        foreach (var job in jobs)
        {
            services.TryAddScoped(job);

            if (typeof(IRecurringJob).IsAssignableFrom(job))
            {
                services.AddScoped(typeof(IRecurringJob), provider => provider.GetRequiredService(job));
            }
        }

        return services;
    }
}