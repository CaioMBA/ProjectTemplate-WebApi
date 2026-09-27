using Domain.Enums;
using Domain.Models.Configuration;

namespace UnitTests.Domain;

public sealed class ConnectionListConfigurationTests
{
    [Fact]
    public void GetCacheMatchesIdCaseInsensitively() =>
        new AppSettings { Caches = [Cache("REDIS")] }.GetCache("redis").Id.ShouldBe("REDIS");

    [Fact]
    public void GetCacheNamesTheConfiguredIdsWhenNothingMatches()
    {
        var settings = new AppSettings { Caches = [Cache("MEMORY"), Cache("REDIS")] };

        var exception = Should.Throw<InvalidOperationException>(() => settings.GetCache("NOPE"));

        exception.Message.ShouldContain("Settings:Caches");
        exception.Message.ShouldContain("MEMORY, REDIS");
    }

    [Fact]
    public void FindCacheReturnsNullWhenNothingMatches() =>
        new AppSettings { Caches = [Cache("MEMORY")] }.FindCache("NOPE").ShouldBeNull();

    [Fact]
    public void GetBrokerNamesTheConfiguredIdsWhenNothingMatches()
    {
        var settings = new AppSettings { Brokers = [Broker("EVENTS")] };

        var exception = Should.Throw<InvalidOperationException>(() => settings.GetBroker("STREAM"));

        exception.Message.ShouldContain("Settings:Brokers");
        exception.Message.ShouldContain("EVENTS");
    }

    [Fact]
    public void ValidateConnectionsRejectsDuplicateCacheIds() =>
        Should.Throw<InvalidOperationException>(
                new AppSettings { Caches = [Cache("REDIS"), Cache("redis")] }.ValidateConnections)
            .Message.ShouldContain("Settings:Caches declares duplicate");

    [Fact]
    public void ValidateConnectionsRejectsDuplicateBrokerIds() =>
        Should.Throw<InvalidOperationException>(
                new AppSettings { Brokers = [Broker("EVENTS"), Broker("Events")] }.ValidateConnections)
            .Message.ShouldContain("Settings:Brokers declares duplicate");

    [Fact]
    public void ValidateConnectionsRejectsABlankBrokerId() =>
        Should.Throw<InvalidOperationException>(
                new AppSettings { Brokers = [Broker("EVENTS"), Broker("")] }.ValidateConnections)
            .Message.ShouldContain("Settings:Brokers:1:Id");

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ValidateConnectionsRejectsANonPositiveCacheTtl(int ttl)
    {
        var redis = Cache("REDIS");
        redis.DefaultTtlMinutes = ttl;

        Should.Throw<InvalidOperationException>(new AppSettings { Caches = [redis] }.ValidateConnections)
            .Message.ShouldContain("Settings:Caches[Id=REDIS]:DefaultTtlMinutes");
    }

    [Fact]
    public void ValidateConnectionsAcceptsDistinctIdsAcrossLists()
    {
        var settings = new AppSettings
        {
            Databases = [new DatabaseSettings { Id = "DEFAULT" }],
            Caches = [Cache("DEFAULT")],
            Brokers = [Broker("DEFAULT")],
        };

        Should.NotThrow(settings.ValidateConnections);
    }

    [Fact]
    public void KeyOfNamesCacheAndBrokerEntriesById()
    {
        Cache("REDIS").KeyOf("Redis:Database").ShouldBe("Settings:Caches[Id=REDIS]:Redis:Database");

        Broker("STREAM").KeyOf("Kafka:TopicPrefix").ShouldBe("Settings:Brokers[Id=STREAM]:Kafka:TopicPrefix");
    }

    [Fact]
    public void ValidateConnectionsRejectsDuplicateApiIds() =>
        Should.Throw<InvalidOperationException>(() =>
                new AppSettings { Apis = [new ApiSettings { Id = "PARTNER" }, new ApiSettings { Id = "partner" }] }
                    .ValidateConnections())
            .Message.ShouldContain("Settings:Apis declares duplicate Ids");

