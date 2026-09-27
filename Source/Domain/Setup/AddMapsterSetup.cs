using System.Reflection;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace Domain.Setup;

public static class MapsterSetup
{
    public static IServiceCollection AddMapsterSetup(
        this IServiceCollection services,
        bool compileEagerly = true)
    {
        ArgumentNullException.ThrowIfNull(services);

        var config = new TypeAdapterConfig();

        config.Scan(Assembly.GetExecutingAssembly());

        config.Default.IgnoreNullValues(false);

        if (compileEagerly)
        {
            config.Compile();
        }

        services.AddSingleton(config);

        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }
}
