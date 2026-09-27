using Data.Broker.Setup;
using Domain.Interfaces.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Data.Broker.Modules;

public sealed class BrokerModule : InfrastructureModuleBase
{
    public override string Name => "Broker";

    protected override IEnumerable<string> Entries(InfrastructureModuleContext context) =>
        context.Settings.Brokers.Select(broker => $"{broker.Type}@{broker.Id}");

    protected override void RegisterModule(
        IServiceCollection services,
        InfrastructureModuleContext context) =>
        services.AddDataBrokerSetup(context.Settings);
}