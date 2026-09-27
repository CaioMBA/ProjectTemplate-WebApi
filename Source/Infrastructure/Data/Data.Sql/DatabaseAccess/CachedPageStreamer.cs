using System.Collections.Concurrent;
using System.Globalization;
using Domain.Extensions;
using Domain.Interfaces.Persistence;
using Domain.Models.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Data.Sql.DatabaseAccess;

public sealed class CachedPageStreamer(
    IServiceScopeFactory scopeFactory,
    Func<IDistributedCache> cacheAccessor,
    IHostApplicationLifetime lifetime,
    SqlPagedCacheOptions options,
    string databaseId,
    string cacheScope,
    ILogger<CachedPageStreamer> logger) : IAsyncDisposable
{
    private readonly Lazy<IDistributedCache> _cache = new(cacheAccessor);

    private readonly ConcurrentDictionary<string, WarmSession> _sessions = new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _shutdown =
        CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);

    private IDistributedCache Cache => _cache.Value;

    public async Task<IReadOnlyList<T>> GetPageAsync<T>(
        SqlPagedQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);

        var queryKey = BuildQueryKey(query);

        var cached = await Cache
            .GetValueAsync<List<T>>(PageKey(queryKey, query.Page, query.PageSize), cancellationToken)
            .ConfigureAwait(false);

        if (cached is not null)
        {
            return cached;
        }

        var backgroundIsUnsafe = query.Transaction is not null
            || !query.WarmRemainingPages
            || query.Page > options.MaxCachedPages;

        if (backgroundIsUnsafe)
        {
            return await StreamBoundedAsync<T>(queryKey, query, cancellationToken).ConfigureAwait(false);
        }

        var session = _sessions.GetOrAdd(queryKey, key => StartSession<T>(key, query));

        var completion = session.Pages.GetOrAdd(
            query.Page,
            _ => new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));

        var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        return (IReadOnlyList<T>)result;
    }

    public async Task WarmAsync<T>(SqlPagedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var queryKey = BuildQueryKey(query);

        await foreach (var page in EnumeratePagesAsync<T>(query, cancellationToken).ConfigureAwait(false))
        {
            await CachePageAsync(queryKey, page, query, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);

        var running = _sessions.Values.Select(session => session.Runner).ToArray();

        if (running.Length > 0)
        {
            try
            {
                await Task.WhenAll(running)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "{Count} cache warm task(s) did not finish before shutdown.",
                    running.Length);
            }
        }

        _shutdown.Dispose();
    }

    private WarmSession StartSession<T>(string queryKey, SqlPagedQuery query)
    {
        var session = new WarmSession();

        session.Runner = Task.Run(
            () => RunSessionAsync<T>(queryKey, query, session),
            CancellationToken.None);

        return session;
    }

    private async Task RunSessionAsync<T>(string queryKey, SqlPagedQuery query, WarmSession session)
    {
        try
        {
            await foreach (var page in EnumeratePagesAsync<T>(query, _shutdown.Token).ConfigureAwait(false))
            {
                await CachePageAsync(queryKey, page, query, _shutdown.Token).ConfigureAwait(false);

                if (session.Pages.TryGetValue(page.Number, out var waiting))
                {
                    waiting.TrySetResult(page.Items);
                }

                if (page.Number >= options.MaxCachedPages)
                {
                    logger.LogInformation(
                        "Stopped warming {CacheKey} at the {MaxCachedPages} page ceiling.",
                        queryKey,
                        options.MaxCachedPages);

                    break;
                }
            }

            foreach (var pending in session.Pages.Values)
            {
                pending.TrySetResult(Array.Empty<T>());
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Cache warm failed for {CacheKey}; pages beyond the requested one are not cached.",
                queryKey);

            foreach (var pending in session.Pages.Values)
            {
                pending.TrySetException(exception);
            }
        }
        finally
        {
            _sessions.TryRemove(queryKey, out _);
        }
    }

    private async Task<IReadOnlyList<T>> StreamBoundedAsync<T>(
        string queryKey,
        SqlPagedQuery query,
        CancellationToken cancellationToken)
    {
        await foreach (var page in EnumeratePagesAsync<T>(query, cancellationToken).ConfigureAwait(false))
        {
            await CachePageAsync(queryKey, page, query, cancellationToken).ConfigureAwait(false);

            if (page.Number == query.Page)
            {
                return page.Items;
            }

            if (page.Number >= query.Page)
            {
                break;
            }
        }

        return [];
    }

    private async IAsyncEnumerable<Page<T>> EnumeratePagesAsync<T>(
        SqlPagedQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var number = 1;
        var buffer = new List<T>(query.PageSize);

        await foreach (var item in StreamAsync<T>(query, cancellationToken).ConfigureAwait(false))
        {
            buffer.Add(item);

            if (buffer.Count < query.PageSize)
            {
                continue;
            }

            yield return new Page<T>(number, [.. buffer]);

            number++;
            buffer.Clear();
        }

        if (buffer.Count > 0)
        {
            yield return new Page<T>(number, [.. buffer]);
        }
    }

    private async IAsyncEnumerable<T> StreamAsync<T>(
        SqlPagedQuery query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (query.Transaction is not null)
        {
            var scoped = scopeFactory.CreateScope();

            try
            {
                var access = scoped.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(databaseId);

                await foreach (var item in access
                    .StreamAsync<T>(query.Sql, query.Parameters, query.Transaction, cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return item;
                }
            }
            finally
            {
                scoped.Dispose();
            }

            yield break;
        }

        await using var scope = scopeFactory.CreateAsyncScope();

        var isolated = scope.ServiceProvider.GetRequiredKeyedService<ISqlDatabaseAccess>(databaseId);

        await foreach (var item in isolated
            .StreamAsync<T>(query.Sql, query.Parameters, transaction: null, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private async Task CachePageAsync<T>(
        string queryKey,
        Page<T> page,
        SqlPagedQuery query,
        CancellationToken cancellationToken) =>
        await Cache.SetValueAsync(
            PageKey(queryKey, page.Number, query.PageSize),
            page.Items,
            query.CacheTtl ?? TimeSpan.FromMinutes(options.DefaultTtlMinutes),
            cancellationToken).ConfigureAwait(false);

    private static string PageKey(string queryKey, int page, int pageSize) =>
        string.Create(CultureInfo.InvariantCulture, $"{queryKey}:page:{page}:size:{pageSize}");

    private string BuildQueryKey(SqlPagedQuery query)
    {
        var parameters = query.Parameters?.ToJson() ?? "null";

        var raw = string.Create(
            CultureInfo.InvariantCulture,
            $"{cacheScope}|{query.CacheScope ?? string.Empty}|{query.Sql}|{parameters}");

        return $"sqlpage:{raw.ToSha256()}";
    }

    private sealed record Page<T>(int Number, IReadOnlyList<T> Items);

    private sealed class WarmSession
    {
        public ConcurrentDictionary<int, TaskCompletionSource<object>> Pages { get; } = new();

        public Task Runner { get; set; } = Task.CompletedTask;
    }
}
