# Project state and handover

This file is the entry point for anyone - human or agent - picking this repository up
without the history behind it. It records what was built, why, what was verified, what is
deliberately absent, and what comes next.

Last verified: .NET SDK 10.0.400, Release build clean, 621 tests passing.

---

## 1. What this is

A .NET 10 WebAPI project template. It is distributed as a **GitHub template repository**
("Use this template"), not as a `dotnet new` package - there is no `.template.config`
anywhere, and renaming is a documented manual checklist in the README.

It targets deployment into the HomeLab Docker Swarm stack, so its observability defaults
are specific to that environment rather than generic.

| | |
|---|---|
| Projects | 15 (12 source, 3 test) |
| C# files | 286, ~17,200 lines |
| Tests | 621 - 423 unit, 109 architecture, 89 integration |
| SQL engines | 6 working, 1 deliberately refusing |
| NoSQL providers | 4 |
| ADRs | 19 |

---

## 2. Origin

Four repositories were analysed before any code was written.

| Reference | What it contributed |
|---|---|
| `RestAPI-ProjectBase` (legacy, net9.0) | EF value converters/comparers, UUIDv7 keys, mirrored Dapper type handlers, config-driven health-check registration. Despite its folder names it was **not** Clean Architecture - `Domain` referenced EF Core, AutoMapper and Dapper.Contrib. |
| `API-IdentityAuthorizationHub` (net10.0) | The modern baseline: `.slnx`, CPM, `Source/`+`Tests/` layout, primary constructors, `Guid.CreateVersion7()`, `ConfigureHttpClientDefaults` + `AddStandardResilienceHandler`, `IDbContextFactory`, per-concern DI extension methods. |
| `ProjectTemplate-MauiBlazorHybrid` (net10.0) | `.slnx` + CPM baseline, enum-keyed named HTTP clients, `IAsyncEnumerable` streaming data access with savepoints, `[GeneratedRegex]`, the `DatabaseAccess.cs` that Phase C was modelled on. |
| `HomeLab` | The binding constraint. Defines the Grafana Alloy collector, the four promoted Loki labels, the dashboards' metric names, and the container health-check contract. |

### Anti-patterns present in all three code references, deliberately not carried over

- `HttpClientHandler.DangerousAcceptAnyServerCertificateValidator` applied globally
- `Timeout.InfiniteTimeSpan` on HttpClient
- `BuildServiceProvider()` during service registration (ASP0000)
- `Database.MigrateAsync()` at registration time
- `app.UseCors()` without `AddCors()` - throws at request time
- Hardcoded AES/HMAC/JWT keys committed to source
- `catch { Console.WriteLine; return null; }`
- A `Utils` singleton god-object capturing `IOptionsMonitor.CurrentValue` in a field

### Licensing rule

Every dependency is MIT or Apache-2.0 with no revenue-threshold clause. AutoMapper (v15+),
MediatR, MassTransit v9 and FluentAssertions v8 were each rejected on licence grounds, not
technical ones. ADRs 0002 and 0003 record this so nobody re-adds them.

---

## 3. Architecture

```
Source/
  Domain/                      SHARED KERNEL - referenced by everything
  Application/                 dispatcher, behaviours, feature slices
  Infrastructure/
    Api/       Data.RestApi, Data.GraphqlApi, Data.GrpcApi
    Data/      Data.Sql, Data.NoSql, Data.Cache, Data.Broker
    Platform/  CrossCutting, Observability, Scheduling
  WebApi/                      controllers only
Tests/
  UnitTests/  ArchitectureTests/  IntegrationTests/
```

Projects are **grouped on disk and in the `.slnx`** but their names and namespaces are
flat and unchanged - `Data.Sql` lives at `Source/Infrastructure/Data/Data.Sql/` and is
still `Data.Sql`. See ADR-0012.

### Dependency rules, enforced by `Tests/ArchitectureTests`

```
Domain          ->  no project reference
Application     ->  Domain
Data.*          ->  Domain          (never Application, never each other)
Observability   ->  Domain
CrossCutting    ->  everything
WebApi          ->  CrossCutting, Application, Domain, Observability
```

