using Data.GrpcApi.Setup;
using Domain.Enums;
using Domain.Interfaces.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Data.GrpcApi.Modules;

public sealed class GrpcApiModule : InfrastructureModuleBase
{
    public override string Name => "GrpcApi";

    protected override IEnumerable<string> Entries(InfrastructureModuleContext context) =>
        context.Settings.Apis
            .Where(api => api.Protocol == ApiProtocolType.Grpc)
            .Select(api => api.Id);

    protected override void RegisterModule(
        IServiceCollection services,
        InfrastructureModuleContext context) =>
        services.AddDataGrpcApiSetup();
}