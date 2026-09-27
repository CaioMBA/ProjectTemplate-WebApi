# 19. The GraphQL schema is generated from live database introspection

Status: Accepted

## Context

The GraphQL server (HotChocolate, `Source/WebApi/GraphQL`) exposed one hand-written query type,
`ProductQueries`. Every table that should be readable over GraphQL needed a query class, a DTO,
a handler and a filter model of its own - repetitive work that also drifted from the database
as columns were added. The template already knows every database it talks to
(`Settings:Databases`, ADR-0015) and can talk to six relational engines and four document
stores, so the schema can be derived instead of written.

## Decision

### Scan at startup, expose everything that is not excluded

The auto schema *is* the GraphQL server: the hand-written `ProductQueries` type was removed and
the root type is a plain `Query` that only the auto schema extends. `Api:GraphQlServer:Enabled`
is the single switch; `Api:GraphQlServer:AutoSchema` holds only exclusions and limits.

With GraphQL enabled the server reads each database entry's catalog once,
while HotChocolate builds its schema at startup (after `UseDataSqlSetupAsync` applied
migrations). Changing the database schema needs a restart; `AutoSchema` is not in
`SettingsReloadPolicy.LivePaths`, so the restart-required watcher warns if it changes at run time.

Every table, view and collection is exposed unless it is excluded. Exclusion is pattern based
(`*` and `?`, case-insensitive) and needs no per-table configuration:

| Setting | Matched against |
|---|---|
| `ExcludeDatabases` | the entry `Id` |
| `ExcludeSchemas` | `schema`, `Id:schema` |
| `ExcludeTables` | `table`, `schema.table`, `Id:table`, `Id:schema.table` |
| `ExcludeColumns` | `column`, `table.column`, `schema.table.column`, the same prefixed with `Id:`; nested document paths are dotted |

A built-in deny-list always hides system objects: `pg_catalog`, `information_schema`, `sys`,
the `hangfire` schema, `__EFMigrationsHistory`, `outbox_messages`, `sqlite_*`, `hangfire*`,
Mongo `system.*`, Raven `@*`, and Firebird/Oracle system relations. The rules live in
`Domain.Abstractions.SchemaExposurePolicy` (pure, unit tested), in the same spirit as
`HealthCheckPolicy` (ADR-0014).

Authentication is still deferred (ADR-0006), so **an enabled auto schema makes every
non-excluded table readable by anyone who can reach `/graphql`**. It is read-only (no
mutations are generated), bounded (`MaxPageSize`, `MaxFilterDepth`, `MaxInValues`,
`MaxExecutionDepth`, HotChocolate cost analysis) - and **on by default in every environment,
Production included**, with nothing excluded beyond the built-in deny-list. That is a deliberate
choice for this template; exclude sensitive tables and columns before deploying data that must
not be public. Development additionally enables the query tool (`EnableTool`) and exception
details.

### Shape of the schema

One root field per database entry (`Id` in camelCase: `DEFAULT` -> `default`), so tables of
different databases never collide (ids that normalise to the same name are suffixed). Inside
it, per table or collection:

```graphql
default {
  customers(where: DefaultCustomersFilter, order: [DefaultCustomersSort!], skip: Int, take: Int): DefaultCustomersPage!
  customersById(id: Int!): DefaultCustomers   # only for single-column keys
}
type DefaultCustomersPage { items: [DefaultCustomers!]!  totalCount: Long!  aggregate: DefaultCustomersAggregate! }
type DefaultCustomersAggregate { count: Long!  min: ...  max: ...  sum: ...  avg: ... }
```

Filters combine with `and`, `or`, `not` at every level. Operators follow the column type:

| Column type | Operators |
|---|---|
| text | `eq neq in nin gt gte lt lte contains ncontains startsWith nstartsWith endsWith nendsWith like isNull` |
| long text (CLOB, `ntext`, Firebird text BLOB) | the pattern operators and `isNull` only |
| numbers, dates, times | `eq neq in nin gt gte lt lte isNull` |
| boolean, uuid | `eq neq in nin isNull` |
| json, binary, arrays | `isNull` (output only) |

`contains`/`startsWith`/`endsWith` are case-insensitive and treat `%`, `_` (and `[` on SQL
Server) in the value literally; `like` takes a raw `%`/`_` pattern. Negated operators keep rows
where the column is null. `eq: null` is ignored - use `isNull`. `take` defaults to
`DefaultPageSize`; a `take` above `MaxPageSize`, a filter deeper than `MaxFilterDepth` or an
`in` list longer than `MaxInValues` is a GraphQL error, not a silent clamp.

Columns of a type the template cannot map (Postgres `interval`, arrays, enums, PostGIS...) are
hidden and logged at startup.

### Relations from foreign keys

