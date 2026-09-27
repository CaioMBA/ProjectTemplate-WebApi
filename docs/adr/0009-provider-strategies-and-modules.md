# 9. Provider strategies and infrastructure module objects

Status: Accepted

Amended by ADR-0017: modules no longer have `Enabled` switches - a module is active when its `Settings` list has a matching entry, and it registers **every** entry as keyed services. `Data.Messaging` / `IMessagingBrokerProvider` are now `Data.Broker` / `IBrokerProvider`.

Amended by ADR-0015: SQL and NoSQL providers are resolved from the `Type` of a
`Settings:Databases` entry (one `DatabaseType` enum split into `Relational` and `Document`
families), not from `Modules:Sql:Provider` / `Modules:NoSql:Provider`. ADR-0016 does the same for caches and brokers: `CacheType` / `BrokerType` come from `Settings:Caches` / `Settings:Brokers` entries.

## Context

Every pluggable concern selected its implementation with an `if` or a `switch` inside a
static registration method. `Data.Cache` branched on `CacheProvider` and inlined a 47-line
private `AddRedis`. `Data.Messaging` branched on `MessagingProvider`. `Data.NoSql` had no
provider concept at all and hardcoded MongoDB. Module activation was seven flat
`if (modules.X.Enabled)` blocks calling seven unrelated static methods.

The result: 55 static classes, 34 interfaces, and exactly one interface with more than one
implementation. Behaviour was selected by branching, not by dispatch.

## Decision

Each pluggable concern gets a provider interface, an abstract base holding the shared
validate-then-register sequence, one class per implementation, and a registry that resolves
an enum value to exactly one provider.

| Concern | Interface | Providers |
|---|---|---|
| SQL | `ISqlDatabaseProvider` | 6 working, 1 throwing |
| NoSQL | `INoSqlDatabaseProvider` | MongoDb, CosmosDb, DynamoDb, RavenDb |
| Cache | `ICacheStoreProvider` | Memory, Redis |
| Messaging | `IMessagingBrokerProvider` | RabbitMq, Kafka |
| API auth | `IApiAuthenticationStrategy` | None, Basic, Bearer, ApiKey |

Module activation uses `IInfrastructureModule`. Each module class lives in the project it
activates, so deleting the project deletes the module. `InfrastructureModuleLoader` holds
the seven modules, rejects duplicate names, registers the enabled ones and returns what it
loaded.

Modules are instantiated explicitly rather than discovered by assembly scan. Scanning hides
composition and turns a registration failure into a mystery; an explicit list is
debuggable, analyzable and greppable.

## The API authentication strategies removed a real defect

`RestApiClient` and `GraphqlApiClient` each carried a near-identical switch over
`ApiAuthorizationType`. They disagreed: one threw on an unrecognised value, the other
silently did nothing. Both silently skipped authentication when the credential was empty,
so a misconfigured secret produced an unauthenticated request rather than an error.

The shared strategy now throws when a scheme that requires a credential is given none.

## Consequences

- Adding a provider is a new class and a registry line.
- Every provider validates its own configuration and registers its own health check, so a
  deleted module leaves nothing dangling.
- A registry test asserts every enum value resolves to exactly one provider, which fails
  the build when an enum member is added without an implementation.
