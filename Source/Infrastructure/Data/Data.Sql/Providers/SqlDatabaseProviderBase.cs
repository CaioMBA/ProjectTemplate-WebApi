using Data.Sql.EntityFrameworkContexts;
using Data.Sql.EntityFrameworkContexts.Interceptors;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Data.Sql.Providers;

public abstract class SqlDatabaseProviderBase<TContext> : SqlDatabaseProvider
    where TContext : AppDbContext
{
    public override Type ContextType => typeof(TContext);

    public override void ConfigureOptions(DbContextOptionsBuilder builder, SqlProviderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registration);

        ConfigureProvider(builder, registration);

        if (!registration.RecordStatements)
        {
            return;
        }

        builder.EnableSensitiveDataLogging();
        builder.EnableDetailedErrors();
    }

    public override AppDbContext CreateContext(IServiceProvider scope, SqlProviderRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(registration);

        var builder = new DbContextOptionsBuilder<TContext>()
            .UseApplicationServiceProvider(scope)
            .UseLoggerFactory(scope.GetService<ILoggerFactory>());

        ConfigureOptions(builder, registration);

        builder.AddInterceptors(
            scope.GetRequiredService<AuditingInterceptor>(),
            scope.GetRequiredService<SoftDeleteInterceptor>(),
            scope.GetRequiredService<DomainEventDispatchInterceptor>());

        return (TContext)(Activator.CreateInstance(typeof(TContext), builder.Options)
            ?? throw new InvalidOperationException($"Could not construct {typeof(TContext).Name}."));
    }
    protected abstract void ConfigureProvider(
        DbContextOptionsBuilder builder,
        SqlProviderRegistration registration);

    protected int PortOf(DatabaseSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return connection.Port ?? DefaultPort;
    }

    protected static string MigrationsAssemblyName =>
        typeof(AppDbContext).Assembly.FullName
        ?? throw new InvalidOperationException("Data.Sql assembly has no full name.");
}
