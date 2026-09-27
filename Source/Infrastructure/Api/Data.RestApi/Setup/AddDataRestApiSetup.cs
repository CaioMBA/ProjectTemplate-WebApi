using Data.RestApi.Clients;
using Domain.Enums;
using Domain.Interfaces.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Data.RestApi.Setup;

public static class DataRestApiSetup
{
    public static IServiceCollection AddDataRestApiSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(NamedHttpClient.RestApi.ToString());

        services.AddScoped<IRestApiClient, RestApiClient>();

        return services;
    }
}