Domain is a shared kernel, not a DDD-pure domain layer: it carries Entities, Enums, Models,
DTOs, Mapster `IRegister` configs, all port interfaces, Results, Guards and Specifications,
and it has NuGet references. That relaxation was deliberate (ADR-0001). What it does **not**
relax is the direction - `Application -> Data.*` remains forbidden, because that inversion
is what makes all three reference repos' application layers untestable.

### Conventions

- **`Setup/` folder in every project.** File `Add{Concern}Setup.cs` -> class `{Concern}Setup`
  -> method `Add{Concern}Setup()`. Pipeline counterparts are `Use{Concern}Setup.cs`.
- **Controllers only.** No Minimal APIs. `MapHealthChecks` and `MapGraphQL` are the two
  unavoidable exceptions - they are framework middleware mappings, not route handlers.
- **Contracts live in `Domain`.** Every interface, enum and record (commands and queries included)
  is declared in `Domain`; other projects hold behaviour. Enforced by `ContractLocationTests`.
- **Private fields are `_camelCase`** (static readonly too); `IDE1006` is a build error.
- **Zero comments.** No `//`, no `///`, no `#` or `<!-- -->` in `.cs`, `.csproj`, `.props`,
  `.targets`, Dockerfile, compose, `.editorconfig` or appsettings. `[SuppressMessage]`
  attributes and `#pragma` stay - they are code. Non-obvious reasoning goes in `docs/adr/`.
- **Mapster**, not AutoMapper or Mapperly.
- **Native `IDistributedCache`**, no custom cache abstraction.
- **Hand-rolled CQRS dispatcher**, not MediatR.

---

## 4. Build order the repository was created in

Phases 0-9 built the template; phases A-H restructured and extended it; P1-P6 closed an
audit. They are listed because the ADRs reference them.

