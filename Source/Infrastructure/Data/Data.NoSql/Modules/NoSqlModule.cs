using Data.NoSql.Setup;
using Domain.Enums;
using Domain.Interfaces.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Data.NoSql.Modules;

public sealed class NoSqlModule : InfrastructureModuleBase
{
    public override string Name => "NoSql";

    protected override IEnumerable<string> Entries(InfrastructureModuleContext context) =>
        context.Settings.Databases
            .Where(database => database.Type.Family() == DatabaseFamily.Document)
            .Select(database => $"{database.Type}@{database.Id}");

    protected override void RegisterModule(
        IServiceCollection services,
        InfrastructureModuleContext context) =>
        services.AddDataNoSqlSetup(context.Settings);
}