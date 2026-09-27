using Data.Broker.Providers;
using Domain.Interfaces.Broker;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Data.Broker.Setup;

public static class DataBrokerSetup
{
    public static IServiceCollection AddDataBrokerSetup(
        this IServiceCollection services,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        foreach (var connection in settings.Brokers)
        {
            var provider = BrokerProviderRegistry.Resolve(connection.Type);

            services.AddKeyedSingleton<IBrokerProvider>(connection.Id, provider);

            provider.Register(services, connection);
        }

        return services;
    }
}