using System.Data.Common;
using Data.Sql.DynamicData;
using Data.Sql.EntityFrameworkContexts;
using Domain.Enums;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Domain.Models.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Data.Sql.Providers;

public abstract class SqlDatabaseProvider : ISqlDatabaseProvider
{
    public abstract DatabaseType ProviderType { get; }

    public abstract int DefaultPort { get; }

    public abstract ISqlDialect Dialect { get; }

    public abstract Type ContextType { get; }

    public virtual SqlCatalogReader? CatalogReader => null;

    public virtual bool IsAvailable => true;

    public virtual string? UnavailableReason => null;

    public abstract string BuildConnectionString(DatabaseSettings connection);

    public abstract DbConnection CreateConnection(string connectionString);

    public abstract void ConfigureOptions(DbContextOptionsBuilder builder, SqlProviderRegistration registration);

    public abstract AppDbContext CreateContext(IServiceProvider scope, SqlProviderRegistration registration);

    public virtual void ConfigureDapperTypeHandlers()
    {
    }
}