using Domain.Connections;
using Domain.Enums;
using Domain.Models.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Data.Cache.Providers;

public sealed class RedisCacheStoreProvider : CacheStoreProviderBase
{
    public override CacheType ProviderType => CacheType.Redis;

    public const int DefaultPort = 6379;

    public override bool IsDistributed => true;

    public override void Validate(CacheSettings connection)
    {
        base.Validate(connection);

        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            throw new InvalidOperationException(
                $"Redis requires {connection.KeyOf(nameof(connection.Host))}. Set it to the Redis "
                + "service name (redis_cache on the HomeLab overlay), or change the entry's Type to Memory.");
        }
    }

    public static ConfigurationOptions BuildConfiguration(CacheSettings connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return new ConfigurationOptions
        {
            EndPoints = { { connection.Host ?? string.Empty, connection.Port ?? DefaultPort } },
            User = connection.Username,
            Password = connection.Password,
            DefaultDatabase = connection.Redis.Database,
            Ssl = connection.UseSsl,
            ConnectTimeout = (int)TimeSpan.FromSeconds(connection.TimeoutSeconds).TotalMilliseconds,
            AbortOnConnectFail = false,
            ConnectRetry = 3,
            ClientName = connection.Redis.InstanceName.TrimEnd(':'),
        };
    }

    protected override void RegisterStore(
        IServiceCollection services,
        CacheSettings connection)
    {
        var cacheId = connection.Id;

        services.AddKeyedSingleton(cacheId, (_, _) => new LazyConnection<IConnectionMultiplexer>(() =>
        {
            Validate(connection);

            return ConnectionMultiplexer.Connect(BuildConfiguration(connection));
        }));

        services.AddKeyedSingleton<IConnectionMultiplexer>(cacheId, (provider, key) =>
            provider.GetRequiredKeyedService<LazyConnection<IConnectionMultiplexer>>(key).Value);

        services.AddKeyedSingleton<IDistributedCache>(cacheId, (provider, key) =>
        {
            Validate(connection);

            return new Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache(Options.Create(new Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions
            {
                ConfigurationOptions = BuildConfiguration(connection),
                InstanceName = connection.Redis.InstanceName,
                ConnectionMultiplexerFactory = () =>
                    Task.FromResult(provider.GetRequiredKeyedService<IConnectionMultiplexer>(key)),
            }));
        });
    }

    protected override IHealthCheck CreateHealthCheck(IServiceProvider provider, string cacheId) =>
        new RedisPingHealthCheck(provider.GetRequiredKeyedService<IConnectionMultiplexer>(cacheId));

    private sealed class RedisPingHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var latency = await multiplexer.GetDatabase().PingAsync().ConfigureAwait(false);

                return HealthCheckResult.Healthy($"Redis answered PING in {latency.TotalMilliseconds:F0} ms.");
            }
            catch (RedisException exception)
            {
                return new HealthCheckResult(context.Registration.FailureStatus, "Redis PING failed.", exception);
            }
        }
    }
}