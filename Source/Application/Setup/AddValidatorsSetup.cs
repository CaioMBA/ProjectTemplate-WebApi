using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Setup;

public static class ValidatorsSetup
{
    public static IServiceCollection AddValidatorsSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatorsFromAssembly(
            Assembly.GetExecutingAssembly(),
            lifetime: ServiceLifetime.Scoped,
            includeInternalTypes: true);

        return services;
    }
}
