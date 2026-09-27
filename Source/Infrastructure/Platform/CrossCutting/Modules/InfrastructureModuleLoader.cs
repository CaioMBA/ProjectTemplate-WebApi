using Data.Broker.Modules;
using Data.Cache.Modules;
using Data.GraphqlApi.Modules;
using Data.GrpcApi.Modules;
using Data.NoSql.Modules;
using Data.RestApi.Modules;
using Data.Sql.Modules;
using Domain.Interfaces.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scheduling.Modules;

namespace CrossCutting.Modules;

public sealed class InfrastructureModuleLoader
{
    public InfrastructureModuleLoader()
        : this(
            [
                new SqlModule(),
                new CacheModule(),
                new NoSqlModule(),
                new BrokerModule(),
                new RestApiModule(),
                new GraphqlApiModule(),
                new GrpcApiModule(),
                new SchedulingModule(),
            ])
    {
    }

    public InfrastructureModuleLoader(IReadOnlyList<IInfrastructureModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var duplicates = modules
            .GroupBy(module => module.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate infrastructure module names: {string.Join(", ", duplicates)}.");
        }

        Modules = modules;
    }

    public IReadOnlyList<IInfrastructureModule> Modules { get; }

    public IReadOnlyList<string> Load(
        IServiceCollection services,
        InfrastructureModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        var loaded = new List<string>();

        foreach (var module in Modules.Where(module => module.IsEnabled(context)))
        {
            module.Register(services, context);

            loaded.Add(module.Describe(context));
        }

        services.AddSingleton(this);

        return loaded;
    }

    public IReadOnlyList<string> Load(
        IServiceCollection services,
        InfrastructureModuleContext context,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var loaded = Load(services, context);

        logger.LogInformation(
            "Infrastructure modules enabled: {EnabledModules}.",
            loaded.Count > 0 ? string.Join(", ", loaded) : "none");

        return loaded;
    }
}
