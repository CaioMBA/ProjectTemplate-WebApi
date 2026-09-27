using System.Collections.Concurrent;
using Domain.Connections;
using Domain.Extensions;
using Domain.Interfaces.Messaging;
using Domain.Models.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Behaviors;

public sealed class CachingBehavior<TRequest, TResponse>(
    IServiceProvider serviceProvider,
    IOptionsMonitor<AppSettings> settingsMonitor,
    ILogger<CachingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(StringComparer.Ordinal);

    private static readonly string _requestName = typeof(TRequest).Name;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (request is ICacheInvalidatingRequest invalidating)
        {
            var response = await next(cancellationToken).ConfigureAwait(false);

            if (response is Domain.Results.Result { IsSuccess: true })
            {
                await EvictAsync(ResolveCache(invalidating), invalidating, cancellationToken).ConfigureAwait(false);
            }

            return response;
        }

        if (request is not ICacheableRequest cacheable)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        var key = cacheable.CacheKey;

        var cache = ResolveCache(cacheable);

        var cached = await cache.GetValueAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);

        if (cached is not null)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Cache hit for {RequestName} ({CacheKey}).", _requestName, key);
            }

            return cached;
        }

        var keyLock = _keyLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));

        await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cached = await cache.GetValueAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);

            if (cached is not null)
            {
                return cached;
            }

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Cache miss for {RequestName} ({CacheKey}).", _requestName, key);
            }

            var response = await next(cancellationToken).ConfigureAwait(false);

            if (response is Domain.Results.Result { IsSuccess: true })
            {
                var duration = cacheable.CacheDuration ?? DefaultDuration(cacheable.CacheId);

                await cache
                    .SetValueAsync(key, response, duration, cancellationToken)
                    .ConfigureAwait(false);
            }

            return response;
        }
        finally
        {
            keyLock.Release();

            if (keyLock.CurrentCount == 1)
            {
                _keyLocks.TryRemove(key, out _);
            }
        }
    }

    private IDistributedCache ResolveCache(ICacheRequest request) =>
        serviceProvider.RequireKeyed<IDistributedCache>(request.CacheId, KeyedConnections.Caches);

    private TimeSpan DefaultDuration(string cacheId)
    {
        var selected = settingsMonitor.CurrentValue.FindCache(cacheId);

        return TimeSpan.FromMinutes(selected?.DefaultTtlMinutes ?? CacheSettings.FallbackTtlMinutes);
    }

    private async Task EvictAsync(
        IDistributedCache cache,
        ICacheInvalidatingRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var key in request.CacheKeysToEvict.Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            await cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Evicted {CacheKey} after {RequestName}.", key, _requestName);
            }
        }
    }
}
