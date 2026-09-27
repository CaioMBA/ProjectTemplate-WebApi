using Domain.Enums;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Domain.Interfaces.Persistence;

public interface INoSqlDatabaseProvider
{
    DatabaseType ProviderType { get; }

    Type RepositoryType { get; }

    void Register(IServiceCollection services, DatabaseSettings connection);

    void Validate(DatabaseSettings connection);
}
