# 12. Infrastructure projects grouped by role on disk

Status: Accepted

Amended by ADR-0017: `Data.Messaging` is renamed `Data.Broker`.

## Context

`Source/Infrastructure/` was one flat folder holding nine projects. Sorted alphabetically
the three API clients were separated by `Data.Messaging` and `Data.NoSql`, and `Data.Sql`
and `Data.NoSql` sat at opposite ends. The `Data.` prefix was doing the work a folder
should do.

## Decision

Projects are grouped by role, on disk and in the `.slnx`:

```
Source/Infrastructure/
├── Api/       Data.RestApi, Data.GraphqlApi, Data.GrpcApi
├── Data/      Data.Sql, Data.NoSql, Data.Cache, Data.Broker
└── Platform/  CrossCutting, Observability
```

Project names, assembly names and namespaces are unchanged. Only paths moved.

## Consequences

- `Dockerfile` copies each project file explicitly, so every move must update it. A
  forgotten `COPY` breaks only the Docker restore layer, which a local build never
  exercises, so an architecture test asserts the Dockerfile copies every project in the
  solution.
- Architecture tests assert every Infrastructure project sits under one of the three group
  folders, so a new project cannot be dropped at the old flat level.
- Visual Studio recreates empty `obj/` folders at old paths while the solution is open.
  They are harmless; reload the solution to stop it.