| Phase | Outcome |
|---|---|
| 0 | Foundation. Fixed a broken `.slnx` path, broken Dockerfile paths, and a missing `docker-compose.yml` that CI already referenced. Added `global.json`, `Directory.Build.props`, CPM, `.editorconfig`, pinned `dotnet-ef`. |
| 1 | Domain shared kernel. |
| 2 | Application dispatcher + 6 pipeline behaviours + feature slices. |
| 3 | `Data.Sql`: EF Core, salvaged converters, interceptors, outbox, Dapper reads. |
| 4 | The six remaining infrastructure modules. |
| 5 | Observability wired to HomeLab Alloy. |
| 6 | WebApi: versioned controllers, Swashbuckle + SwaggerUI + Scalar, GraphQL server. |
| 7 | CrossCutting composition root and `Program.cs` pipeline. |
| 8 | Tests. |
| 9 | Container, CI/CD, README, ADRs. |
| A | Regrouped Infrastructure into `Api/`, `Data/`, `Platform/`. |
| A2 | System ports (`IFileSystem`, `IEnvironmentAccessor`, `ISystemInfo`) and `[Secret]` resolution. |
| B | Six working SQL engines, six derived contexts, six migration sets. |
| C | `ISqlDatabaseAccess` Dapper layer. |
| D | Four NoSQL providers. |
| E | Cache and messaging provider strategies. |
| F | `IInfrastructureModule` objects. |
| G | `ApiClientBase` + `IApiAuthenticationStrategy`. |
| H | Cleanup, ADRs, CI model-drift gate. |
| P1-P6 | Audit remediation - see section 7. |
| I | SQL and NoSQL connections unified into `Settings:Databases`; one `DatabaseType` enum; engine chosen only by the entry's `Type` (ADR-0015). |
| J | Cache and broker connections become `Settings:Caches` / `Settings:Brokers` lists; TTL and messaging names live on the entry; Kafka topic mismatch fixed (ADR-0016). |
| K | Private fields renamed to `_camelCase` (83, via Roslyn rename) and `IDE1006` made an error; all interfaces/enums/records moved into `Domain`, `ISqlDatabaseProvider` split into a Domain port + EF abstract class in Data.Sql (ADR-0001). |
| L | Secrets resolved inside the options pipeline (`PostConfigure` + `AppSettingsValidator`), so `IOptions*<AppSettings>` no longer serves file paths to the API clients; `LastKnownGoodOptionsMonitor` keeps the last valid value on a broken reload (ADR-0011 amendment). |
| M | `Data.Messaging` renamed `Data.Broker`; entry models renamed `*Settings`; `ApiConnections` -> `Apis` with `Id`/`Endpoints`. `Modules` removed: every entry registered as keyed services under its `Id`, features select entries (`ProductsStore`, `IDatabaseRequest`, `ICacheRequest`), entries built lazily, one health check per entry, SQL behaviour (migrate, outbox->BrokerId, paged cache->CacheId, diagnostics) and `EnableConsumer` moved onto entries (ADR-0017). |
| N | Health checks follow the dependency kind (`HealthCheckPolicy`): databases/brokers gate `/ready` and fail Unhealthy, caches/APIs only degrade; "Not in use" removed, every entry probed; global per-check timeout; `self`-tag guard; `/health-ui` dashboard over `/health-ui-api`; appsettings ships only used entries (ADR-0014). |
| O | Line endings normalised to LF (`.gitattributes eol=lf`); settings namespace `Domain.Models.Configuration`, `AppSettingsModel` -> `AppSettings`; `StartupSettings` bound once, `ApiOptions`/`ObservabilityOptions` validated, `OTEL_*`/`HEALTHCHECK_PATH` applied in the options pipeline, `SettingsReloadPolicy` + `RestartRequiredSettingsWatcher` (ADR-0011 amendment). |
| P | `Platform/Scheduling`: Hangfire behind `Domain.Interfaces.Scheduling` (`IRecurringJob`, `IBackgroundJob`, `IJobScheduler`, `IJobProgress`); memory/Postgres/SQL Server storage; ILogger -> dashboard console; `/hangfire` dashboard local-only; `scheduler` health check (degrades only); `OutboxCleanupJob` via `IOutboxMaintenance` (ADR-0018). |
| Q | MongoDB job storage (`Hangfire.Mongo`, reuses the entry's keyed `IMongoClient`, tail-notifications mode for standalone mongod); unsupported engines rejected with a reason (ADR-0018 amendment). |
| R | GraphQL auto schema: every database entry introspected at startup (catalog SQL on the six SQL engines, sampled documents on Mongo/Cosmos/Raven/Dynamo) behind the keyed `IDynamicDataSource` port; per-database root fields with filter (`eq`...`like`, `and`/`or`/`not`), sort, skip/take, `totalCount`, aggregates, by-id and batched FK navigation both ways; exclusions + built-in deny-list in `SchemaExposurePolicy` (ADR-0019). |

---

## 5. Key subsystems

### Connections

`Settings` holds four lists - `Databases`, `Caches`, `Brokers`, `Apis` - and there is **no
`Modules` section**. Every entry is registered under its `Id` as keyed services; nothing
connection-bound (`IRepository<,>`, `IUnitOfWork`, `IDistributedCache`, `IEventBus`, ...) is
registered unkeyed. Features name their entries: `ProductsStore.DatabaseId` / `CacheId`
constants, `[FromKeyedServices(...)]` on handler parameters, `IDatabaseRequest` /
`ICacheRequest` on requests for the pipeline behaviours. `ConnectionSelectionTests` and
`ContainerResolutionTests` enforce it.

The entry's `Type` is the only engine selector. Behaviour lives on the entry:
`Databases[].Sql { MigrateOnStartup, LogInterpolatedSql, Outbox { Enabled, BrokerId, ... },
PagedCache { CacheId, ... } }`, `Caches[].DefaultTtlMinutes`, `Brokers[].EnableConsumer`.
Entries are built lazily (`LazyConnection<T>`, which retries after a failed build); a broken
entry fails on first use or first health probe with its key. Health: databases and brokers
gate `/ready` (Unhealthy), caches and APIs only degrade `/health`; every entry is probed,
every check has a timeout, `/health-ui` polls `/health-ui-api` (ADR-0014). Compose overrides cache `DEFAULT`
(index 0) to Redis. See ADR-0015, ADR-0016, ADR-0017.
### SQL - `Data.Sql`

`AppDbContext` is **abstract** with `protected abstract ISqlDialect Dialect { get; }`. Six
derived contexts and six `IDesignTimeDbContextFactory` implementations exist because EF
migrations are provider-specific - one snapshot cannot serve Postgres and SQL Server.

| Engine | Status |
|---|---|
| Postgresql, SqlServer, Mysql, Oracle, Firebird, Sqlite | working, own migration set |
| Mariadb | **provider throws with an explanation** - Pomelo 9.0.0 pins EF Relational to `[9.0.0,9.0.999]`, no EF 10 build. Route to Mysql. |

Contexts are created by `SqlDatabaseProvider.CreateContext` inside a keyed factory per
database entry (`AddDbContext` cannot be keyed); two entries on different engines run side by
side, each with its engine's migration set.

Application code never hardcodes dialect: it injects `ISqlSyntax` for `BooleanLiteral`,
`CaseInsensitiveLike`, `ApplyPagination`, `Parameter` and `QuoteIdentifier`. `ILIKE`,
`FALSE` and `LIMIT/OFFSET` are Postgres-only and broke SQLite when they were inline.

`ISqlDatabaseProvider.ConfigureDapperTypeHandlers()` is a per-engine hook - SQLite needs
`TextGuid`/`TextDateTime`/`TextDecimal` handlers because it stores everything as TEXT.

Audit and event timestamps are `DateTime` (UTC), **not** `DateTimeOffset` - MySQL, SQLite
and Firebird lack native `DateTimeOffset`.

### Dapper access - `ISqlDatabaseAccess`

Replaced and deleted `IReadDbConnection`. Covers query/scalar/execute, stored procedures,
`IAsyncEnumerable` streaming, `QueryMultiple` via `ISqlResultSets` (boundaries preserved),
cached paging, single-table CRUD, and `BeginTransactionAsync` returning
`ISqlTransactionScope` with savepoints. No Dapper types leak into Domain.

CRUD SQL is generated by `SqlEntityMapFactory`, which reads **EF Core's own `IModel` first**
- so snake_case columns and owned-type columns like `price_amount` come free - and falls
back to convention plus `[SqlTable]`/`[SqlColumn]`/`[SqlKey]`/`[SqlIgnore]` for POCOs EF
does not map. Dapper.Contrib was rejected (abandoned since Nov 2020, no `[Column]`
attribute, key column bypasses the `ISqlAdapter` hook). See ADR-0010.

`CachedPageStreamer` is cache-first, then **one** background warm per query key in a fresh
DI scope with its own connection, cancellation linked to `ApplicationStopping` rather than
the request token. A caller-supplied transaction forces bounded inline streaming instead,
because a transaction cannot outlive its caller.

### Secrets

There are no `Password`/`PasswordFile` property pairs. A secret is **one property marked
`[Secret]`**. `SecretResolver` walks the bound `AppSettings` graph once after binding:

| Value | Behaviour |
|---|---|
| Path-shaped (`^(?:/|[A-Za-z]:[\\/]|\\\\|\.{1,2}[\\/])`) | **must** read a readable file, or `SecretResolutionException` aborts startup |
| Anything else | literal |
| `plain:` prefix | literal escape hatch for path-shaped literals |

Also rejects directories, caps file size (default 64 KB), trims the trailing newline Docker
secrets carry, rejects all-whitespace, and supports an optional `AllowedRoots` allow-list.

Resolution is **scoped to `[Secret]` properties on purpose**. Blanket scanning would fail
startup on legitimate path-shaped non-secrets like `VirtualHost = "/"`,
`HealthEndpoint = "/health"` and `ApiEndpointSettings.Path = "/api/v1/x"`. See ADR-0011.

### Observability

All OTLP signals go to Grafana Alloy at `monitoring_alloy:4317` (gRPC), no auth. Never
directly to Loki/Tempo/Prometheus - those are internal-only on the external overlay network
`shared-network`.

Alloy's `loki_hints` promotes exactly four resource attributes to Loki labels. All four
**must** be set or the `logs-overview.json` dashboard breaks:

```
service.name   service.namespace   service.instance.id   deployment.environment.name
```

Dashboards query stable seconds-based semconv (`http_server_request_duration_seconds_bucket`,
`process_cpu_time_seconds_total`), so process instrumentation is required or three panels
stay blank. Pyroscope profiling bypasses Alloy and pushes direct to `monitoring_pyroscope:4040`.

### Modules

Seven `IInfrastructureModule` implementations, instantiated **explicitly** in
`InfrastructureModuleLoader` - not discovered by assembly scan. A module is active when its
`Settings` list has a matching entry (Sql: relational database; NoSql: document database;
Cache; Broker; RestApi/GraphqlApi/GrpcApi: an `Apis` entry with that `Protocol`).

Removing a capability permanently = delete the project + its `ProjectReference` + its
`.slnx` entry + its loader line + its `Settings` entries. Nothing else knows it existed,
because everything binds to `Domain.Interfaces`.
### GraphQL auto schema

The GraphQL server is entirely generated (`ProductQueries` removed; root type `Query`);
`Api:GraphQlServer:Enabled` is the only switch and is on in every environment (public, no auth); `autoSchemaStatus` reports per-database exposure and is the only field when nothing can be exposed, so a database outage at startup is handled by health checks and Swarm restarts. `WebApi/GraphQL/AutoSchema/AutoSchemaTypeModule` asks every database
entry's keyed `IDynamicDataSource` to describe itself while HotChocolate builds the schema,
`SchemaExposurePolicy` decides names, exclusions and operators, `AutoSchemaTypeFactory` builds the
types and `AutoSchemaRowLoader` batches relation and by-id loads. SQL is generated only by
`Data.Sql/DynamicData/SqlDynamicQueryBuilder` from the introspected model, values always as
parameters. Read-only and unauthenticated (ADR-0006): exclude sensitive tables/columns before
enabling it outside a developer machine. See ADR-0019.

---

## 6. Verification status

All of the following were run against real infrastructure, not mocked.

- Release build clean with `TreatWarningsAsErrors`
- 621 tests green
- Docker image builds; container runs non-root as `app`; `HEALTHCHECK` reports healthy
- `POST /api/v1/products` returns 201 with a correctly populated outbox row
- Engine switch verified end-to-end on **Postgresql and Sqlite** - health, create, Dapper
  list, case-insensitive search, pagination, GraphQL
- Secret resolution verified in all three states - real file, missing file (startup aborts),
  `plain:` literal
- gRPC verified against a real Kestrel gRPC server - round-trip, auth header, deadline
  applied, `DeadlineExceeded` on a slow call
- No EF model drift across all six contexts
- GraphQL auto schema verified end-to-end on **Postgresql, Sqlite and MongoDB** - every
  operator family, FK navigation both ways (batched), aggregates, exclusions, an unreachable
  database skipped; the other engines are covered by unit tests of their catalog mapping and
  query text only

---

## 7. Bugs found by running the code

Compiling proved nothing in most of these cases. Recorded because the failure modes recur.

| Bug | Why it survived |
|---|---|
| Every write returned 500 - `EnableRetryOnFailure` is incompatible with manual `BeginTransactionAsync` | A code comment had asserted it was "acceptable" |
| Idempotent retry returned 409 instead of replaying the original 201 | The comment said it replayed; the code called `next()` again |
| Integration tests were green while testing a *different* database | `WebApplicationFactory.ConfigureAppConfiguration` arrives too late under minimal hosting; the app silently fell back to `localhost:5432` |
| Architecture tests missed the violation they exist for | Roslyn elides unused assembly references, so `Application -> Data.Sql` passed until a type was used. Fixed with a `.csproj` scan. |
| Docker build failed while local build passed | `.editorconfig` was in `.dockerignore`, so analyser severities never reached the image |
| Outbox rows shipped with **no business data** | `ToJson<T>()` inferred `T` from the *declared* parameter type, so `EnqueueAsync(IIntegrationEvent e)` serialised only the interface's three properties. Affected the outbox and both brokers. |
| Every cacheable query returned **501 on the second identical request** | `Result<T>` has no parameterless ctor, so STJ threw `NotSupportedException` -> mapped to 501. Every test created a fresh entity, so no test ever requested the same key twice. |
| The GraphQL client threw on **every** real call | `GraphQL.Client`'s `SystemTextJsonSerializer` mutates the options it is handed; `JsonDefaults.Standard` is read-only |
| Disabling `Modules:Cache` broke every request in the app (historical; `Modules` no longer exists) | `CachingBehavior`/`IdempotencyBehavior`/`CachedPageStreamer` all required `IDistributedCache`, which only the Cache module registered |
| `Data.GrpcApi` was a shell with no success path | Every architecture test passed on it - they verify shape, not behaviour |

The lesson is recorded in ADR-0013: an infrastructure module is not finished when it
compiles and the architecture tests pass, it is finished when something calls it.

The cheapest single guard is `Tests/UnitTests/Modules/ContainerResolutionTests.cs`, which
builds the real container per module combination. It would have caught the cache break, the
gRPC shell and an earlier duplicate-registration bug on its own.

---

## 8. Traps where the obvious change is wrong

Full list with file references in **`docs/adr/0007-non-obvious-constraints.md`**. Read it
before editing build files, the transaction path, or the test host. Highlights:

- `.editorconfig` must **not** be in `.dockerignore`
- HotChocolate global usings are removed in `Directory.Build.targets`, not the csproj,
  because package `.targets` are imported after the csproj body
- `JsonSerializerOptions.MakeReadOnly` needs `populateMissingResolver: true`
- `[Produces]` on a controller base breaks `application/problem+json`
- xUnit **v2**, not v3: xunit.v3 4.0.1 bundles Microsoft.Testing.Platform 2.4.0, which
  fails the `dotnet test` server-mode handshake on SDK 10.0.400. Tests run fine standalone
  but `dotnet test` reports "Zero tests ran" with exit code 5 - CI goes green having
  verified nothing
- EF reserves `Dictionary<string, object>` for shared-type entity types, so
  `configurationBuilder.Properties<Dictionary<string, object?>>()` is illegal
- Nullable EF value converters are only needed for **structs**. For reference types like
  `JsonObject`/`CrontabSchedule`, `T?` erases to `T`, so `Nullable*Converter` variants are
  redundant duplicates
- MSBuild XML comments cannot contain a double hyphen
- `dotnet ef migrations add` **must** be given `-o`, or it defaults to `Migrations/` at the
  project root instead of the existing per-provider folder

---

## 9. Open work

### 9.1 Health checks - done

Implemented in phase N (ADR-0014). Authentication (9.2) is now the next task.

### 9.2 Authentication

Explicitly deferred. The template is auth-**ready**, not auth-implemented: correct
`UseAuthentication`/`UseAuthorization` pipeline ordering, an empty policy seam, and an
unimplemented `ICurrentUser` port.

The agreed direction when it is picked up is a **resource server** consuming
`API-IdentityAuthorizationHub` - validate Hub-issued JWTs, optional cached introspection for
real revocation, permission-based policies/requirements/handlers, `IClaimsTransformation`.
**Not** a second token issuer. See ADR-0006.

### 9.3 CI

- `health-check-url` is empty, so post-deploy verification is off. It needs the public
  hostname from the Nginx Proxy Manager rule, which is not knowable from this repo.
- `sonar-enforce-quality-gate` is now `true`. It was `false`, meaning Sonar reported
  findings that nothing acted on. The first enforced run may fail on thresholds.

### 9.4 Smaller items

- HomeLab's `Dockerfile.dotnet` still uses `aspnet:9.0`; offer a bump to `10.0`.
- HomeLab has **no Alertmanager**, so there is nothing to wire alerts into. Stack gap, not
  a template task.
- `Data.GrpcApi/Protos/greeter.proto` generates both client and server stubs so the
  integration test can host a server. Removing the sample proto also removes that test.

---

## 10. Hard constraints

### CI/CD paths

`.github/workflows/ci-cd.yml` calls the reusable workflow
`OFA-TECH/.github/.github/workflows/ci-cd.yml@main` on a self-hosted runner and **hard-codes
these paths**:

```
Source/WebApi/WebApi.csproj
Tests/UnitTests/UnitTests.csproj
Dockerfile              (repo root)
docker-compose.yml      (repo root)
```

Portainer swarm stack `dotnet-web-api-template`, endpoint 1, swarm id
`<YOUR SWARM ID>`. The layout must keep these.

### Analysers

`TreatWarningsAsErrors=true` with NetAnalyzers + SonarAnalyzer.CSharp as
`GlobalPackageReference`. Any new code must be analyser-clean.

Justified global `NoWarn`: `CS1591`, `CA1062`, `CA1848`, `CA2007`, `CA1515`, `CA2227`,
`CA1002`, `CA1716`, `CA1711`. Escalated to **errors** in `.editorconfig`: `ASP0000`,
`CA2016`, `CA5359`, `CA5386`, `CA5397`.

`GenerateDocumentationFile` stays `true` even with zero XML docs, because `IDE0005` (unused
usings, an error here) only functions when it is on.

### Model-drift gate

`.github/scripts/check-ef-model-drift.sh` runs `dotnet ef migrations has-pending-model-changes`
for all six contexts. CI runs it as the shared build stage's verify step (`run-verify` /
`verify-command` in `ci-cd.yml`), right after the Release build and before the tests, so a
model change without regenerated migrations stops the pipeline before tests, Sonar, the image
and the deploy. Scripts used only by workflows live in `.github/scripts/`. Verified to fire in
both directions.

