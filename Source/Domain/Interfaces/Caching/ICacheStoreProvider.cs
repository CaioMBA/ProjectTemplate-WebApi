using Domain.Enums;
using Domain.Models.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Domain.Interfaces.Caching;

public interface ICacheStoreProvider
{
    CacheType ProviderType { get; }

    bool IsDistributed { get; }

    void Register(IServiceCollection services, CacheSettings connection);

    void Validate(CacheSettings connection);
}
