# 17. Features select their connections; the Modules section is removed

Status: Accepted

Supersedes the `Modules` parts of ADR-0015 and ADR-0016. Amends ADR-0004, ADR-0009,
ADR-0012 and ADR-0014.

## Context

After ADR-0016, `Settings` held four lists of connections (`Databases`, `Caches`,
`Brokers`, `ApiConnections`) but only one entry per list could ever be used: `Modules`
picked it (`Modules:Sql:DatabaseId`, `Modules:Cache:CacheId`, ...) and everything was
registered **unkeyed**. The lists existed so several connections could be described, yet a
second database or cache could not be reached by any code. `Modules` also still carried
behaviour - outbox polling, migrations, paged-cache sizes, SQL diagnostics, the consumer
switch - contradicting ADR-0016's rule that `Settings` describes engines completely.

The messaging concern had three names (`Data.Messaging`, `Settings:Brokers`, `IEventBus`),
and "Messaging" also named the in-process CQRS contracts in `Domain.Interfaces.Messaging`.

## Decision

### Every entry is registered under its Id; nothing connection-bound is unkeyed

Each module registers every entry of its list as .NET keyed services, keyed by the entry's
`Id`:

| List | Services registered per `Id` |
|---|---|
| `Databases`, relational | `AppDbContext`, `IRepository<,>`, `IUnitOfWork`, `IOutboxWriter`, `ISqlDatabaseAccess`, `ISqlSyntax`, `ISqlDialect`, `ISqlDatabaseProvider`, `CachedPageStreamer` |
| `Databases`, document | engine client/database handle, `IDocumentRepository<>` |
| `Caches` | `IDistributedCache` (Memory: a private `MemoryDistributedCache` per entry; Redis: `RedisCache` + `IConnectionMultiplexer`) |
| `Brokers` | `IEventBus` and the engine connection |

There is **no unkeyed registration** of any of these. `ContainerResolutionTests` fails if one
appears, and the always-registered in-memory `IDistributedCache` fallback is gone.

Relational contexts are built by `SqlDatabaseProvider.CreateContext` inside a keyed factory
(EF's `AddDbContext` cannot be keyed), with the same interceptors as before. Two entries on
different engines work side by side; each uses its engine's existing migration set.

`ServiceKeyLookupMode.InheritKey` is **not** used: in .NET 10 it fails for open generics and
under `ValidateOnBuild`. Open-generic repositories take `[ServiceKey] string databaseId`
plus `IServiceProvider`; non-generic services are registered through keyed factories.

### Features name the entries they use

A feature owns constants for its entry ids (`Domain.Models.Requests.Products.ProductsStore`:
`DatabaseId = "DEFAULT"`, `CacheId = "DEFAULT"`). Handlers inject
`[FromKeyedServices(ProductsStore.DatabaseId)] IRepository<ProductEntity, Guid>`.
Requests declare their entries for the pipeline:

- `IDatabaseRequest.DatabaseId` - `TransactionBehavior` resolves that entry's `IUnitOfWork`.
  A request without it runs without a transaction.
- `ICacheRequest.CacheId` (base of `ICacheableRequest`, `ICacheInvalidatingRequest`,
  `IIdempotentRequest`) - `CachingBehavior` and `IdempotencyBehavior` use that cache, and
  the TTL comes from that entry's `DefaultTtlMinutes`.

`ConnectionSelectionTests` enforces both: every connection-bound constructor parameter in
`Application` carries `[FromKeyedServices]`, and every command implements `IDatabaseRequest`.
An unknown id fails on first use with a message naming the `Settings` list.

Which entry a feature uses is therefore decided in code; what an entry points at is
configuration. Re-pointing a feature to another id is a rebuild.

### Modules are removed

`ModulesOptions` and the `Modules` section no longer exist. A module is active when its list
has a matching entry: Sql - any relational database; NoSql - any document database; Cache -
any cache; Broker - any broker; RestApi / GraphqlApi / GrpcApi - any `Apis` entry with that
`Protocol`. `InfrastructureModuleContext` carries only `Settings` and `Observability`.

### Behaviour lives on the entry

```json
"Databases": [ { "Id": "DEFAULT", "Type": "Postgresql", ...,
  "Sql": { "MaxPoolSize": 100, "MigrateOnStartup": false, "LogInterpolatedSql": false,
           "Outbox":     { "Enabled": false, "BrokerId": "EVENTS", "PollIntervalSeconds": 10, "BatchSize": 50 },
           "PagedCache": { "CacheId": "DEFAULT", "MaxCachedPages": 50, "DefaultTtlMinutes": 5 } } } ],
"Brokers":   [ { "Id": "EVENTS", "Type": "RabbitMq", "EnableConsumer": false, "RabbitMq": { ... } } ]
```

`OutboxPublisher` runs per database whose `Outbox.Enabled` is true, publishes to the keyed
`IEventBus` named by `Outbox.BrokerId`, and re-reads its block from
`IOptionsMonitor<AppSettings>` on every poll - interval, batch size and target broker
change without a restart; enabling or disabling it needs one. Startup validation
(`AppSettings.ConnectionErrors`) checks the ranges, that `Outbox.BrokerId` and
`PagedCache.CacheId` name existing entries, and blank/duplicate ids in `Apis` and in each
API's `Endpoints`.

### Entries are built lazily

Clients, connections and engine validation run in the keyed factory on first resolve
(`Domain.Connections.LazyConnection<T>`). A broken entry nothing uses does not stop the
service; it fails on first use - including its first health probe (ADR-0014) - with its key (`Settings:Databases[Id=DOCUMENTS]:ConnectionString`).
Cross-entry checks above still fail at startup. Migrations, outbox publishers and consumers
touch their entries at startup because configuration asks for them.

### One health check per entry

Checks are named `db:<Id>`, `cache:<Id>`, `broker:<Id>`.

> Amended by ADR-0014: the "Not in use" result originally described here is removed. Health
> checks build and probe every entry, and the dependency kind decides readiness and failure
> status (databases and brokers gate `/ready`; caches and APIs only degrade `/health`).
> Health checks are therefore the one caller that builds unused entries.

### Naming

- `Data.Messaging` is renamed **`Data.Broker`** (`BrokerModule`, `IBrokerProvider`,
  `BrokerProviderBase<T>`, `BrokerProviderRegistry`). `IEventBus` and `IBrokerProvider` live
  in `Domain.Interfaces.Broker`; `Domain.Interfaces.Messaging` stays the CQRS contracts.
  `Data.Sql` / `Data.NoSql` keep their names: they are different families behind different
  ports that share one list.
- Entry models are `DatabaseSettings`, `CacheSettings`, `BrokerSettings`, `ApiSettings`,
  `ApiEndpointSettings`; the SQL block is `SqlDatabaseOptions`.
- `ApiConnections` is `Apis`; `ApiID` / `EndPointID` are `Id`; `EndPoints` is `Endpoints`.

## Consequences

- Every `Modules__*` environment variable is gone; every API key and the moved SQL/broker
  keys changed. `docker-compose.yml` now overrides the cache at index 0 (`DEFAULT`, Redis).
- Template cache ids are `DEFAULT` (Memory) and `SHARED` (Redis); a deployment makes the
  cache Products uses distributed by overriding `DEFAULT`'s `Type`.
- One request uses one database; there is no cross-database transaction.
- All relational entries share one EF model (products + outbox tables).
- Some configuration errors move from startup to first use, but only for unused entries.
