using Domain.Enums;
using Domain.Interfaces.Modules;
using Microsoft.Extensions.DependencyInjection;
using Scheduling.Setup;

namespace Scheduling.Modules;

public sealed class SchedulingModule : InfrastructureModuleBase
{
    public override string Name => "Scheduling";

    protected override IEnumerable<string> Entries(InfrastructureModuleContext context)
    {
        var scheduling = context.Settings.Scheduling;

        if (!scheduling.Enabled)
        {
            return [];
        }

        return scheduling.Storage.Type == SchedulingStorageType.Memory
            ? ["Memory"]
            : [$"Database@{scheduling.Storage.DatabaseId}"];
    }

    protected override void RegisterModule(
        IServiceCollection services,
        InfrastructureModuleContext context) =>
        services.AddSchedulingSetup(context.Settings, context.Observability);
}