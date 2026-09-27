using CrossCutting.Configuration;
using CrossCutting.Services;
using Domain.Interfaces.Configuration;
using Domain.Interfaces.Identity;
using Domain.Interfaces.Platform;
using Domain.Interfaces.Services;
using Domain.Models.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrossCutting.Setup;

public static class ServicesSetup
{
    public static IServiceCollection AddServicesSetup(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingletonTimeProvider();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddSingleton<IFileSystem, PhysicalFileSystem>();

        services.AddSingleton<IEnvironmentAccessor, SystemEnvironmentAccessor>();

        services.AddSingleton<ISystemInfo, SystemInfo>();

        services.Configure<SecretResolutionOptions>(
            configuration.GetSection(SecretResolutionOptions.SectionName));

        services.AddSingleton<ISecretResolver>(provider => new SecretResolver(
            provider.GetRequiredService<IFileSystem>(),
            provider.GetRequiredService<IOptions<SecretResolutionOptions>>().Value));

        services
            .AddLastKnownGoodOptionsMonitor<AppSettings>()
            .AddLastKnownGoodOptionsMonitor<ApiOptions>()
            .AddLastKnownGoodOptionsMonitor<ObservabilityOptions>();

        services.AddHttpContextAccessor();

        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }

    public static IServiceCollection AddLastKnownGoodOptionsMonitor<TOptions>(this IServiceCollection services)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IOptionsMonitor<TOptions>, LastKnownGoodOptionsMonitor<TOptions>>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
