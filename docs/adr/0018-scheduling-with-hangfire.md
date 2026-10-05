# 18. Scheduled and background jobs with Hangfire

Status: Accepted

## Context

The template had no way to run work on a schedule. The only background loops were the
outbox publisher and broker consumers, each a hand-written `BackgroundService`. Recurring
maintenance - the outbox table, for one, grew forever - had nowhere to live, and a
hand-rolled timer per job gives no retries, no history, no dashboard and no coordination
between replicas.

## Decision

### A Platform module, Hangfire kept behind Domain ports

`Source/Infrastructure/Platform/Scheduling` is a new project (references `Domain` only) with a
`SchedulingModule` the loader activates when `Settings:Scheduling:Enabled` is true.
Hangfire types never leave it (`LayerDependencyTests.OnlySchedulingReferencesHangfire`).
Application writes jobs against `Domain.Interfaces.Scheduling`:

| Port | Purpose |
|---|---|
| `IRecurringJob` | `Id`, default `Cron`, `ExecuteAsync(IJobProgress, CancellationToken)` |
| `IBackgroundJob` | a fire-and-forget / delayed job |
| `IJobScheduler` | `Enqueue<TJob>()`, `Schedule<TJob>(delay)`, `Trigger(recurringJobId)` |
| `IJobProgress` | `Report(percent)` - a progress bar in the dashboard console |

`Application.Setup.JobsSetup` registers every `IBackgroundJob` / `IRecurringJob` in the
Application assembly. Jobs are resolved in a DI scope per run, so they use
`[FromKeyedServices(<Feature>Store.DatabaseId)]` exactly like handlers (ADR-0017).

Hangfire only ever stores two job types, `RecurringJobRunner.RunAsync(jobId)` and
`BackgroundJobRunner.RunAsync(jobType)`. Renaming or moving a job class therefore does not
break jobs already in storage, and a recurring job whose class is deleted is removed on the
next start (`RecurringJobRegistrar`). Duplicate recurring job ids fail startup
(`RecurringJobCatalog`).

### Console

`Hangfire.Console.Extensions` routes `ILogger` output written during a job to that job's
console in the dashboard, so jobs log normally and never touch `PerformContext`. It brings
the maintained `IdentityStream.Hangfire.Console` fork; the original `Hangfire.Console` must
not be referenced alongside it (duplicate `IProgressBar`).

### Settings

```json
"Scheduling": {
  "Enabled": true,
  "Storage": { "Type": "Memory", "DatabaseId": null, "Schema": "hangfire" },
  "Workers": 5, "Queues": ["default"], "RetryAttempts": 3,
  "Dashboard": { "Enabled": false, "Path": "/hangfire", "ReadOnly": true },
  "Jobs": [ { "Id": "outbox-cleanup", "Cron": "0 3 * * *", "Enabled": true, "TimeZone": "UTC" } ]
}
```

- `Storage:Type` is `Memory` or `Database`. `Database` reuses a `Settings:Databases` entry's
  connection string (`DatabaseId`); the entry's engine picks the Hangfire store. Supported:
  **Postgresql** (`Hangfire.PostgreSql`) and **SqlServer** (`Hangfire.SqlServer`). Other engines
  fail validation with the key. Hangfire creates and migrates its own schema (`Schema`), so the
  EF model and migrations are untouched.
- `Jobs[]` overrides a job's code-declared cron, time zone, or disables it, and is **live**:
  the registrar re-applies it on every configuration reload (ADR-0011 reload table).
- Validation (startup and reload): workers >= 1, non-blank queues, unique job ids, parseable
  cron, known time zone, supported storage database.

### Replicas and memory storage

In-memory storage is per process: every replica runs every recurring job and jobs are lost on
restart. It is the default because the template runs one replica and needs no schema. A
Production start with memory storage logs a warning; use `Database` before scaling out.

### Dashboard

Off by default, on in Development (`/hangfire`, not read-only). It is protected by Hangfire's
`LocalRequestsOnlyAuthorizationFilter`: behind the proxy the forwarded client address is not
loopback, so it is unreachable from outside until authentication (ADR-0006) lands. Read-only
by default so a local session cannot delete or re-trigger jobs.

