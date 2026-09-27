using Microsoft.Extensions.Caching.Distributed;

namespace Domain.Extensions;

public static class DistributedCacheExtensions
{
    public static async ValueTask<T?> GetValueAsync<T>(
        this IDistributedCache cache,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var bytes = await cache.GetAsync(key, cancellationToken).ConfigureAwait(false);

        if (bytes is null || bytes.Length == 0)
        {
            return default;
        }

        try
        {
            return bytes.FromJsonBytes<T>();
        }
        catch (System.Text.Json.JsonException)
        {
            return default;
        }
    }

    public static async ValueTask SetValueAsync<T>(
        this IDistributedCache cache,
        string key,
        T value,
        TimeSpan absoluteExpirationRelativeToNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(absoluteExpirationRelativeToNow, TimeSpan.Zero);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow,
        };

        await cache
            .SetAsync(key, value.ToJsonBytes(), options, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async ValueTask<T?> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan absoluteExpirationRelativeToNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var cached = await cache.GetValueAsync<T>(key, cancellationToken).ConfigureAwait(false);

        if (cached is not null)
        {
            return cached;
        }

        var produced = await factory(cancellationToken).ConfigureAwait(false);

        if (produced is not null)
        {
            await cache
                .SetValueAsync(key, produced, absoluteExpirationRelativeToNow, cancellationToken)
                .ConfigureAwait(false);
        }

        return produced;
    }

    public static string BuildKey(string prefix, params ReadOnlySpan<string?> parts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        if (parts.Length == 0)
        {
            return prefix;
        }

        var joined = string.Join('|', parts.ToArray().Select(part => part ?? "\u2205"));

        return $"{prefix}:{joined.ToSha256()}";
    }
}
