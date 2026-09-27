using Application.Behaviors;
using Domain.Interfaces.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Setup;

public static class BehaviorsSetup
{
    public static IServiceCollection AddBehaviorsSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TelemetryBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
