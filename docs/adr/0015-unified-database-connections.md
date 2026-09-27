# 15. Relational and document databases share one connection list

Status: Accepted

Amended by ADR-0017: `Modules:Sql:DatabaseId` / `Modules:NoSql:DatabaseId` are gone - every entry is registered under its `Id` and features name the entry they use. `DatabaseConnectionModel` is `DatabaseSettings`.

Amends ADR-0008 and ADR-0009.

## Context

SQL and NoSQL connections were configured in two unrelated shapes:

| | SQL | NoSQL |
|---|---|---|
| Connection | `Settings:DataBaseConnections[]`, keyed by `DataBaseID` | `Settings:NoSql`, a single object |
| Engine | `Modules:Sql:Provider` **and** the entry's `Type` | `Modules:NoSql:Provider` |
| Enum | `DataBaseType` | `NoSqlProvider` |

Three problems followed.

**The connection's `Type` was dead.** `BuildSqlRegistration` picked the provider from
`Modules:Sql:Provider` and never read `DataBaseConnectionModel.Type`. An entry declaring
`"Type": "SqlServer"` under a module set to `Postgresql` built a Postgres connection string
from SQL Server settings, and nothing complained.

**NoSQL could not have more than one connection**, and could not be named. Both are
databases; only one of them was addressable.

**Every engine defaulted to port 5432**, because `Port` was a non-nullable `int` with the
Postgres default baked into the model.

## Decision

### One list, one element type

```json
"Settings": {
  "Databases": [
    { "Id": "DEFAULT",   "Type": "Postgresql", "Host": "db", "Database": "app", "Username": "app",
      "Password": "/run/secrets/db_password", "Sql": { "MaxPoolSize": 100 } },
    { "Id": "DOCUMENTS", "Type": "MongoDb",    "ConnectionString": "/run/secrets/mongo", "Database": "app" },
    { "Id": "EVENTS",    "Type": "DynamoDb",   "Cloud": { "Region": "eu-west-1" } },
    { "Id": "ARCHIVE",   "Type": "RavenDb",    "Database": "archive",
      "Cluster": { "Urls": [ "https://raven:8080" ], "CertificatePath": "/run/secrets/raven.pfx" } }
  ]
},
"Modules": {
  "Sql":   { "Enabled": true,  "DatabaseId": "DEFAULT" },
  "NoSql": { "Enabled": false, "DatabaseId": "DOCUMENTS" }
}
```

`DatabaseConnectionModel` carries the fields every engine understands - `Id`, `Type`,
`ConnectionString`, `Host`, `Port`, `Database`, `Username`, `Password`, `UseSsl`,
`TimeoutSeconds` - plus three engine-specific blocks:

| Block | Read by | Fields |
|---|---|---|
| `Sql` | relational providers | `MaxPoolSize` |
| `Cloud` | DynamoDb | `Region`, `ServiceUrl`, `AccessKey`, `SecretKey`, `MaxScanPageSize` |
| `Cluster` | RavenDb | `Urls`, `CertificatePath`, `CertificatePassword` |

The blocks exist because `Microsoft.Extensions.Configuration` binds to one concrete element
type per list; it has no polymorphic binding. A flat union of every engine's fields would
bind, but leaves most properties meaningless for any given entry. Grouping them names the
engine they belong to.

### The connection's `Type` is the only engine selector

`Modules:Sql:Provider` and `Modules:NoSql:Provider` are gone. A module names a
`DatabaseId`; the entry's `Type` resolves the provider. The value cannot disagree with
itself because it exists once.

### One enum, two families

`DataBaseType` and `NoSqlProvider` merge into `DatabaseType`. `DatabaseType.Family()`
classifies each member as `Relational` or `Document` through an exhaustive switch.

The modules stay separate. They expose different ports - `IRepository<,>`, `IUnitOfWork`
and `ISqlDatabaseAccess` against `IDocumentRepository<>` - and different projects. Only the
configuration is unified.

### Failures happen at startup, with the key

| Mistake | Where it fails |
|---|---|
| Duplicate `Id` (case-insensitive) or blank `Id` | `GetAppSettings`, before secret resolution |
| `DatabaseId` names no entry | module registration; message lists the configured ids |
| `DatabaseId` names an entry of the wrong family | module registration; message names the `Type` and both families |
| Engine-specific field missing | provider `Validate`, e.g. `Settings:Databases[Id=EVENTS]:Cloud:Region` |

Each registry also refuses the other family, so a direct call such as
`SqlProviderRegistry.Resolve(DatabaseType.MongoDb)` throws rather than returning nothing.

### Smaller changes carried with it

- `Port` is `int?`. Each relational provider declares `DefaultPort` (5432, 1433, 3306, 1521,
  3050) and uses it when the entry leaves `Port` unset.
- `CommandTimeoutSeconds` and `RequestTimeoutSeconds` become `TimeoutSeconds`: the command
  timeout for relational engines, the request or server-selection timeout for document ones.
  Both are per-operation limits.
- `DataBase*` casing is normalised to `Database*`: `DataBaseConnections` -> `Databases`,
  `DataBaseID` -> `Id`, `GetDataBase` -> `GetDatabase`, `ConnectionKey` -> `DatabaseId`.

## Consequences

- **Every existing configuration key for databases changed.** The template has no deployed
  consumers to stay compatible with, so the rename was done once rather than aliased.
  Environment overrides are now `Settings__Databases__0__Id` and `Modules__Sql__DatabaseId`.
- Adding a second document database is one list entry. Consuming more than one at once
  still needs keyed registrations in `Data.NoSql`, which this change does not add.
- `SecretResolver` needed no change: it already walks lists and nested
  `Domain.Models.*` classes, so `[Secret]` works inside `Cloud` and `Cluster`. Tests pin the
  resolved configuration key, e.g. `Settings:Databases:1:Cloud:SecretKey`.
- The EF model is unaffected. The enum member names match the existing
  `AppDbContext.<Provider>` migration folders, and `has-pending-model-changes` reports no
  drift for any of the six contexts.
- The registry-totality tests now split by family: every `Relational` member maps to exactly
  one SQL provider (Mariadb still refuses), every `Document` member to exactly one NoSQL
  provider, and a test asserts every member has a family.
