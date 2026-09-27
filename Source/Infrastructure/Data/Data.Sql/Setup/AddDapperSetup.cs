using Dapper;
using Data.Sql.DapperHandlers;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Data.Sql.Setup;

public static class DapperSetup
{
    private static readonly Lock _registrationGate = new();
    private static readonly HashSet<DatabaseType> _providersWithHandlers = [];
    private static bool _registered;

    public static IServiceCollection AddDapperSetup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        lock (_registrationGate)
        {
            if (_registered)
            {
                return services;
            }

            DefaultTypeMap.MatchNamesWithUnderscores = true;

            SqlMapper.AddTypeHandler(new JsonObjectTypeHandler());
            SqlMapper.AddTypeHandler(new JsonArrayTypeHandler());
            SqlMapper.AddTypeHandler(new JsonNodeTypeHandler());
            SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
            SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
            SqlMapper.AddTypeHandler(new CrontabScheduleTypeHandler());
            SqlMapper.AddTypeHandler(new DictionaryTypeHandler());

            _registered = true;
        }

        return services;
    }

    public static void EnsureProviderTypeHandlers(ISqlDatabaseProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_registrationGate)
        {
            if (_providersWithHandlers.Add(provider.ProviderType))
            {
                provider.ConfigureDapperTypeHandlers();
            }
        }
    }
}