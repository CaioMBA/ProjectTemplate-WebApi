using Domain.Enums;
using Domain.Interfaces.Caching;

namespace Data.Cache.Providers;

public static class CacheProviderRegistry
{
    private static readonly ICacheStoreProvider[] _providers =
    [
        new MemoryCacheStoreProvider(),
        new RedisCacheStoreProvider(),
    ];

    public static IReadOnlyList<ICacheStoreProvider> All => _providers;

    public static ICacheStoreProvider Resolve(CacheType providerType) =>
        _providers.SingleOrDefault(provider => provider.ProviderType == providerType)
        ?? throw new NotSupportedException(
            $"CacheType '{providerType}' has no ICacheStoreProvider. "
            + $"Registered providers: {string.Join(", ", _providers.Select(p => p.ProviderType))}.");
}
