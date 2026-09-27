using Domain.Abstractions;
using Domain.Interfaces.Platform;
using Domain.Models.Configuration;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Observability.HealthChecks;

namespace Observability.Setup;

public static class UseObservabilitySetup
{
    public static WebApplication UseObservabilitySetupPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
        var healthOptions = options.HealthChecks;

        var environment = app.Services.GetRequiredService<IEnvironmentAccessor>();


        Func<HttpContext, HealthReport, Task> writer = healthOptions.ExposeDetails
            ? HealthCheckResponseWriter.WriteDetailedResponse
            : HealthCheckResponseWriter.WriteMinimalResponse;

        app.MapHealthChecks(healthOptions.Path, new HealthCheckOptions
        {
            ResponseWriter = writer,
        });

        app.MapHealthChecks(healthOptions.LivenessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Self),
            ResponseWriter = HealthCheckResponseWriter.WriteMinimalResponse,
        });

        app.MapHealthChecks(healthOptions.ReadinessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
            ResponseWriter = writer,
        });

        if (healthOptions.UiEnabled)
        {
            app.MapHealthChecks(healthOptions.UiApiPath, new HealthCheckOptions
            {
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
            });

            app.MapHealthChecksUI(ui =>
            {
                ui.UIPath = healthOptions.UiPath;
                ui.ApiPath = $"{healthOptions.UiPath.TrimEnd('/')}/api";
                ui.ResourcesPath = $"{healthOptions.UiPath.TrimEnd('/')}/resources";
                ui.WebhookPath = $"{healthOptions.UiPath.TrimEnd('/')}/webhooks";
            });
        }

        ProfilingSetup.ReportProfilingState(
            options,
            environment,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(ProfilingSetup)));

        return app;
    }
}
