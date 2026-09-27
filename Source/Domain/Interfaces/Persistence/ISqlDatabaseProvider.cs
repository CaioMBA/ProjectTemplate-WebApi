using System.Data.Common;
using Domain.Enums;
using Domain.Models.Configuration;

namespace Domain.Interfaces.Persistence;

public interface ISqlDatabaseProvider
{
    DatabaseType ProviderType { get; }

    int DefaultPort { get; }

    ISqlDialect Dialect { get; }

    Type ContextType { get; }

    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    string BuildConnectionString(DatabaseSettings connection);

    DbConnection CreateConnection(string connectionString);

    void ConfigureDapperTypeHandlers();
}