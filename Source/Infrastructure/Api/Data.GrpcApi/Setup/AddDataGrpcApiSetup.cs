using Data.GrpcApi.Channels;
using Data.GrpcApi.Clients;
using Domain.Enums;
using Domain.Interfaces.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Data.GrpcApi.Setup;

public static class DataGrpcApiSetup
{
    public static IServiceCollection AddDataGrpcApiSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(NamedHttpClient.GrpcApi.ToString())
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<GrpcChannelPool>();

        services.AddScoped<IGrpcApiClient, GrpcApiClient>();

        return services;
    }
}
