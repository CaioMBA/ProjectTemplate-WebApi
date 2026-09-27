# 7. Non-obvious constraints

Status: Accepted

Amended by ADR-0017: `Data.Messaging` is now `Data.Broker`, and section 12 is obsolete - consumers exist and are switched on per broker entry (`Settings:Brokers[].EnableConsumer`).

## Context

This codebase carries no comments. Most of what was removed restated the code and deserved
to go.

A residue did not: a set of constraints where the *obvious* change is the wrong one. They
are recorded here because each one has already cost a debugging session, and because none
of them is discoverable by reading the line that depends on them.

Treat every entry as "changing this looks safe and is not".

---

## 1. `.editorconfig` must not be excluded from the Docker build context

**File:** `.dockerignore`

`.editorconfig` sets analyzer severities, including `dotnet_analyzer_diagnostic.severity = none`
for `**/Migrations/*.cs`. `TreatWarningsAsErrors` is on.

Excluding it makes `docker build` fail with analyzer errors in EF-generated migration code
that **never appear in a local build**, because locally the file is present. The failure
looks like a Docker problem and is not.

## 2. HotChocolate's global usings are removed in `Directory.Build.targets`, not the csproj

**File:** `Directory.Build.targets`

`HotChocolate.AspNetCore.targets` injects `global using HotChocolate;`,
`HotChocolate.Types`, `HotChocolate.Types.Relay` and `GreenDonut`. Three collide with the
core vocabulary of this solution:

| Injected | Collides with |
| --- | --- |
| `HotChocolate.Error` | `Domain.Results.Error` |
| `GreenDonut.Result<T>` | `Domain.Results.Result<T>` |
| `HotChocolate.Path` | `System.IO.Path` |

The `<Using Remove .../>` items cannot live in `WebApi.csproj`: MSBuild imports package
`.targets` **after** the project file, so the removal would run before the items exist.
`Directory.Build.targets` is imported last, which is the entire reason that file exists.

Deleting the `ItemGroup` reintroduces `CS0104` across every controller and handler.

## 3. A retrying execution strategy must own the transaction

**Files:** `Data.Sql/Providers/*Provider.cs` (`ConfigureProvider`), `Data.Sql/Repositories/UnitOfWork.cs`,
`Application/Behaviors/TransactionBehavior.cs`

`EnableRetryOnFailure` installs `NpgsqlRetryingExecutionStrategy`. Calling
`BeginTransactionAsync` outside that strategy throws:

> The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support
> user-initiated transactions.

This is not an edge case - it fails **every write**. `IUnitOfWork.ExecuteInTransactionAsync`
exists solely so the strategy wraps the whole unit of work and can replay it as one atom.

Consequence: the delegate may run **more than once**. Database work is covered by the
rollback; a non-transactional side effect inside a handler is not, and must be idempotent
or opt out via `ITransactionalRequest`.

## 4. xUnit v2, not v3

**File:** `Directory.Packages.props`

`xunit.v3` 4.0.1 bundles Microsoft.Testing.Platform 2.4.0, which does not complete the
`--server dotnettestcli` handshake `dotnet test` uses on SDK 10.0.400.

The failure mode is the dangerous one: the test assembly runs every test correctly when
executed directly, but `dotnet test` reports **"Zero tests ran"** and exit code 5. CI goes
green having verified nothing.

Revisit only when `xunit.v3` ships against MTP >= 2.4.1 **and** `dotnet test` is observed
to discover tests. The migration is a package swap plus
`UseMicrosoftTestingPlatformRunner`; no test code changes, since the assertions are
framework-agnostic.

## 5. `JsonSerializerOptions.MakeReadOnly` needs `populateMissingResolver: true`

**File:** `Domain/Extensions/JsonDefaults.cs`

The parameterless overload throws `InvalidOperationException` unless a `TypeInfoResolver`
has already been assigned. The argument installs the reflection-based
`DefaultJsonTypeInfoResolver`.

If this template is ever published with `PublishTrimmed` or NativeAOT, replace it with a
source-generated `JsonSerializerContext` - reflection-based resolution is exactly what
trimming removes.

## 6. Serialize the runtime type, never the declared type

**Files:** `Domain/Extensions/ObjectExtension.cs`, `Data.Sql/Outbox/OutboxWriter.cs`,
`Data.Messaging/Providers/*`

`JsonSerializer.Serialize(value, options)` infers the type parameter from the **declared**
type. A method taking `IIntegrationEvent` therefore serialized only the interface's three
properties, and every outbox row and broker message went out carrying `eventId`,
`occurredOnUtc` and `eventType` with **no business data at all**.

`ToJson`, `ToJsonIndented` and `ToJsonBytes` now pass `value.GetType()` explicitly. Any new
serialization helper must do the same. `UnitTests/Domain/JsonSerializationTests.cs` pins
this.

