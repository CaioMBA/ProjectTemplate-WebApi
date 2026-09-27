# 8. Multi-provider SQL with one migration set per engine

Status: Accepted

Supersedes ADR-0005. Amended by ADR-0015: the engine is now chosen by the connection
entry's `Type` in `Settings:Databases`, not by `Modules:Sql:Provider`, and `DataBaseType` is
now `DatabaseType`.

## Context

ADR-0005 shipped Npgsql only. The template is now expected to be plug-and-play across SQL
engines: change one configuration value, get a different database.

Two facts constrain any solution.

EF Core migrations are provider-specific. A migration and its model snapshot bake in
provider types - `jsonb`, `timestamptz`, `nvarchar(max)`, `datetime2`. One migrations
assembly cannot serve two engines.

`DateTimeOffset` is not portable. MySQL, SQLite and Firebird have no native equivalent,
and EF Core's providers either refuse it or silently lose the offset.

## Decision

`Data.Sql/Providers/` holds one file per engine. Each file contains that engine's
`ISqlDialect` and its `ISqlDatabaseProvider`, so removing an engine means deleting one
file, one package pair and one line in `SqlProviderRegistry`.

Six engines work: Postgresql, SqlServer, Mysql, Oracle, Firebird, Sqlite.

MariaDB is a provider that throws on registration. Pomelo 9.0.0 pins EF Core Relational to
`[9.0.0, 9.0.999]` and has no EF 10 build. The provider explains this and directs the
reader to the Mysql provider, which MariaDB is wire-compatible with. A silent absence
would have been worse than a loud refusal.

`AppDbContext` is abstract and declares `protected abstract ISqlDialect Dialect { get; }`.
Six derived contexts exist so each engine gets its own migrations assembly namespace.
`SqlDbContextFactory<TContext>` adapts `IDbContextFactory<TDerived>` to
`IDbContextFactory<AppDbContext>`, because `AddDbContextFactory` cannot target an abstract
type.

Audit and event timestamps are `DateTime` in UTC.

## Application code never writes dialect-specific SQL

`ISqlSyntax` is a Domain port implemented by every dialect. Application code injects it for
`BooleanLiteral`, `CaseInsensitiveLike`, `ApplyPagination`, `Parameter` and
`QuoteIdentifier`.

This was not theoretical. The product list query used `ILIKE`, the literal `FALSE` and
`LIMIT/OFFSET`. All three are PostgreSQL-only and every one of them failed the moment the
provider was switched to SQLite.

## Consequences

- Adding an engine is a new file, a package pair, a registry line and a migration set.
- Six migration sets must be regenerated when the model changes. CI runs
  `dotnet ef migrations has-pending-model-changes` per context to catch a forgotten one.
- `dotnet ef migrations add` must always be given an explicit `-o` path. Without it EF
  writes to `Migrations/` at the project root rather than the per-provider folder.
