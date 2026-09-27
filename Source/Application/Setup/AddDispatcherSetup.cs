using System.Reflection;
using Application.Dispatching;
using Domain.Interfaces.Integration;
using Domain.Interfaces.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Setup;

public static class DispatcherSetup
{
    public static IServiceCollection AddDispatcherSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ISender, Sender>();
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        services.AddScoped<IIntegrationEventDispatcher, IntegrationEventDispatcher>();

        var assembly = Assembly.GetExecutingAssembly();

        RegisterClosedImplementations(services, assembly, typeof(IRequestHandler<,>));
        RegisterClosedImplementations(services, assembly, typeof(IDomainEventHandler<>));
        RegisterClosedImplementations(services, assembly, typeof(IIntegrationEventHandler<>));

        return services;
    }

    private static void RegisterClosedImplementations(
        IServiceCollection services,
        Assembly assembly,
        Type openGenericContract)
    {
        var candidates = assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

        foreach (var implementation in candidates)
        {
            var contracts = implementation
                .GetInterfaces()
                .Where(contract =>
                    contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == openGenericContract);

            foreach (var contract in contracts)
            {
                services.AddScoped(contract, implementation);
            }
        }
    }
}
