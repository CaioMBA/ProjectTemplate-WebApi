using Data.GraphqlApi.Clients;
using Domain.Enums;
using Domain.Interfaces.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Data.GraphqlApi.Setup;

public static class DataGraphqlApiSetup
{
    public static IServiceCollection AddDataGraphqlApiSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(NamedHttpClient.GraphqlApi.ToString());

        services.AddScoped<IGraphqlApiClient, GraphqlApiClient>();

        return services;
    }
}