## 7. No `[Produces]` on the controller base

**File:** `WebApi/Controllers/ApiControllerBase.cs`

`[Produces("application/json")]` forces the content type of every action result, silently
overriding the `application/problem+json` set on the failure path. RFC 9457 documents then
go out labelled `application/json`, and a client negotiating on media type cannot tell a
payload from a problem.

## 8. The context is a keyed scoped service, not `AddDbContext`

**File:** `Data.Sql/Setup/AddDataSqlSetup.cs`

*Superseded wording: this section used to require both `AddDbContext` and
`AddDbContextFactory`. ADR-0017 replaced both because EF's registrations cannot be keyed.*

Each relational entry registers `AddKeyedScoped<AppDbContext>(Id, ...)`, built by the entry's
provider (`SqlDatabaseProviderBase.CreateContext`). Request handling resolves it from the
request scope; `OutboxPublisher`, which runs outside any request, opens its own scope through
`IServiceScopeFactory` per polling cycle. Do not add `AddDbContext`/`AddDbContextFactory`
back: an unkeyed context would silently resolve against whichever entry registered last.

## 9. Integration tests configure via environment variables, not `ConfigureAppConfiguration`

**File:** `Tests/IntegrationTests/ApiFactory.cs`

Under minimal hosting, the top-level statements in `Program.cs` read
`builder.Configuration` **before** `builder.Build()`, whereas `WebApplicationFactory`
applies its configuration callbacks **during** the build. In-memory values therefore arrive
too late to influence which database the `DbContext` is registered against.

Observed symptom: the suite passed while a developer's local Postgres happened to be
listening on `127.0.0.1:5432`, because the app fell back to the `appsettings` default and
connected *there* instead of to the test container. The tests were green and exercising the
wrong database. They only failed once that local container was stopped.

`WebApplication.CreateBuilder` reads environment variables while constructing the builder,
so values set in `InitializeAsync` are visible to those statements.

## 10. Architecture rules are checked two ways, and both are needed

**Files:** `Tests/ArchitectureTests/LayerDependencyTests.cs`,
`Tests/ArchitectureTests/ProjectReferenceTests.cs`

Roslyn omits an assembly reference whose types are never used. Adding
`<ProjectReference Include="...Data.Sql.csproj" />` to `Application.csproj` therefore passes
every assembly-level check until the first line of code that uses it.

Verified by introducing that exact violation: the assembly test stayed green and only
turned red once a `Data.Sql` type was referenced. `ProjectReferenceTests` reads the
`.csproj` files directly and fails the moment the reference is added, which is when it is
cheap to reverse.

## 11. The four OpenTelemetry resource attributes are mandatory

**Files:** `Infrastructure/Observability/*`, `docker-compose.yml`

Grafana Alloy's `loki_hints` promotes exactly `service.name`, `service.namespace`,
`service.instance.id` and `deployment.environment.name` to Loki labels. Omit one and the
logs dashboard's `$service_name` variable is empty.

`OpenTelemetry.Instrumentation.Process` is likewise not optional: the Grafana dashboards
query `process_cpu_time_seconds_total`, `process_memory_usage_bytes` and
`process_uptime_seconds`, and three panels stay blank without it.

OTLP goes to Alloy only. Loki, Tempo and Prometheus are internal-only on the
`shared-network` overlay and speak different protocols. No auth headers - Loki runs with
`auth_enabled: false`.

## 12. (Obsolete) `Modules:Messaging:EnableConsumer` is a seam with no implementation

> Obsolete: `RabbitMqEventConsumer` / `KafkaEventConsumer` exist and run for every broker
> entry with `EnableConsumer: true` (ADR-0017). The text below is kept for history.

**File:** `Domain/Models/Configuration/ModulesOptions.cs`

`Data.Messaging` publishes only. There is no consumer `BackgroundService` and no
`IIntegrationEventHandler` implementation. The flag is a declared extension point for
downstream projects, in the same spirit as `ICurrentUser` for deferred authentication
(ADR 6) - but unlike `ICurrentUser`, nothing is wired behind it yet.

Setting it to `true` has no effect today.

## 13. `Deprecated` on a controller must return a constant

**Files:** `WebApi/Controllers/ApiControllerBase.cs`, `WebApi/Setup/AddVersioningSetup.cs` (`VersionByFolderConvention`)

API versions come from the controller's `Controllers/V<n>` namespace, and a controller deprecates its
version with `protected override bool Deprecated => true;`. Versioning conventions run once at
startup against the controller *type*, before any instance exists and without DI, so the convention
reads the property from an instance created with `RuntimeHelpers.GetUninitializedObject` - no
constructor runs and every injected field is null. An override that reads constructor state (`=>
sender is null`, configuration, ...) therefore reports the wrong value or throws at startup. Keep it a
literal.
