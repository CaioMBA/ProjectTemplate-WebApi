# 16. Cache and broker connections are lists selected by id

Status: Accepted

Amended by ADR-0017: `Modules` is removed entirely (the rule below now holds with no selector in `Modules` at all); `EnableConsumer` moved onto the broker entry; the paged-cache TTL moved to `Settings:Databases[].Sql:PagedCache`; entry models are `CacheSettings` / `BrokerSettings`.

Extends ADR-0015. Amends ADR-0004 and ADR-0009.

## Context

After ADR-0015 databases were a list keyed by `Id`, but caches and brokers were still single
objects - `Settings:Cache` and `Settings:Messaging` - with the engine chosen separately by
`Modules:Cache:Provider` and `Modules:Messaging:Provider`. The same problems ADR-0015 fixed
for databases were still present:

- only one cache and one broker could be described, and neither could be named;
- the engine was chosen in `Modules` while its connection lived in `Settings`;
- `MessagingConnectionModel` mixed RabbitMQ and Kafka fields, and one `Exchange` value was
  used as the RabbitMQ exchange **and** the Kafka topic prefix;
- the query-result cache TTL was a single value, even though a local memory cache and a
  shared Redis usually want different lifetimes.

## Decision

### The rule

**`Settings` describes every engine: how to reach it, what things are called on it, and how
it behaves. `Modules` only switches a feature on and picks an entry by id.**

### Configuration

```json
"Settings": {
  "Caches": [
    { "Id": "MEMORY", "Type": "Memory", "DefaultTtlMinutes": 60 },
    { "Id": "REDIS",  "Type": "Redis", "Host": "redis_cache", "Password": "/run/secrets/redis",
      "TimeoutSeconds": 5, "DefaultTtlMinutes": 60,
      "Redis": { "Database": 0, "InstanceName": "webapi-template:" } }
  ],
  "Brokers": [
    { "Id": "EVENTS", "Type": "RabbitMq", "Host": "rabbitmq_app", "Password": "/run/secrets/rabbit",
      "RabbitMq": { "VirtualHost": "/", "Exchange": "webapi-template.events", "Queue": "webapi-template.inbox" } },
    { "Id": "STREAM", "Type": "Kafka",
      "Kafka": { "BootstrapServers": "kafka:9092", "TopicPrefix": "webapi-template.events", "ConsumerGroup": "webapi-template" } }
  ]
},
"Modules": {
  "Cache":     { "Enabled": true,  "CacheId": "MEMORY" },
  "Messaging": { "Enabled": false, "BrokerId": "EVENTS", "EnableConsumer": true }
}
```

| Model | Shared fields | Engine blocks |
|---|---|---|
| `CacheConnectionModel` | `Id`, `Type`, `Host`, `Port?`, `Username`, `Password`, `UseSsl`, `TimeoutSeconds`, `DefaultTtlMinutes` | `Redis { Database, InstanceName }` |
| `BrokerConnectionModel` | `Id`, `Type`, `Host`, `Port?`, `Username`, `Password`, `UseSsl` | `RabbitMq { VirtualHost, Exchange, Queue }`, `Kafka { BootstrapServers, TopicPrefix, ConsumerGroup }` |

`CacheProvider` and `MessagingProvider` are renamed `CacheType` and `BrokerType` to match
`DatabaseType`. The entry's `Type` is the only engine selector.

`Memory` is an ordinary entry type. It ignores every connection field, so "which cache" has
one answer - a `CacheId` - whatever the engine.

### Messaging names belong to the broker entry

Exchange, queue, topic prefix and consumer group are properties of how this service talks to
a specific broker, so they sit in that broker's block. RabbitMQ no longer carries an unused
`ConsumerGroup`, and Kafka no longer reuses the RabbitMQ `Exchange` as its topic.

Kafka's value is named `TopicPrefix` because it is one: each integration event is published
to `{TopicPrefix}.{EventType}`.

### TTL belongs to the cache entry

`CachingBehavior` resolves the entry named by `Modules:Cache:CacheId` and uses its
`DefaultTtlMinutes`, in this order:

1. the request's own `CacheDuration`;
2. the selected entry's `DefaultTtlMinutes`;
3. `CacheConnectionModel.FallbackTtlMinutes` (60) when no entry matches.

The lookup still goes through `IOptionsMonitor`, so a TTL change applies without a restart.
When the Cache module is disabled the in-memory fallback cache is still used, and it follows
the same rule - the selected entry's TTL if it exists, otherwise 60 minutes.

`Modules:Sql:PagedCache:DefaultTtlMinutes` is unchanged: it governs `CachedPageStreamer`
pages, a different cache use with a deliberately shorter lifetime.

### Validation

| Mistake | Where it fails |
|---|---|
| Duplicate or blank `Id` in `Caches` or `Brokers` | `GetAppSettings` (`ValidateConnections`) |
| `DefaultTtlMinutes` <= 0 | `GetAppSettings`, naming `Settings:Caches[Id=X]:DefaultTtlMinutes` |
| `CacheId` / `BrokerId` names no entry | module registration; message lists the configured ids |
| Redis without `Host` | provider `Validate` |
| RabbitMq without `Host` or `RabbitMq:Exchange`; without `RabbitMq:Queue` when consuming | provider `Validate` |
| Kafka without `Kafka:BootstrapServers` or `Kafka:TopicPrefix`; without `Kafka:ConsumerGroup` when consuming | provider `Validate` |

Default ports come from the provider when `Port` is unset: Redis 6379, RabbitMQ 5672.

The three lists share one lookup and one id validator in `AppSettings`, so their error
messages and `KeyOf()` formats cannot drift apart.

## A defect this surfaced

The Kafka producer published to `{Exchange}.{EventType}`, but the Kafka consumer subscribed to
the bare event type and deserialised using the topic name as the event type. A Kafka
round-trip could never deliver a message. Both sides now go through `KafkaTopics.For` and
`KafkaTopics.EventTypeOf`, and a unit test pins the round-trip. There is still no integration
test against a real Kafka broker; RabbitMQ has one.

## Consequences

- Every cache and messaging configuration key changed: `Settings__Caches__N__*`,
  `Settings__Brokers__N__*`, `Modules__Cache__CacheId`, `Modules__Messaging__BrokerId`.
- **Environment overrides address list entries by index.** `appsettings.json` ships `MEMORY`
  at index 0 and `REDIS` at index 1, so `docker-compose.yml` overrides `Settings__Caches__1__*`.
  Overriding index 0 with `Id=REDIS` would merge into the `MEMORY` entry and then collide
  with the real `REDIS` entry as a duplicate id - which startup rejects.
- Each module still consumes one entry. Several caches or brokers at once would need keyed
  registrations, which this change does not add.