    [Fact]
    public void ValidateConnectionsRejectsDuplicateEndpointIdsWithinOneApi() =>
        Should.Throw<InvalidOperationException>(() =>
                new AppSettings
                {
                    Apis =
                    [
                        new ApiSettings
                        {
                            Id = "PARTNER",
                            Endpoints = [new ApiEndpointSettings { Id = "PING", Path = "/a" }, new ApiEndpointSettings { Id = "PING", Path = "/b" }],
                        },
                    ],
                }.ValidateConnections())
            .Message.ShouldContain("Settings:Apis[Id=PARTNER]:Endpoints declares duplicate Ids");

    [Fact]
    public void GetApiNamesTheConfiguredIdsWhenNothingMatches() =>
        Should.Throw<InvalidOperationException>(() =>
                new AppSettings { Apis = [new ApiSettings { Id = "PARTNER" }] }.GetApi("NOPE"))
            .Message.ShouldContain("Configured ids: PARTNER");

    [Theory]
    [InlineData("Sql:Outbox:PollIntervalSeconds")]
    [InlineData("Sql:Outbox:BatchSize")]
    [InlineData("Sql:PagedCache:MaxCachedPages")]
    [InlineData("Sql:PagedCache:DefaultTtlMinutes")]
    public void ValidateConnectionsRejectsOutOfRangeSqlSettings(string key)
    {
        var database = new DatabaseSettings { Id = "DEFAULT", Type = DatabaseType.Postgresql };

        switch (key)
        {
            case "Sql:Outbox:PollIntervalSeconds":
                database.Sql.Outbox.PollIntervalSeconds = 0;
                break;
            case "Sql:Outbox:BatchSize":
                database.Sql.Outbox.BatchSize = 0;
                break;
            case "Sql:PagedCache:MaxCachedPages":
                database.Sql.PagedCache.MaxCachedPages = 0;
                break;
            default:
                database.Sql.PagedCache.DefaultTtlMinutes = 0;
                break;
        }

        Should.Throw<InvalidOperationException>(() =>
                new AppSettings { Databases = [database] }.ValidateConnections())
            .Message.ShouldContain($"Settings:Databases[Id=DEFAULT]:{key}");
    }

    [Fact]
    public void ValidateConnectionsRequiresAnEnabledOutboxToNameAnExistingBroker()
    {
        var database = new DatabaseSettings { Id = "DEFAULT", Type = DatabaseType.Postgresql };
        database.Sql.Outbox.Enabled = true;

        Should.Throw<InvalidOperationException>(() =>
                new AppSettings { Databases = [database] }.ValidateConnections())
            .Message.ShouldContain("Sql:Outbox:BrokerId is required");

        database.Sql.Outbox.BrokerId = "EVENTS";

        Should.Throw<InvalidOperationException>(() =>
                new AppSettings { Databases = [database] }.ValidateConnections())
            .Message.ShouldContain("'EVENTS', which is not an Id in Settings:Brokers");

        Should.NotThrow(() =>
            new AppSettings { Databases = [database], Brokers = [Broker("EVENTS")] }.ValidateConnections());
    }

    [Fact]
    public void ValidateConnectionsRequiresThePagedCacheToNameAnExistingCache()
    {
        var database = new DatabaseSettings { Id = "DEFAULT", Type = DatabaseType.Postgresql };
        database.Sql.PagedCache.CacheId = "LOCAL";

        Should.Throw<InvalidOperationException>(() =>
                new AppSettings { Databases = [database] }.ValidateConnections())
            .Message.ShouldContain("Settings:Databases[Id=DEFAULT]:Sql:PagedCache:CacheId");

        Should.NotThrow(() =>
            new AppSettings { Databases = [database], Caches = [Cache("LOCAL")] }.ValidateConnections());
    }

    [Fact]
    public void SqlSettingsAreNotCheckedOnDocumentDatabases()
    {
        var database = new DatabaseSettings { Id = "DOCS", Type = DatabaseType.MongoDb };
        database.Sql.Outbox.BatchSize = 0;

        Should.NotThrow(() => new AppSettings { Databases = [database] }.ValidateConnections());
    }

    private static CacheSettings Cache(string id) => new() { Id = id, Type = CacheType.Redis };

    private static BrokerSettings Broker(string id) => new() { Id = id, Type = BrokerType.RabbitMq };
}
