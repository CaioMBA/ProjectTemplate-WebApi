using Data.Sql.Setup;
using Domain.Enums;
using Domain.Interfaces.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Data.Sql.Modules;

public sealed class SqlModule : InfrastructureModuleBase
{
    public override string Name => "Sql";

    protected override IEnumerable<string> Entries(InfrastructureModuleContext context) =>
        context.Settings.Databases
            .Where(database => database.Type.Family() == DatabaseFamily.Relational)
            .Select(database => $"{database.Type}@{database.Id}");

    protected override void RegisterModule(
        IServiceCollection services,
        InfrastructureModuleContext context) =>
        services.AddDataSqlSetup(
            context.Settings,
            context.Observability.RecordDatabaseStatements);
}