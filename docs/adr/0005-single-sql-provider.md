# 5. Only the PostgreSQL driver ships

Status: Superseded by [ADR-0008](0008-multi-provider-sql.md)

Superseded by ADR-0008; the provider-switch steps below predate ADR-0015/0017 (the entry's `Type` is now the only selector).

The template now ships six working SQL engines, each with its own dialect, provider and
migration set. The reasoning below about driver weight and the BouncyCastle leak still
holds and explains why engines are removable one file at a time, but the decision itself -
Npgsql only - no longer describes the codebase. `ConnectionStringBuilderExtension` and the
hardcoded `UseNpgsql` call referenced here have both been replaced by
`Data.Sql/Providers/`.

## Context

`RestAPI-ProjectBase` references six database drivers - SQL Server, Oracle, MySQL,
PostgreSQL, Firebird and SQLite - in every build, while EF Core is configured for exactly
one of them. Every deployment carries five unused drivers, their transitive dependencies
and their CVE surface.

One of those transitive dependencies leaks into behaviour: `MySql.Data` pulls in
BouncyCastle, whose `InvalidKeyException` surfaces from the HTTP client's control flow when
an unrecognised verb is used.

The deployment target runs PostgreSQL.

## Decision

`Data.Sql` references **Npgsql only**, for both EF Core and Dapper.

`Domain.Enums.DataBaseType` keeps every member, and the provider `switch` in
`ConnectionStringBuilderExtension` keeps its arms. Only the driver *packages* are absent,
so configuration stays portable and adding a provider is additive.

## Adding another provider

1. Add a `PackageVersion` to `Directory.Packages.props`, for example
   `Microsoft.Data.SqlClient`.
2. Add the matching `PackageReference` to `Data.Sql.csproj`.
3. Extend the `switch` in `ConnectionStringBuilderExtension.BuildConnectionString` and the
   `UseNpgsql` call in `AddDbContextSetup`.
4. Set `Modules:Sql:Provider` and the connection's `Type`.

`AppDbContext.ConfigureConventions` already selects the JSON column type per provider
(`jsonb`, `json`, `nvarchar(max)`, `TEXT`), so entity configuration needs no change.

## Consequences

- Smaller image, smaller dependency graph, smaller CVE surface.
- A project needing a different engine does three small edits rather than deleting five
  unused references.
