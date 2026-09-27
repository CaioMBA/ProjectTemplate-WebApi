using Data.Sql.EntityFrameworkContexts;
using Data.Sql.Outbox;
using Data.Sql.Repositories;
using Domain.Interfaces.Persistence;
using Domain.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Data.Sql.Setup;

public static class RepositoriesSetup
{
    public static IServiceCollection AddRepositoriesSetup(this IServiceCollection services, string databaseId)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseId);

        services.AddKeyedScoped(typeof(IRepository<,>), databaseId, typeof(Repository<,>));

        services.AddKeyedScoped<IUnitOfWork>(databaseId, (provider, key) =>
            new UnitOfWork(provider.GetRequiredKeyedService<AppDbContext>(key)));

        services.AddKeyedScoped<IOutboxWriter>(databaseId, (provider, key) => new OutboxWriter(
            provider.GetRequiredKeyedService<AppDbContext>(key),
            provider.GetRequiredService<IDateTimeProvider>()));

        services.AddKeyedScoped<IOutboxMaintenance>(databaseId, (provider, key) =>
            new OutboxMaintenance(provider.GetRequiredKeyedService<AppDbContext>(key)));

        return services;
    }
}