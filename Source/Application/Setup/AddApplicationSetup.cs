using Microsoft.Extensions.DependencyInjection;

namespace Application.Setup;

public static class ApplicationSetup
{
    public static IServiceCollection AddApplicationSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddDispatcherSetup()
            .AddBehaviorsSetup()
            .AddValidatorsSetup()
            .AddJobsSetup();
    }
}
