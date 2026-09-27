# WebApi Project Template

.NET 10 Web API template. Clean-Architecture-derived layering, a shared-kernel domain,
plug-and-play infrastructure modules, and OpenTelemetry pre-wired for the HomeLab Grafana
stack.

This is a **GitHub template repository** - use *Use this template*, then follow
[Renaming the template](#renaming-the-template).

> **Picking this repository up without the history?** Read
> [`docs/PROJECT-STATE.md`](docs/PROJECT-STATE.md) first. It records what was built and
> why, what was verified against real infrastructure, the traps where the obvious change is
> wrong, and what is still open.

---

## Quick start

```bash
# Postgres is the only prerequisite for a default local run.
docker run -d --name webapi-template-db \
  -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=webapi_template \
  -p 5432:5432 postgres:17-alpine

dotnet run --project Source/WebApi/WebApi.csproj
```

A fresh clone uses **Postgres + an in-memory cache** and nothing else; examples for the other
engines are under [Connections and modules](#connections-and-modules).
In Development, migrations are applied at startup and the documentation UIs are served.

| Endpoint | Purpose |
| --- | --- |
| `/api/v1/products` | Worked example, REST |
| `/swagger` | SwaggerUI |
| `/scalar` | Scalar |
| `/graphql` | Generated GraphQL schema, on by default and public until auth exists (ADR 0019); query tool in Development |
| `/health` `/live` `/ready` | Aggregate, liveness, readiness |
| `/health-ui` | Health dashboard (Development, or `UiEnabled`) |
| `/hangfire` | Hangfire job dashboard (Development, local requests only) |

```bash
dotnet test          # 516 tests: 339 unit, 102 architecture, 75 integration
```

Integration tests need a Docker daemon - they start their own Postgres, MongoDB and
RabbitMQ via Testcontainers.

---

## Structure

```
Source/
  Domain/                    Shared kernel. Referenced by everything; references no project.
  Application/               Dispatcher, pipeline behaviours, feature slices. Depends on Domain ONLY.
  Infrastructure/
    Api/
      Data.RestApi/          Outbound REST client
      Data.GraphqlApi/       Outbound GraphQL client
      Data.GrpcApi/          Outbound gRPC client; drop .proto files in Protos/
    Data/
      Data.Sql/              EF Core + Dapper, 6 engines, interceptors, outbox
      Data.NoSql/            MongoDB, CosmosDB, DynamoDB, RavenDB
      Data.Cache/            IDistributedCache: Memory or Redis
      Data.Broker/           IEventBus + consumer: RabbitMQ or Kafka
    Platform/
      Observability/         OpenTelemetry, health checks, Pyroscope profiling
      Scheduling/            Hangfire jobs: memory/MongoDB/Postgres/SQL Server storage, console, dashboard
      CrossCutting/          Composition root, module loader, system access
  WebApi/                    Controllers only. No Minimal APIs.
Tests/
  UnitTests/  IntegrationTests/  ArchitectureTests/
```

Grouping is enforced: an architecture test fails if an Infrastructure project sits outside
`Api/`, `Data/` or `Platform/`. Project names and namespaces are unaffected by the folders.

### Dependency rules

```
Domain          ->  no project
Application     ->  Domain
Data.*          ->  Domain              (never Application, never each other)
Observability   ->  Domain
CrossCutting    ->  everything
WebApi          ->  CrossCutting, Application, Domain, Observability
```

These are **tests, not documentation**. `Tests/ArchitectureTests` checks them two ways:

- compiled assembly references, which catch a forbidden type actually being used;
- `ProjectReference` items in the `.csproj` files, which catch the reference being added at
  all.

Both are needed. Roslyn omits references to assemblies whose types are never used, so a
stray `<ProjectReference>` stays invisible to the assembly check until someone writes the
first line of code that depends on it.

### The `Setup/` convention

Every project owns its own DI wiring in a `Setup/` folder:

```
file  Add{Concern}Setup.cs
class {Concern}Setup
method Add{Concern}Setup(this IServiceCollection services, ...)
```

Pipeline counterparts live in `Use{Concern}Setup.cs`. Each project exposes one aggregate
method; `CrossCutting` chains the projects; `Program.cs` chains `CrossCutting` plus the
WebApi-local ones.

---

## Connections and modules

There is no `Modules` section. `Settings` holds four lists - `Databases`, `Caches`, `Brokers`,
`Apis` - and **every entry is registered under its `Id`** as .NET keyed services. A module is
active when its list has a matching entry; there is nothing to switch on.

```json
"Settings": {
  "Databases": [
    { "Id": "DEFAULT", "Type": "Postgresql", "Host": "localhost", "Database": "webapi_template",
      "Username": "webapi_template", "Password": null,
      "Sql": { "MaxPoolSize": 100, "MigrateOnStartup": false, "LogInterpolatedSql": false,
               "Outbox":     { "Enabled": false, "BrokerId": null, "PollIntervalSeconds": 10, "BatchSize": 50 },
               "PagedCache": { "CacheId": "DEFAULT", "MaxCachedPages": 50, "DefaultTtlMinutes": 5 } } }
  ],
  "Caches":  [ { "Id": "DEFAULT", "Type": "Memory", "DefaultTtlMinutes": 60 } ],
  "Brokers": [],
  "Apis":    []
}
```

The template ships only what it uses, because every database and broker entry is probed by
`/ready` (see [Health checks](#health-checks)). Add the others when a feature needs them -
copy-paste examples for each engine:

```json
"Databases": [
  { "Id": "DOCUMENTS", "Type": "MongoDb", "ConnectionString": "/run/secrets/mongo_connection", "Database": "webapi_template" },
  { "Id": "EVENTS_STORE", "Type": "DynamoDb", "Cloud": { "Region": "eu-west-1", "AccessKey": "/run/secrets/aws_key", "SecretKey": "/run/secrets/aws_secret" } },
  { "Id": "CATALOG", "Type": "RavenDb", "Database": "catalog", "Cluster": { "Urls": ["https://raven:443"], "CertificatePath": "/run/secrets/raven.pfx" } }
],
"Caches": [
  { "Id": "SHARED", "Type": "Redis", "Host": "redis_cache", "Port": 6379, "Password": "/run/secrets/redis_password",
    "DefaultTtlMinutes": 60, "Redis": { "Database": 0, "InstanceName": "webapi-template:" } }
],
"Brokers": [
  { "Id": "EVENTS", "Type": "RabbitMq", "Host": "rabbitmq_app", "Username": "webapi_template", "Password": "/run/secrets/rabbitmq_password",
    "EnableConsumer": true, "RabbitMq": { "VirtualHost": "/", "Exchange": "webapi-template.events", "Queue": "webapi-template.inbox" } },
  { "Id": "STREAM", "Type": "Kafka", "EnableConsumer": false,
    "Kafka": { "BootstrapServers": "kafka:9092", "TopicPrefix": "webapi-template.events", "ConsumerGroup": "webapi-template" } }
]
```

To publish the outbox, add a broker and set `Sql:Outbox:Enabled: true` and
`Sql:Outbox:BrokerId` on the database entry.
| Module | Active when | Registers per entry `Id` |
| --- | --- | --- |
| `Sql` | a relational `Databases` entry | `AppDbContext`, `IRepository<,>`, `IUnitOfWork`, `IOutboxWriter`, `ISqlDatabaseAccess`, `ISqlSyntax` |
| `NoSql` | a document `Databases` entry | `IDocumentRepository<>` and the engine client |
| `Cache` | a `Caches` entry | `IDistributedCache` |
| `Broker` | a `Brokers` entry | `IEventBus` (+ consumer when `EnableConsumer`) |
| `RestApi` / `GraphqlApi` / `GrpcApi` | an `Apis` entry with that `Protocol` | the client (calls pass the API `Id`) |

**Features name the entries they use.** Nothing connection-bound is registered without an
id, so a handler must say which entry it wants:

```csharp
public static class ProductsStore { public const string DatabaseId = "DEFAULT"; public const string CacheId = "DEFAULT"; }

public sealed class GetProductByIdQueryHandler(
    [FromKeyedServices(ProductsStore.DatabaseId)] IRepository<ProductEntity, Guid> repository) ...
```

Requests declare their entries for the pipeline: `IDatabaseRequest.DatabaseId` picks the
unit of work `TransactionBehavior` uses, and `ICacheRequest.CacheId` (on cacheable,
cache-invalidating and idempotent requests) picks the cache and its default TTL.
`ConnectionSelectionTests` fails the build when a handler dependency or a command omits it.

**Entries are built lazily.** A client, connection or engine check runs on first use - a
request or a health probe - and a broken one fails with its key
(`Settings:Databases[Id=DOCUMENTS]:ConnectionString`). Things configuration explicitly asks
for start with the service: migrations (`Sql:MigrateOnStartup`), outbox publishers
(`Sql:Outbox:Enabled`, publishing to `Sql:Outbox:BrokerId`) and consumers (`EnableConsumer`).
Cross-entry references, ranges and duplicate ids (including `Apis` and their `Endpoints`)
are checked at startup and on every configuration reload.

The entry's `Type` alone picks the engine. Shared fields sit on the entry; engine-specific
ones live in a block - `Sql`, `Cloud` (DynamoDb), `Cluster` (RavenDb), `Redis`, `RabbitMq`,
`Kafka`. `Port` is optional and defaults per engine. Environment overrides address entries
by index (`Settings__Caches__0__Type=Redis`). See ADRs
[0015](docs/adr/0015-unified-database-connections.md),
[0016](docs/adr/0016-cache-and-broker-connection-lists.md) and
[0017](docs/adr/0017-features-select-connections.md).

Each module is an `IInfrastructureModule` living in the project it activates.
`InfrastructureModuleLoader` holds the seven, rejects duplicate names, registers the active
ones and logs them at startup (`Sql(Postgresql@DEFAULT)`). Modules are instantiated
**explicitly**, not discovered by assembly scan.
### Scheduled jobs

`Platform/Scheduling` runs Hangfire when `Settings:Scheduling:Enabled` is true. Jobs live in
Application and implement Domain ports - no Hangfire types:

```csharp
public sealed class OutboxCleanupJob(
    [FromKeyedServices(ProductsStore.DatabaseId)] IOutboxMaintenance outbox,
    ILogger<OutboxCleanupJob> logger) : IRecurringJob
{
    public string Id => "outbox-cleanup";
    public string Cron => "0 3 * * *";

    public async Task ExecuteAsync(IJobProgress progress, CancellationToken cancellationToken) { ... }
}
```

Every `IRecurringJob` / `IBackgroundJob` in Application is registered automatically. `ILogger`
output and `IJobProgress` show up in the job's console on the `/hangfire` dashboard; inject
`IJobScheduler` to enqueue, delay or trigger jobs. `Settings:Scheduling:Jobs[]` overrides a
job's cron or time zone or disables it, live, without a restart.

`Storage:Type` is `Memory` (default; one replica - jobs are lost on restart and every replica
runs every recurring job) or `Database` with `DatabaseId` naming a **MongoDb**, Postgresql or
SqlServer entry; Hangfire creates its own `hangfire` schema or `hangfire.*` collections:

```json
"Databases":  [ ..., { "Id": "JOBS", "Type": "MongoDb", "ConnectionString": "/run/secrets/mongo_jobs", "Database": "webapi_jobs" } ],
"Scheduling": { "Enabled": true, "Storage": { "Type": "Database", "DatabaseId": "JOBS" } }
```

MongoDB works standalone (no replica set needed). The dashboard is local-requests-only until
authentication exists. See [ADR 0018](docs/adr/0018-scheduling-with-hangfire.md).

### GraphQL auto schema

`/graphql` is fully generated. With `Api:GraphQlServer:Enabled`, the server reads
every `Settings:Databases` entry at startup - catalog for SQL engines, sampled documents for
MongoDB/Cosmos/Raven/DynamoDB - and generates read-only queries for every table and collection
that is not excluded. Each database gets a root field named after its `Id`:

```graphql
{
  default {
    customers(
      where: { fullName: { contains: "silva" }, or: [{ city: { in: ["Lisbon", "Porto"] } }], not: { isActive: { eq: false } } }
      order: [{ createdAt: DESC }]
      skip: 0, take: 25) {
      totalCount
      aggregate { count max { credit } avg { credit } }
      items { id fullName orders(take: 5) { items { id total } } }
    }
    customersById(id: 42) { fullName }
    orders { items { customer { fullName } } }
  }
}
```

Operators per column type: `eq neq in nin gt gte lt lte contains ncontains startsWith
nstartsWith endsWith nendsWith like isNull`, combined with `and`/`or`/`not`. Foreign keys become
navigation fields both ways, loaded in one batched query per level. Hide what must not be
public - there is no authentication yet:

```json
"GraphQlServer": { "Enabled": true, "AutoSchema": { "ExcludeTables": ["audit_*"], "ExcludeColumns": ["*.password*", "customers.ssn"] } }
```

System tables (migrations history, outbox, Hangfire, catalogs) are never exposed. Tables are read
directly, so soft-deleted rows are visible - filter with `isDeleted: { eq: false }`. `{ autoSchemaStatus { databases { id exposed reason } } }`
reports which databases are exposed; if none can be (e.g. the database is down at startup) the app
still starts with only that field and `/health` reports the outage, so the orchestrator restarts it. A schema change needs a restart. See [ADR 0019](docs/adr/0019-graphql-auto-schema.md).

### gRPC
`Data.GrpcApi` resolves generated clients through a pooled `GrpcChannel` per API `Id`.
Drop a `.proto` into `Source/Infrastructure/Api/Data.GrpcApi/Protos/`, rebuild, then ask
for the generated client:

```csharp
var greeter = grpcApiClient.GetClient<Greeter.GreeterClient>("GREETER");
```

`ApiSettings.TimeoutSeconds` becomes a per-call deadline through an interceptor,
because gRPC deadlines are per call and not a channel setting. Authorization comes from the
same `IApiAuthenticationStrategy` the REST and GraphQL clients use.

### Providers

Every pluggable concern resolves an enum value to exactly one provider object through a
registry. A unit test asserts the mapping is total, so adding an enum member without an
implementation fails the build.

| Concern | Providers |
| --- | --- |
| `Settings:Databases[].Type`, relational | `Postgresql` `SqlServer` `Mysql` `Oracle` `Firebird` `Sqlite` |
| `Settings:Databases[].Type`, document | `MongoDb` `CosmosDb` `DynamoDb` `RavenDb` |
| `Settings:Caches[].Type` | `Memory` `Redis` |
| `Settings:Brokers[].Type` | `RabbitMq` `Kafka` |

`Mariadb` is declared and **throws on registration**: Pomelo pins EF Core Relational to
9.0.x and has no EF 10 build. Route it to `Mysql`, which it is wire-compatible with.

Both database families share one `DatabaseType` enum; each registry refuses the other
family. Switching SQL engine is one config value - the entry's `Type`. Each engine has its own migration set under
`Data.Sql/EntityFrameworkContexts/Migrations/AppDbContext.<Provider>/`, because EF Core
migrations bake in provider-specific column types. Application code never writes
dialect-specific SQL; it injects `ISqlSyntax`. See
[ADR 0008](docs/adr/0008-multi-provider-sql.md).

### Broker round-trips

`Data.Broker` publishes through a keyed `IEventBus` per broker entry and consumes through a
provider-specific `BackgroundService` for every entry with `EnableConsumer: true`.

A consumed message is resolved to a CLR type by `IIntegrationEventRegistry` (which reads
each event's `EventType` discriminator), then dispatched to every registered
`IIntegrationEventHandler<T>` in a fresh DI scope. RabbitMQ acks on success, requeues once
on failure, and dead-drops an unknown event type rather than looping on a poison message.
Kafka commits the offset only after the handler returns.

The **transactional outbox** stages integration events into `outbox_messages` inside the
same transaction as the state change. An `OutboxPublisher` runs for each database whose
`Sql:Outbox:Enabled` is true and drains it through the broker named by `Sql:Outbox:BrokerId`,
re-reading interval, batch size and broker on every poll; an unknown broker leaves rows pending.

### Removing a module permanently

1. Delete `Source/Infrastructure/<group>/<Module>/`.
2. Remove its `<ProjectReference>` from `CrossCutting.csproj`.
3. Remove its entry from `InfrastructureModuleLoader`.
4. Remove its entry from `ProjectTemplate-WebApi.slnx`.
5. Remove its entries from the `Settings` list in `appsettings.json`.
6. Remove its `COPY` line from `Dockerfile`.
7. Remove its `<ProjectReference>` from the test projects and its `[InlineData]` rows.

Nothing else knows it existed - every consumer binds to an interface in `Domain`.

### Removing a database engine

Delete `Data.Sql/Providers/<Engine>Provider.cs`, its two `PackageReference` entries, its
line in `SqlProviderRegistry`, its derived context, its design-time factory and its
migration folder.

### Adding a module

Create the project under the right group folder, reference `Domain` only, implement a
`Domain.Interfaces` port, add `Setup/Add<Module>Setup.cs` and a `Modules/<Module>Module.cs`,
then reverse the steps above. Register the module's own health check and OTel
instrumentation **inside its Setup file**, so deleting it later removes them cleanly.

---

## Observability

Pre-configured for the HomeLab stack. All three signals go to **Grafana Alloy** - never
directly to Loki, Tempo or Prometheus, which are internal-only on the `shared-network`
overlay and expect different protocols.

```
OTEL_EXPORTER_OTLP_ENDPOINT = http://monitoring_alloy:4317
OTEL_EXPORTER_OTLP_PROTOCOL = grpc
OTEL_SERVICE_NAME           = <service>
OTEL_RESOURCE_ATTRIBUTES    = service.name=<service>,service.namespace=homelab,
                              service.instance.id=<task-id>,deployment.environment.name=Production
```

**All four resource attributes are mandatory.** Alloy's `loki_hints` promotes exactly those
to Loki labels; omit one and the logs dashboard's `$service_name` variable is empty.

No auth headers - Loki runs with `auth_enabled: false` and there is no gateway in front of
Alloy.

Instrumentation covers AspNetCore, HttpClient, Runtime, **Process**, EF Core and Redis, and
emits **stable seconds-based semconv** metric names. Process instrumentation is required:
the Grafana dashboards query `process_cpu_time_seconds_total`, and three panels stay blank
without it.

Profiling is opt-in via `Observability:Pyroscope:Enabled`. Enabling it registers the
Pyroscope span processor so traces and profiles cross-link, but profiles are produced by
the **native profiler**, which attaches through container environment variables:

```
CORECLR_ENABLE_PROFILING = 1
CORECLR_PROFILER         = {BD1A650D-AC5D-4896-B64F-D6FA25D6B26A}
PYROSCOPE_SERVER_ADDRESS = http://monitoring_pyroscope:4040
PYROSCOPE_APPLICATION_NAME = <service>
```

Profiles push directly to Pyroscope, bypassing Alloy. If the flag is on and the profiler is
not attached, startup logs a warning naming the missing variables rather than silently
producing nothing.

### Health checks

Every `Settings` entry gets one check (`db:<Id>`, `cache:<Id>`, `broker:<Id>`, `api:<Id>`),
and **the kind of dependency decides what it means** - nothing is configured per entry:

| Kind | In `/ready` | On failure |
| --- | --- | --- |
| database (relational or document), broker | yes | Unhealthy - `/ready` and `/health` return 503 |
| cache, outbound API | no | Degraded - `/health` stays 200 |

`/live` checks only the process, so a dependency outage takes a replica out of rotation
without the orchestrator restarting it. Every entry is probed, even one no request has used
yet, so every configured database and broker must be reachable for `/ready` to pass. Each
check times out after `Observability:HealthChecks:TimeoutSeconds` (5). `/health` keeps its
`{"status":"Healthy"}` shape for the container `HEALTHCHECK`.

`/health-ui` is a dashboard (on in Development, `Observability:HealthChecks:UiEnabled`
elsewhere) that polls its own `/health-ui-api`. See
[ADR 0014](docs/adr/0014-dynamic-health-checks.md).

---

## Renaming the template

Namespaces are flat (`Domain`, `Application`, `Data.Sql`, ...), so there is no company
prefix to replace. What to change:

| Where | What |
| --- | --- |
| `appsettings.json` | `Settings:AppName`, `Observability:ServiceName` |
| `docker-compose.yml` | image name, `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`, secret names, DB/cache hosts |
| `.github/workflows/ci-cd.yml` | `docker-image-repository`, `portainer-stack-name` |
| `Source/Domain/Entities/` | replace `ProductEntity` and `Money` with your aggregate |
| `Source/Domain/Events/Integration/` | replace the product integration events |
| `Source/Application/Features/Products/` | replace the worked slice |
| `Source/Application/EventHandlers/` | replace the product event handlers |
| `Source/WebApi/Controllers/V1/` | replace `ProductsController` |
| `Data.Sql/EntityFrameworkContexts/Migrations/` | delete and regenerate all six sets |

Regenerating migrations, one command per engine - **`-o` is mandatory**, EF otherwise
writes to `Migrations/` at the project root:

```bash
dotnet ef migrations add InitialCreate \
  --project Source/Infrastructure/Data/Data.Sql \
  --startup-project Source/WebApi \
  --context PostgresqlAppDbContext \
  -o EntityFrameworkContexts/Migrations/AppDbContext.Postgresql
```

Repeat for `SqlServer`, `Mysql`, `Oracle`, `Firebird` and `Sqlite`. Then move each
generated `*ModelSnapshot.cs` next to its migrations, since EF writes snapshots to a folder
derived from the context name.

---

## Conventions worth knowing

- **Every interface, enum and record lives in `Domain`** - DTOs, models, options, provider
  ports, commands and queries. Other projects hold behaviour only. `ContractLocationTests`
  fails the build otherwise. See [ADR 0001](docs/adr/0001-shared-kernel-domain.md).
- **Private fields are `_camelCase`**, static ones included (`_providers`, `_activitySource`);
  `const` stays PascalCase. `IDE1006` is an error, so a violation fails the build.
- **The code carries no comments.** Names and structure are the explanation. Constraints
  that are genuinely invisible from the code - where the obvious change is the wrong one -
  live in [ADR 0007](docs/adr/0007-non-obvious-constraints.md). Read it before changing
  build files, the transaction path, or the test host.
- **Controllers only.** No Minimal APIs. `MapHealthChecks` and `MapGraphQL` are framework
  middleware mappings, not route handlers, and are the only two exceptions.
- **Versioning is a URL segment** - `/api/v1/...`. Unversioned requests are rejected, not
  silently routed to v1.
- **The version comes from the folder.** A controller in `Controllers/V2/` (namespace
  `WebApi.Controllers.V2`) serves `/api/v2/...`; `V2_1` is 2.1. Never write `[ApiVersion]`,
  `[MapToApiVersion]` or `[Route]` for the version - `ApiControllerBase` supplies
  `api/v{version}/[controller]`, kebab-cased (`OrderLinesController` -> `order-lines`). To
  deprecate, add `protected override bool Deprecated => true;` to the controller; it keeps
  serving and is reported in `api-deprecated-versions` and Swagger. `ControllerVersioningTests`
  enforce all of this.
- **One OpenAPI generator** (Swashbuckle) and two UIs over it (SwaggerUI, Scalar).
  `Microsoft.AspNetCore.OpenApi` is deliberately absent from the dependency graph.
- **Failures are `Result`, not exceptions.** Exceptions mean programmer error.
  `ApiControllerBase` maps `ErrorType` to a status code in one place.
- **Every error response is RFC 9457** `application/problem+json` with a `traceId`.
  Exception messages are never echoed outside Development.
- **A secret is one property.** Mark it `[Secret]`; the server decides. A path-shaped value
  must resolve to a readable file or startup fails, anything else is a literal, and
  `plain:` escapes a path-shaped literal. There are no `*File` companion properties.
  Secrets are resolved inside the options pipeline, so `IOptionsMonitor<AppSettings>`
  serves resolved values and re-resolves on reload; a broken reload keeps the last valid value.
  See [ADR 0011](docs/adr/0011-secret-resolution.md).
- **File, environment and machine access go through ports** - `IFileSystem`,
  `IEnvironmentAccessor`, `ISystemInfo` in `Domain.Interfaces.Platform`.
- **Migrations are a deployment step**, not startup work. `Settings:Databases[].Sql:MigrateOnStartup` is
  false outside Development and unsafe with more than one replica.
- **`TreatWarningsAsErrors` is on** with .NET analyzers and SonarAnalyzer. `ASP0000`,
  `CA2016` and the TLS rules are escalated to errors.
- **xUnit v2, not v3** - see `Directory.Packages.props` for the reason and the revisit
  condition.

---

## Architecture decisions

| ADR | Decision |
| --- | --- |
| [0001](docs/adr/0001-shared-kernel-domain.md) | Domain is a shared kernel |
| [0002](docs/adr/0002-mapster-over-automapper.md) | Mapster replaces AutoMapper |
| [0003](docs/adr/0003-hand-rolled-dispatcher.md) | Hand-rolled CQRS dispatcher |
| [0004](docs/adr/0004-native-distributed-cache.md) | Native `IDistributedCache`, no wrapper |
| [0005](docs/adr/0005-single-sql-provider.md) | PostgreSQL driver only (superseded by 0008) |
| [0006](docs/adr/0006-authentication-deferred.md) | Authentication deferred, seams in place |
| [0007](docs/adr/0007-non-obvious-constraints.md) | Constraints where the obvious change is wrong |
| [0008](docs/adr/0008-multi-provider-sql.md) | Six SQL engines, one migration set each |
| [0009](docs/adr/0009-provider-strategies-and-modules.md) | Provider strategies and module objects |
| [0010](docs/adr/0010-dapper-access-layer.md) | Hand-rolled Dapper layer, not Dapper.Contrib |
| [0011](docs/adr/0011-secret-resolution.md) | One secret property, resolved by the server |
| [0012](docs/adr/0012-infrastructure-grouping.md) | Infrastructure grouped by role on disk |
| [0013](docs/adr/0013-shells-versus-implementations.md) | Shape tests pass on empty shells |
| [0014](docs/adr/0014-dynamic-health-checks.md) | Health checks follow the dependency kind; timeouts; dashboard |
| [0015](docs/adr/0015-unified-database-connections.md) | Relational and document databases share one connection list |
| [0016](docs/adr/0016-cache-and-broker-connection-lists.md) | Cache and broker connections are lists selected by id |
| [0017](docs/adr/0017-features-select-connections.md) | Features select connections by entry id; Modules removed; Broker naming |
| [0018](docs/adr/0018-scheduling-with-hangfire.md) | Scheduled and background jobs with Hangfire |
| [0019](docs/adr/0019-graphql-auto-schema.md) | The GraphQL schema is generated from live database introspection |

Project history, verification status and open work live in
[`docs/PROJECT-STATE.md`](docs/PROJECT-STATE.md).

---

## Deployment

CI/CD calls the reusable `OFA-TECH/.github` workflow on a self-hosted runner: build the
solution, run unit + architecture + integration tests, Sonar with the quality gate
enforced, build and push the image, deploy the Swarm stack via Portainer.

The stack joins the external `shared-network` overlay and **publishes no ports** - ingress
is a host rule on Nginx Proxy Manager. Credentials are Docker Swarm secrets.

Post-deploy health verification is disabled because the public URL depends on the proxy
host rule; set `run-health-check` and `health-check-url` in the workflow to enable it.
