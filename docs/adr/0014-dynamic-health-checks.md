# 14. Health checks follow the dependency kind

Status: Accepted

Amends ADR-0017 (the "Not in use" health result is removed).

## Context

`/health`, `/live` and `/ready` existed, but:

- **Readiness was vacuous.** `/ready` selected the `critical` tag, which only relational
  databases carried. After ADR-0017 made entries lazy, even that check reported
  Healthy/"Not in use" until a request touched the database, so a freshly started replica
  declared itself ready having checked nothing. `HealthReport` starts at `Healthy` and only
  downgrades per entry, so an empty or skipped set is indistinguishable from a passing one.
- **Criticality was inconsistent.** SQL failed as Unhealthy, document databases and brokers
  only as Degraded, and each provider chose its own tags.
- **No timeouts.** A dependency that accepts TCP and never answers hung the probe for the
  whole client timeout.
- **Nothing kept dependencies out of `/live`.** A dependency tagged `self` would turn a
  database blip into an orchestrator restart storm.
- **No dashboard.**

An earlier draft of this ADR made readiness and criticality configurable per entry
(`Health { Ready, Critical }`) and added a `Checks[]` list of extra URL/TCP probes. Both were
rejected: whether a dependency gates traffic follows from what kind of dependency it is, and
every real dependency already has an entry in `Databases`, `Caches`, `Brokers` or `Apis`.

## Decision

### The kind of dependency decides; nothing is configured per entry

`Domain.Abstractions.HealthCheckPolicy.For(DependencyKind)` is the single table:

| Kind | In `/ready` | On failure | Tags |
|---|---|---|---|
| `Self` | yes | Unhealthy | `self`, `ready` |
| `Database` (relational) | yes | Unhealthy (503) | `db`, `ready`, `critical` |
| `DocumentDatabase` | yes | Unhealthy (503) | `db`, `nosql`, `ready`, `critical` |
| `Broker` | yes | Unhealthy (503) | `broker`, `ready`, `critical` |
| `Cache` | no | Degraded (200) | `cache` |
| `Api` | no | Degraded (200) | `api` |

The service cannot do its job without its databases and brokers; it can without a cache (a
miss falls through to the database) or an optional outbound API. `critical` now means only
"failure is Unhealthy"; `ready` means "included in readiness".

Every registration goes through `HealthCheckPolicy.Registration(kind, name, probe)`.
`Tests/ArchitectureTests/HealthCheckPolicyTests` fails if any source file outside the policy
calls `new HealthCheckRegistration`, `AddCheck`, `AddTypeActivatedCheck`, `AddAsyncCheck` or
`AddUrlGroup`. Tag names are `HealthCheckTags` constants.

### Every configured entry is probed

The "Not in use" shortcut is gone. A health check resolves its entry on demand, which builds
the connection if nothing has yet, and probes it. The policy wraps the probe so that an entry
that cannot even be built (missing host, bad configuration) reports its failure status with
the configuration key in the exception, instead of throwing out of `HealthCheckService` and
failing the whole endpoint with a 500.

`LazyConnection<T>` no longer caches a failed creation: the next access retries, so a
dependency that was down at the first probe recovers without a restart.

Consequence: every database and broker in `Settings` must be reachable for `/ready` to pass.
The template therefore ships only the entries it uses (Postgres `DEFAULT`, memory cache
`DEFAULT`); examples for the other engines live in the README.

### Endpoints

| Endpoint | Includes | Writer |
|---|---|---|
| `/live` | `self` only | minimal |
| `/ready` | tag `ready`: `self` + databases + brokers | detailed / minimal (`ExposeDetails`) |
| `/health` | everything | detailed / minimal (`ExposeDetails`) |
| `/health-ui` | dashboard, polls `/health-ui-api` | HealthChecks.UI |

`/health` keeps `HealthCheckResponseWriter` and its `{"status":...}` shape, because the
Dockerfile and compose `HEALTHCHECK` pipe it through `jq -r .status`. The dashboard polls a
separate `/health-ui-api` written by `UIResponseWriter`, so a UI package upgrade cannot change
the container's health contract.

A startup guard (`HealthChecksSetup.ApplyTimeoutsAndGuard`) throws if any check other than
`self` carries the `self` tag.

### Timeouts

`Observability:HealthChecks:TimeoutSeconds` (default 5) is applied to every registration that
did not set its own. A hung dependency reports its failure status instead of stalling the
probe; the integration suite pauses the Postgres container and asserts `/ready` returns 503
while `/live` stays 200.

### Dashboard

`Observability:HealthChecks:UiEnabled` (off by default, on in Development) maps `/health-ui`
(`UiPath`) and `/health-ui-api` (`UiApiPath`), evaluated every `UiEvaluationSeconds` with
in-memory storage - persistent storage would add a schema this template does not own to the
application database.

Packages (all 9.0.0, Apache-2.0): `AspNetCore.HealthChecks.UI`, `.UI.Client`,
`.UI.InMemory.Storage`, plus `Microsoft.EntityFrameworkCore.InMemory` 10.0.x. Two pins are
required on .NET 10:

- `KubernetesClient` 19.0.2 - the UI package pulls 15.0.1, which has a known moderate
  vulnerability (GHSA-w7r3-mgwf-4mqq) and fails the build under `TreatWarningsAsErrors`.
- `IdentityModel` 5.2.0 - KubernetesClient 19 no longer brings it, but the UI collector
  still loads it at runtime; without it the collector throws `FileNotFoundException`. The
  package is marked legacy (successor `Duende.IdentityModel`), which is acceptable until the
  UI package ships a .NET 10 build.

`ExposeDetails` and `/health-ui` stay off in Production. When authentication lands
(ADR-0006) they are the first endpoints to sit behind a policy.

## Consequences

- `/ready` now tests something from the first request, and a down database or broker takes
  the replica out of rotation without restarting it.
- Adding a database or broker entry makes it a readiness dependency; adding a cache or API
  entry never does.
- Providers supply a probe, not a policy.
- Each `/ready` call opens a connection per database and broker. Orchestrator probe intervals
  (Swarm: 30 s) keep that negligible.