When testing this locally, note that restoring a probe file with `Copy-Item` preserves the
source's `LastWriteTime`, so MSBuild skips recompiling and the check reports stale results.
Touch the file first.

---

## 11. Commands

```bash
dotnet build -c Release            # must be clean
dotnet test                        # 621 tests

# Integration tests need a Docker daemon - they start their own
# Postgres, MongoDB and RabbitMQ via Testcontainers.

# Adding a migration - -o is mandatory
dotnet ef migrations add <Name> \
  --project Source/Infrastructure/Data/Data.Sql \
  --startup-project Source/WebApi \
  --context PostgresqlAppDbContext \
  -o EntityFrameworkContexts/Migrations/AppDbContext.Postgresql

# Drift check, all six contexts (Git Bash; needs a Release build first)
bash .github/scripts/check-ef-model-drift.sh

# Drift check, per context
dotnet ef migrations has-pending-model-changes \
  --project Source/Infrastructure/Data/Data.Sql \
  --startup-project Source/WebApi \
  --context PostgresqlAppDbContext
```

---

## 12. Decision index

| ADR | Subject |
|---|---|
| 0001 | Domain as a shared kernel |
| 0002 | Mapster over AutoMapper |
| 0003 | Hand-rolled dispatcher over MediatR |
| 0004 | Native `IDistributedCache`, no custom abstraction |
| 0005 | Single SQL provider - **superseded by 0008** |
| 0006 | Authentication deferred |
| 0007 | Non-obvious constraints - **read before editing build or test infrastructure** |
| 0008 | Multi-provider SQL |
| 0009 | Provider strategies and modules |
| 0010 | Dapper access layer |
| 0011 | Secret resolution |
| 0012 | Infrastructure grouping |
| 0013 | Shells versus implementations |
| 0014 | Health checks follow the dependency kind; timeouts; dashboard |
| 0015 | Unified database connections - SQL and NoSQL in one `Settings:Databases` list |
| 0016 | Cache and broker connections - `Settings:Caches` / `Settings:Brokers` lists, TTL and messaging names on the entry |
