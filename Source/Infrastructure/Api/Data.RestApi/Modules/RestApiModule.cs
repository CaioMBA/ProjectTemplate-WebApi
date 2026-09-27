using Data.RestApi.Setup;
using Domain.Enums;
using Domain.Interfaces.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Data.RestApi.Modules;

public sealed class RestApiModule : InfrastructureModuleBase
{
    public override string Name => "RestApi";

    protected override IEnumerable<string> Entries(InfrastructureModuleContext context) =>
        context.Settings.Apis
            .Where(api => api.Protocol == ApiProtocolType.Rest)
            .Select(api => api.Id);

    protected override void RegisterModule(
        IServiceCollection services,
        InfrastructureModuleContext context) =>
        services.AddDataRestApiSetup();
}