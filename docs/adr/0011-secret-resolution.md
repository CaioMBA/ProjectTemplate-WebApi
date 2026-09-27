# 11. One secret property, resolved by the server

Status: Accepted

Amended by ADR-0017: `ModulesOptions` no longer exists; `ApiEndPointConnectionModel` is `ApiEndpointSettings`.

## Context

Secrets were configured as property pairs: `Password` and `PasswordFile`,
`ConnectionString` and `ConnectionStringFile`, five pairs in all. The resolver preferred
the file, and fell back to the inline value when the file was missing.

That fallback was the bug. A typo in a Docker secret path did not fail. It silently used
whatever was in `appsettings.json`, which for RabbitMQ meant connecting as `guest`.

The pair also pushed the decision onto the caller. Choosing which of two properties to set
is the server's job, not the operator's.

## Decision

A secret is one property marked `[Secret]`. After configuration binding, `SecretResolver`
walks the bound settings graph once and resolves every marked value.

| Value | Behaviour |
|---|---|
| Path-shaped | Must read a readable file, or startup fails |
| Not path-shaped | Literal |
| `plain:` prefix | Literal, escape hatch for path-shaped literals |

Path detection is an explicit `[GeneratedRegex]` rather than `Path.IsPathRooted`, which is
OS-dependent: `C:\x` is not rooted on Linux.

The resolver also rejects directories, caps file size at
`SecretResolution:MaxFileSizeBytes`, trims the single trailing newline Docker secrets
carry, rejects all-whitespace contents, and supports an optional
`SecretResolution:AllowedRoots` allow-list.

## Resolution is scoped to marked properties on purpose

Scanning every configuration value would be simpler and would break immediately.
`VirtualHost` is `/`. `HealthEndpoint` is `/health`. `ApiEndPointConnectionModel.Path` is
`/api/v1/items`. All are path-shaped, none are secrets, and every one of them would fail
startup as a missing file.

## Consequences

- Five properties removed from the configuration surface.
- A missing secret file is a startup failure with the exact configuration key, the resolved
  path and the `plain:` escape in the message.
- Secrets resolve once at startup. Rotation requires a restart, which matches Docker Swarm
  where secrets are immutable.

## Amendment: secrets are resolved inside the options pipeline

The resolver originally ran only on the `AppSettings` that `Program.cs` builds for
service registration. `IOptions<AppSettings>` was bound a second time and never
resolved, so everything reading settings through options - the REST, GraphQL and gRPC
clients - received the secret's **file path** as the credential. A test that sends a request
through `RestApiClient` and inspects the `Authorization` header proved it.

Now both copies are prepared by `Domain.Setup.AppSettingsPreparation`:

| Copy | Resolve | Validate |
|---|---|---|
| `Program.cs` (`GetAppSettings`) | `AppSettingsPreparation.Prepare` | same call, throws |
| `IOptions` / `IOptionsSnapshot` / `IOptionsMonitor` | `PostConfigure<ISecretResolver>` | `AppSettingsValidator` (`IValidateOptions`), `ValidateOnStart` |

Because post-configuration runs every time options are built, a configuration reload is
resolved again - `IOptionsMonitor<AppSettings>.CurrentValue` always holds resolved
secrets.

### A broken reload keeps the last valid value

The framework's `OptionsMonitor` rebuilds the value inside the change-token callback. If that
throws - a secret file removed, a duplicate id introduced - the exception escapes
`IConfigurationRoot.Reload()` onto the file-watcher thread, and every later `CurrentValue`
read throws too.

`CrossCutting.Configuration.LastKnownGoodOptionsMonitor<T>` replaces it for
`AppSettings`, `ModulesOptions`, `ApiOptions` and `ObservabilityOptions`. The first
build still throws, so startup fails fast. A failed **reload** is logged at Error and the
previous value stays in use until the configuration is fixed; listeners are only notified of
values that built successfully.

### What reloads

Values read through `IOptionsMonitor` - outbound API credentials and endpoints, cache TTL -
follow a reload. Database, cache and broker connections are built once at registration and
still need a restart. Editing only a secret file does not trigger a reload; configuration
files and environment changes do.
## Amendment: one startup copy, validated options, and a reload policy

`Program.cs` builds a single `StartupSettings` record (`ConfigurationSetup.GetStartupSettings`):
`AppSettings`, `ApiOptions` and `ObservabilityOptions` bound once, with secrets resolved, the
`OTEL_*` / `HEALTHCHECK_PATH` environment overrides applied
(`ObservabilityPreparation.ApplyEnvironment`) and every validator run. All registration code
(`AddObservabilitySetup`, `AddCrossCuttingSetup`, modules, health checks) takes that record;
nothing else calls `.Get<T>()` on a configuration section (`SettingsBindingTests`).

The runtime copies (`IOptions*<T>`) run the same steps inside the options pipeline:
`PostConfigure` resolves secrets and applies the environment overrides, and
`AppSettingsValidator`, `ApiOptionsValidator` and `ObservabilityOptionsValidator` (the same
rule functions as the startup copy) fail startup and reject broken reloads.

### What reloads without a restart

`Domain.Setup.SettingsReloadPolicy.LivePaths` is the single list:

| Live (read through `IOptionsMonitor` on every use) | Restart required |
|---|---|
| `Settings:Apis[*]` auth, timeout, API-key header and `Endpoints` | adding/removing any entry; `Id`, `Type`, `Protocol`, `BaseAddress`, `HealthEndpoint` |
| `Settings:Caches[*]:DefaultTtlMinutes` | every connection field (host, port, credentials, blocks) |
| `Settings:Databases[*]:Sql:Outbox:PollIntervalSeconds`, `BatchSize`, `BrokerId` | `Outbox:Enabled`, `MigrateOnStartup`, `PagedCache`, pool sizes |
| `Settings:Scheduling:Jobs` (cron, enabled, time zone) | everything in `Api` and `Observability` |

`RestartRequiredSettingsWatcher` compares every reload against the startup copy and logs a
warning naming each changed startup-only key ("restart the service to apply:
Settings:Caches[0]:Type"), so an edit is never silently ignored.