### Health and telemetry

`DependencyKind.Scheduler` (ADR-0014): the `scheduler` check reports whether a job server has
sent a heartbeat in the last two minutes, plus enqueued/failed/recurring counts. It degrades
`/health` and never gates `/ready` - the API serves requests without its job server. The
storage database already has its own `db:<Id>` check. Job execution is traced through
`OpenTelemetry.Instrumentation.Hangfire` when tracing is on.

### Example: outbox cleanup

`Application.Jobs.OutboxCleanupJob` (`outbox-cleanup`, daily 03:00 UTC) deletes outbox rows
processed more than `Settings:Databases[].Sql:Outbox:RetentionDays` (7) days ago through the
new keyed `IOutboxMaintenance` port (EF `ExecuteDeleteAsync`, so every SQL engine works).

## Packages and pins

`Hangfire.Core` / `.AspNetCore` / `.SqlServer` 1.8.25, `Hangfire.InMemory` 1.0.0,
`Hangfire.PostgreSql` 1.21.1, `Hangfire.Console.Extensions` 2.1.2,
`OpenTelemetry.Instrumentation.Hangfire` 1.19.0-beta.1. `Newtonsoft.Json` is not referenced directly:
Hangfire.Core alone would resolve its minimum, 11.0.1, which has a high-severity advisory
(GHSA-5crp-9r3c-p9vr) and fails the build under `TreatWarningsAsErrors`, but
`OpenTelemetry.Instrumentation.Hangfire` requires >= 13.0.1, which is patched. If that package is
ever removed, pin `Newtonsoft.Json` again (`Directory.Packages.props` + `Scheduling.csproj`).

## Consequences

- Recurring work has retries, history, a dashboard and per-job logs with no Hangfire in
  Application.
- A second replica needs `Storage:Type Database` on Postgres or SQL Server.
- MySQL, SQLite, MongoDB and Redis job stores were left out: their Hangfire providers are
  community-maintained with irregular releases. Adding one is a new `case` in
  `SchedulingSetup.UseStorage` plus the engine in `AppSettings.SupportedSchedulingEngines`.

## Amendment: MongoDB job storage

`Storage:Type Database` now accepts a **MongoDb** entry as well as Postgresql and SqlServer
(`Hangfire.Mongo` 1.17.0, which requires the same `MongoDB.Driver` 3.12.0 the NoSql module
uses). The storage reuses the keyed `IMongoClient` the NoSql module registers for that entry,
so there is one connection and no reference from Scheduling to `Data.NoSql`. `Schema` is the
collection prefix (`hangfire.job`, `hangfire.server`, ...). Collections migrate automatically
on package upgrades, with a backup copy first (`MigrateMongoMigrationStrategy` +
`CollectionMongoBackupStrategy`), and the connection is checked at startup.

| Engine | Job storage |
|---|---|
| Postgresql, SqlServer, MongoDb | supported |
| CosmosDb | rejected: `Hangfire.AzureCosmosDB` unmaintained since 2023, builds its own client |
| RavenDb | rejected: `Hangfire.Raven` targets RavenDB.Client 3.5, cannot coexist with 7.x |
| DynamoDb | rejected: no Hangfire storage exists |
| Mysql, Oracle, Firebird, Sqlite | rejected: no storage wired |

Each rejection is a validation error naming `Settings:Scheduling:Storage:DatabaseId` and the
reason (`AppSettings.SupportedSchedulingEngines`).

**Standalone vs replica set.** `CheckQueuedJobsStrategy` is `TailNotificationsCollection`,
which works on a standalone `mongod` (the target deployment) using a capped notifications
collection. The package default, `Watch`, needs change streams and therefore a replica set;
switch to it in `SchedulingSetup.UseStorage` only if MongoDB is deployed as one.

The job store's MongoDB entry is an ordinary `Databases` entry, so it has its own
`db:<Id>` health check and gates `/ready` (ADR-0014). The template still defaults to
`Storage:Type Memory` and ships no MongoDB entry.