# Persistence

How Shipping stores data (SLS-125). The decision is recorded in
[ADR-002](architecture/ADR-002-module-persistence.md).

## Why PostgreSQL

MP Core 0.10.1 supports one database provider: PostgreSQL through EF Core and Npgsql
(`MPCore.Persistence.EntityFrameworkCore.PostgreSql`). Its outbox, idempotency, inbox, audit and translation
packages are PostgreSQL packages too. PostgreSQL schemas also give each module a real boundary inside one
database.

## The shape

```text
Shipping database (PostgreSQL)
├── sample                      ← owned by the Sample module (SampleDbContext)
│   ├── __EFMigrationsHistory   ← Sample's migrations only
│   └── (Sample's tables, when it has any)
└── <module>                    ← one schema per module, same layout
```

Per module, in `src/Modules/<M>/`:

| File | Role |
|---|---|
| `…Application/Ports/I<M>UnitOfWork.cs` | The module's unit-of-work port (`: IUnitOfWork`). Handlers depend on it. |
| `…Infrastructure/Persistence/<M>DbContext.cs` | `internal`, `: MPCoreDbContext, I<M>UnitOfWork`. Default schema `<m>`, history `<m>.__EFMigrationsHistory`. |
| `…Infrastructure/Persistence/<M>Persistence.cs` | The schema name and `ApplyModel` (every `IEntityTypeConfiguration` in the assembly). |
| `…Infrastructure/Persistence/<M>DbContextDesignTimeFactory.cs` | Builds the context for `dotnet ef` from `ConnectionStrings__PostgreSql`. |
| `…Infrastructure/Persistence/Migrations/` | The module's migrations and snapshot. |
| `…Infrastructure/ModuleRegistration.cs` | `Add<M>Infrastructure(connectionString)`: `AddDbContext` with MP Core's `PostgreSqlDbContextOptions.Apply`, plus the port resolved to that same scoped context. |

The host (`Program.cs`) requires `ConnectionStrings:PostgreSql`, calls `AddMPCoreAggregateEvents()` once, and
passes the connection string to each `Add<M>Module`. It owns no `DbContext` and binds no global `IUnitOfWork`.
A save through `I<M>UnitOfWork` runs MP Core's `MPCoreDbContext.SaveChangesAsync`: timestamps from the
`TimeProvider`, then aggregate events drained to the sink, then the write. A save commits only that module's
schema. A change to two modules is never one transaction.

EF Core and Npgsql are referenced only by Infrastructure projects and the host; Domain and Application never
see them.

## Local configuration

No connection string is committed. The host fails at startup without one.

```bash
dotnet user-secrets set ConnectionStrings:PostgreSql "Host=localhost;Port=5432;Database=shipping;Username=<user>;Password=<password>" --project src/Shipping.Platform.Api
export ConnectionStrings__PostgreSql="Host=localhost;Port=5432;Database=shipping;Username=<user>;Password=<password>"   # for dotnet ef and bundles
```

## Migration commands

Run `dotnet tool restore` once (dotnet-ef 10.0.11). Each command names the module's context:

```bash
P=src/Modules/Sample/Shipping.Modules.Sample.Infrastructure
H=src/Shipping.Platform.Api
dotnet ef migrations add <Name> --project $P --startup-project $H --context SampleDbContext --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project $P --startup-project $H --context SampleDbContext
dotnet ef database update --project $P --startup-project $H --context SampleDbContext
dotnet ef migrations script --idempotent --project $P --startup-project $H --context SampleDbContext
```

Apply each module's migrations in turn. Order does not matter. The API does not migrate at startup in any
environment and never calls `EnsureCreated`. The architecture test
`Nothing_in_the_host_or_a_module_creates_or_migrates_the_database` checks this for the host and module
assemblies.

## Development: automatic migrations (SLS-15)

The parent story SLS-15 (US-EN-1) requires migrations to run automatically in Development. Since SLS-126 the
local environment does this outside the host. The `migrate` service in `compose.yaml` (the `migrator` target
of the `Dockerfile`) runs one migration bundle per module, built with `dotnet ef migrations bundle --context
<M>DbContext`, before the API starts. The API starts only if every bundle succeeds. The host and the guard test
are unchanged. See [local environment](local-environment.md#startup-order).

MP Core 0.10.1 does not provide this ("Automatic migration on startup: Not available" in
[capabilities](capabilities.md#migrations)), so it is repository code. It meets the constraints recorded here
by SLS-125:

- run only in Development and never in production, where migrations remain a deployment step (below);
- apply every module's migrations through that module's own context, so each history stays in
  `<module>.__EFMigrationsHistory` (ADR-002);
- use migrations, never `EnsureCreated`, which bypasses the migration history;
- either run outside the host and module assemblies (for example a step that runs each module's bundle or
  `dotnet ef database update` when the local environment starts), or, if it runs inside the host, change the
  guard test above deliberately and record that in an ADR. The test is not deleted or weakened to make room.

## Production

Build one bundle per module
(`dotnet ef migrations bundle … --context SampleDbContext --self-contained -r linux-x64 -o sample-efbundle`). Run
each bundle as a deployment step before the new API version starts, with `ConnectionStrings__PostgreSql` set
from the secret store. A bundle applies only the migrations missing from its own module's history, so a re-run
is a no-op. Where the platform allows it, use two roles: a migration role that owns the module schemas, and an
application role with DML grants per schema. Review the `--idempotent` script before each release.

## Adding persistence to a new module

`dotnet new shipping-module --name Booking` generates `IBookingUnitOfWork`, `BookingDbContext` (schema
`booking`), its factory, its registration, and `AddBookingModule(connectionString)`. Then:

1. In `Program.cs`, call `builder.Services.AddBookingModule(databaseConnection);`.
2. Add the baseline migration with
   `dotnet ef migrations add Baseline --project src/Modules/Booking/Shipping.Modules.Booking.Infrastructure --startup-project src/Shipping.Platform.Api --context BookingDbContext --output-dir Persistence/Migrations`.
3. Run `dotnet test`. The persistence rules find the module by convention.

Map each entity with an `IEntityTypeConfiguration` in the module's Infrastructure project. Refer to another
module's rows by identifier only, never with a foreign key.