Single-column foreign keys become fields in both directions: `orders.customer` (many-to-one,
named after the column without its `_id` suffix) and `customers.orders` (one-to-many, a paged,
filterable, sortable `Page`). Composite foreign keys are skipped with a log line. Both
directions go through one request-scoped DataLoader (`AutoSchemaRowLoader`): all parents of a
level are loaded with **one** query - `IN (...)` for lookups, `ROW_NUMBER() OVER (PARTITION BY
fk ...)` for per-parent paging of one-to-many pages. `totalCount` and `aggregate` on a nested
page run one query per parent and only when selected.

### Ports and responsibilities

| Piece | Where | Role |
|---|---|---|
| `IDynamicDataSource` | `Domain.Interfaces.Persistence` | describe, query, count, aggregate, lookups; keyed by database `Id` |
| `DataSourceSchema`, `FilterNode`, `DynamicQuery`... | `Domain.Models.DynamicData` | engine-neutral model |
| `SchemaExposurePolicy`, `GraphQlNames`, `FieldValues` | `Domain.Abstractions` | exclusions, naming, operator matrix, value normalisation |
| `SqlCatalogReader` per engine, `SqlDynamicQueryBuilder`, `SqlDynamicDataSource` | `Data.Sql/DynamicData` | catalog SQL and parameterised query SQL |
| `Mongo/Cosmos/Raven/DynamoDynamicDataSource` | `Data.NoSql/DynamicData` | sampled schemas and native queries |
| `AutoSchemaTypeModule`, `AutoSchemaTypeFactory`, `AutoSchemaRowLoader` | `WebApi/GraphQL/AutoSchema` | HotChocolate types and resolvers |

`AutoSchemaTests` keeps the GraphQL layer off every `Data.*` assembly and driver, and
HotChocolate inside WebApi.

### Relational engines

Catalogs are read with engine SQL, not `DbConnection.GetSchema()` (inconsistent across
providers and blind to foreign keys): Postgres `information_schema` + `pg_constraint`, SQL
Server `INFORMATION_SCHEMA` + `sys.*`, MySQL `information_schema` (`DATABASE()`), Oracle
`ALL_TAB_COLUMNS`/`ALL_CONSTRAINTS` for the current schema, Firebird `RDB$` relations, SQLite
`pragma_table_info`/`pragma_foreign_key_list`. SQLite has no real column types, so its dates and
GUIDs appear as text.

Identifiers in generated SQL only ever come from the introspected model (names containing
quote characters are dropped at introspection); every value is a parameter. The builder
rejects any field, operator or sort the model does not allow.

### Document engines

Documents have no fixed columns, so each collection is sampled (`DocumentSampleSize`) and the
field sets merged: a field missing from some documents is nullable, numbers widen, a field
whose type varies becomes a JSON string, nested objects become nested types filtered by dotted
path. There are no relations.

| Engine | Filtering, sort, paging | Aggregates |
|---|---|---|
| MongoDB | native `FilterDefinition`, escaped case-insensitive regexes | `$group` |
| Cosmos DB | parameterised Cosmos SQL (`CONTAINS(..., true)`, `ARRAY_CONTAINS`, `RegexMatch`) | `SELECT VALUE MIN/MAX/SUM/AVG` |
| RavenDB | parameterised RQL, JS projection to read raw documents | not exposed |
| DynamoDB | full scan, filtered in memory; more than `MaxScanItems` items is an error, never a truncated answer | in memory |

Cosmos sorts on more than one field need a composite index on the container.

## Consequences

- A table becomes queryable over GraphQL by existing; nothing is written per table.
- The schema is a projection of the database: renaming a column renames a GraphQL field.
  There is no hand-written GraphQL contract any more; clients that need a stable shape use REST.
- A database that fails introspection is logged and left out while others are exposed
  (`FailOnIntrospectionError` turns any failure into a startup failure). A root
  `autoSchemaStatus { databases { id exposed reason } }` field is always present; `reason` is a
  category (`exposed`, `excluded`, `unreachable`, `nothing to expose`, `no data source`), never
  exception text. If **no** database can be exposed the app still starts, serving only that
  field, and logs one Error naming every database and why. A database that is down at startup
  turns `/health` Unhealthy; the container `HEALTHCHECK` and the Swarm restart policy restart the
  service, and the next start introspects again. Exception text stays in the logs.
- The auto schema reads tables directly, bypassing EF: soft-deleted rows (`is_deleted = true`)
  and audit columns are visible. Filter with `isDeleted: { eq: false }` or hide columns with
  `ExcludeColumns`.
- Integration tests cover Postgres, SQLite and MongoDB end to end; SQL Server, MySQL, Oracle,
  Firebird, Cosmos, Raven and DynamoDB are covered by unit tests of their catalog mapping and
  query text only.
