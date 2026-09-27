using Data.GraphqlApi.Setup;
using Domain.Enums;
using Domain.Interfaces.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Data.GraphqlApi.Modules;

public sealed class GraphqlApiModule : InfrastructureModuleBase
{
    public override string Name => "GraphqlApi";

    protected override IEnumerable<string> Entries(InfrastructureModuleContext context) =>
        context.Settings.Apis
            .Where(api => api.Protocol == ApiProtocolType.GraphQl)
            .Select(api => api.Id);

    protected override void RegisterModule(
        IServiceCollection services,
        InfrastructureModuleContext context) =>
        services.AddDataGraphqlApiSetup();
}