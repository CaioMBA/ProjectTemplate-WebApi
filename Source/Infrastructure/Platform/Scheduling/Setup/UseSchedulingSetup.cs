using Domain.Models.Configuration;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Setup;

public static class UseSchedulingSetup
{
    public static WebApplication UseSchedulingSetupPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var settings = app.Services.GetRequiredService<StartupSettings>().Settings;
        var scheduling = settings.Scheduling;

        if (!scheduling.Enabled || !scheduling.Dashboard.Enabled)
        {
            return app;
        }

        app.UseHangfireDashboard(scheduling.Dashboard.Path, new DashboardOptions
        {
            DashboardTitle = $"{settings.AppName} jobs",
            Authorization = [new LocalRequestsOnlyAuthorizationFilter()],
            IsReadOnlyFunc = _ => scheduling.Dashboard.ReadOnly,
            DisplayStorageConnectionString = false,
            AppPath = null,
        });

        return app;
    }
}