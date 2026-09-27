using Application.Behaviors;
using Domain.Enums;
using Domain.Interfaces.Messaging;
using Domain.Models.Configuration;
using Domain.Results;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace UnitTests.Application;

public sealed record TtlProbeQuery(string CacheKey, TimeSpan? CacheDuration, string CacheId)
    : IRequest<Result<string>>, ICacheableRequest;

public sealed class CachingBehaviorTtlTests
{
    [Fact]
    public async Task UsesTheDefaultTtlOfTheCacheEntryTheRequestNames()
    {
        var caches = Caches("REDIS");

        await Run(caches, cacheId: "REDIS", requestTtl: null);

        caches["REDIS"].LastExpiration.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task FallsBackToSixtyMinutesWhenTheCacheHasNoSettingsEntry()
    {
        var caches = Caches("UNLISTED");

        await Run(caches, cacheId: "UNLISTED", requestTtl: null);

        caches["UNLISTED"].LastExpiration.ShouldBe(TimeSpan.FromMinutes(CacheSettings.FallbackTtlMinutes));
    }

    [Fact]
    public async Task ARequestTtlWinsOverTheEntryDefault()
    {
        var caches = Caches("REDIS");

        await Run(caches, cacheId: "REDIS", requestTtl: TimeSpan.FromSeconds(30));

        caches["REDIS"].LastExpiration.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task EachRequestIsCachedInTheCacheItNames()
    {
        var caches = Caches("LOCAL", "REDIS");

        await Run(caches, cacheId: "LOCAL", requestTtl: null);

        caches["LOCAL"].LastExpiration.ShouldBe(TimeSpan.FromMinutes(5));
        caches["REDIS"].LastExpiration.ShouldBeNull();
    }

    [Fact]
    public async Task ACacheIdWithNoRegisteredCacheFailsWithTheSettingsList()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            Run(Caches("REDIS"), cacheId: "NOPE", requestTtl: null));

        exception.Message.ShouldContain("'NOPE'");
        exception.Message.ShouldContain("Settings:Caches");
    }

    private static Dictionary<string, RecordingCache> Caches(params string[] ids) =>
        ids.ToDictionary(id => id, _ => new RecordingCache(), StringComparer.Ordinal);

    private static async Task Run(Dictionary<string, RecordingCache> caches, string cacheId, TimeSpan? requestTtl)
    {
        var settings = new AppSettings
        {
            Caches =
            [
                new CacheSettings { Id = "LOCAL", Type = CacheType.Memory, DefaultTtlMinutes = 5 },
                new CacheSettings { Id = "REDIS", Type = CacheType.Redis, DefaultTtlMinutes = 15 },
            ],
        };

        var services = new ServiceCollection();

        foreach (var (id, cache) in caches)
        {
            services.AddKeyedSingleton<IDistributedCache>(id, cache);
        }

        await using var provider = services.BuildServiceProvider();

        var behavior = new CachingBehavior<TtlProbeQuery, Result<string>>(
            provider,
            new FixedOptionsMonitor<AppSettings>(settings),
            NullLogger<CachingBehavior<TtlProbeQuery, Result<string>>>.Instance);

        await behavior.Handle(
            new TtlProbeQuery($"ttl-probe:{Guid.NewGuid():N}", requestTtl, cacheId),
            _ => Task.FromResult(Result.Success("value")),
            CancellationToken.None);
    }
    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class RecordingCache : IDistributedCache
    {
        public TimeSpan? LastExpiration { get; private set; }

        public byte[]? Get(string key) => null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult<byte[]?>(null);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            LastExpiration = options.AbsoluteExpirationRelativeToNow;

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            LastExpiration = options.AbsoluteExpirationRelativeToNow;

            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key)
        {
        }

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }
}
