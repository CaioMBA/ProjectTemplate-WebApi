using Domain.Abstractions;
using Domain.Enums;
using Domain.Models.Configuration;
using HealthChecks.Uris;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Observability.Setup;

public static class HealthChecksSetup
{
    public const string SelfCheckName = "self";

    public const string UiClientName = "health-ui";

    public static IServiceCollection AddHealthChecksSetup(
        this IServiceCollection services,
        AppSettings settings,
        HealthCheckOptionsModel options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        var builder = services.AddHealthChecks();

        builder.Add(HealthCheckPolicy.Registration(
            DependencyKind.Self,
            SelfCheckName,
            _ => new SelfHealthCheck()));

        var probeable = settings.Apis.Where(api =>
            api.BaseAddress is not null && !string.IsNullOrWhiteSpace(api.HealthEndpoint));

        foreach (var api in probeable)
        {
            var uri = new Uri(api.BaseAddress!, api.HealthEndpoint!.TrimStart('/'));
            var name = $"api:{api.Id}";

            services.AddHttpClient(name);

            builder.Add(HealthCheckPolicy.Registration(
                DependencyKind.Api,
                name,
                provider => new UriHealthCheck(
                    new UriHealthCheckOptions().AddUri(uri),
                    () => provider.GetRequiredService<IHttpClientFactory>().CreateClient(name))));
        }

        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        services.PostConfigure<HealthCheckServiceOptions>(serviceOptions =>
            ApplyTimeoutsAndGuard(serviceOptions, timeout));

        if (options.UiEnabled)
        {
            services
                .AddHealthChecksUI(ui =>
                {
                    ui.AddHealthCheckEndpoint(UiClientName, options.UiApiPath);
                    ui.SetEvaluationTimeInSeconds(options.UiEvaluationSeconds);
                    ui.MaximumHistoryEntriesPerEndpoint(50);
                })
                .AddInMemoryStorage();
        }

        return services;
    }

    public static void ApplyTimeoutsAndGuard(HealthCheckServiceOptions serviceOptions, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(serviceOptions);

        foreach (var registration in serviceOptions.Registrations)
        {
            if (registration.Tags.Contains(HealthCheckTags.Self) && registration.Name != SelfCheckName)
            {
                throw new InvalidOperationException(
                    $"Health check '{registration.Name}' carries the '{HealthCheckTags.Self}' tag. Only the " +
                    $"'{SelfCheckName}' check may: /live must never fail because a dependency is down.");
            }

            if (registration.Timeout == Timeout.InfiniteTimeSpan)
            {
                registration.Timeout = timeout;
            }
        }
    }

    private sealed class SelfHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy("The service is running."));
    }
}
