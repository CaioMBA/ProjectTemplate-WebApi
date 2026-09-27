# 1. Domain is a shared kernel, not an isolated DDD domain layer

Status: Accepted

## Context

Textbook Clean Architecture keeps entities, DTOs, interfaces and mapping profiles in
separate layers, with the domain at the centre depending on nothing.

All three reference repositories (`RestAPI-ProjectBase`, `API-IdentityAuthorizationHub`,
`ProjectTemplate-MauiBlazorHybrid`) instead put shared contracts in a project named
`Domain` that every other project references. That is not textbook DDD, but it is the
established convention across this codebase family.

## Decision

`Domain` is a **shared kernel**. It holds `Entities`, `Enums`, `Models`, `DTOs`,
`Mappings` (Mapster `IRegister` configurations), `Interfaces`, `Results`, `Guards`,
`Specifications` and `Extensions`. Every other project references it.

### Every contract lives in Domain

Every **interface, enum and record** - DTOs, models, options, persistence records, provider ports,
commands and queries - is declared in `Domain`, so any layer can use it without a new project
reference. Other projects hold behaviour only: implementations, abstract base classes, registries,
handlers, validators and `Setup`. Types nested inside a single class are exempt.

`Tests/ArchitectureTests/ContractLocationTests` enforces this per assembly.

Two consequences of the rule:

- `ISqlDatabaseProvider` in `Domain` is the EF-free port (engine, port, dialect, connection
  string, `DbConnection`, Dapper hook). The EF Core and health-check members live on the
  abstract `Data.Sql.Providers.SqlDatabaseProvider` class, so `Domain` still has no EF Core
  reference and `Domain_ContainsNoEntityFrameworkTypes` stays green.
- Commands and queries live in `Domain.Models.Requests.<Feature>`; their validators and
  handlers stay in the `Application` feature slice.

Consequently `Domain` carries NuGet dependencies it would not have under strict DDD:
Mapster, `Microsoft.Extensions.Caching.Abstractions` and data annotations.

## What this does NOT relax

The dependency **direction** is unchanged and is enforced by `Tests/ArchitectureTests`:

- `Domain` references no other solution project.
- `Application` references `Domain` and nothing else - in particular **never** a `Data.*`
  module.
- No `Data.*` module references another `Data.*` module.
- `Observability` references neither `Application` nor any `Data.*` module.

The rule that changed is "Domain has zero dependencies". The rule that did not change is
"the application layer must not depend on infrastructure" - which is precisely the rule all
three references broke, and the reason none of them can unit-test a handler without a
database.

## Consequences

Negative, and worth stating plainly:

- Any DTO change recompiles the entire solution.
- `Domain` cannot be unit-tested in isolation from Mapster.
- The project will accumulate types over time and needs periodic review.

Positive:

- One place to look for a contract.
- No duplicate DTO definitions drifting apart across layers.
- Mapping configuration lives beside the types it maps.
