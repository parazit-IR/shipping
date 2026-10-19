# ADR-002: Module persistence — one runtime MP Core DbContext, schema and migration history per module

Status: **Accepted** (2026-10-15). Implemented on `feat/SLS-125-database-efcore` for US-EN-1 T2 (SLS-125).
It replaces the earlier proposal on this branch (option C below).

## Approval

The project owner explicitly approved this decision on 2026-10-15. The approval covers this architecture:

- one shared PostgreSQL database;
- one independent schema per module;
- one independent runtime DbContext per module;
- one module-specific unit of work per module;
- independent EF Core migrations and migration history per module;
- no shared runtime `AppDbContext`;
- MP Core components reused wherever they are compatible.

The owner approved it knowing that it departs from MP Core's default of one unit of work per host (see
"Deviation from MP Core" below).

## Context

SLS-125 requires "one DbContext/schema per module". Each module owns its **runtime** DbContext, its PostgreSQL
schema, its mappings, its migrations and its migration history. One physical database is fine. A migration-only
context per module next to one shared runtime context does not meet the requirement.

What MP Core 0.10.1 provides (from the package's XML docs and its compiled code):

- `MPCoreDbContext(DbContextOptions, TimeProvider[, IAggregateEventSink])` is abstract and takes non-generic
  options, so each module can subclass it. Its `SaveChangesAsync` stamps `ITrackableEntity` timestamps from the
  `TimeProvider`, drains aggregate events into the sink, and only then calls EF's `SaveChangesAsync`.
- `PostgreSqlDbContextOptions.Apply(builder, connectionString, configureNpgsql)` is MP Core's Npgsql setup,
  "exposed so a host can register the context through another mechanism".
- `AddMPCorePostgreSql<T>` and `AddMPCoreUnitOfWork<T>` bind `IUnitOfWork` with `TryAddScoped`. With two
  contexts, the first one registered wins and the second is silently not the unit of work. A test pins this:
  `UnitOfWorkCompositionTests.MP_Core_binds_IUnitOfWork_to_the_first_context_registered_and_ignores_a_second`.
- `AddMPCoreAggregateEvents` registers the discarding sink with `TryAddSingleton`. `AddMPCoreFoundation`
  registers `TimeProvider`.
- MP Core's guidance (`mpcore-implement-ddd-module` step 6, `docs/capabilities.md` "A second unit-of-work
  context in one host: Not available") assumes one shared `AppDbContext`. The reason given is Wolverine:
  `UseMPCoreWolverine<TContext>` names one transaction owner per host. The manifest records `messaging: none`,
  and Wolverine is not installed, so that rule does not apply today.

## Options considered

| Option | Meets SLS-125 | Decision |
|---|---|---|
| A. One runtime `MPCoreDbContext` per module, each with its own unit-of-work port, and no global `IUnitOfWork` | Yes | **Chosen** |
| B. MP Core default: one `AppDbContext`, a schema per module, one migration history | No | Rejected |
| C. A migration-only context per module, plus one shared runtime `AppDbContext` applying every module's mappings | No: the runtime context is shared | Rejected (it was implemented earlier on this branch and has been removed) |
| D. Per-module contexts sharing one `DbConnection`/transaction, wired by hand | Yes | Rejected: custom transaction infrastructure that MP Core does not know about |

## Decision

1. **Database:** one PostgreSQL database, through `MPCore.Persistence.EntityFrameworkCore.PostgreSql` 0.10.1
   (EF Core 10.0.11, Npgsql 10.0.3).
2. **Runtime context per module:** `<Module>DbContext : MPCoreDbContext, I<Module>UnitOfWork`, `internal` to the
   module's Infrastructure project. It applies only its own module's mappings, sets
   `HasDefaultSchema("<module>")`, and sets its history table to `<module>.__EFMigrationsHistory` in
   `OnConfiguring`, so whoever builds the options gets that history location.
3. **Unit of work per module:** `I<Module>UnitOfWork : IUnitOfWork` in the module's Application project
   (`Ports/`). The module's handlers depend on it. The host binds **no** unkeyed `IUnitOfWork`.
4. **Registration:** `Add<Module>Module(connectionString)` (Api) calls `Add<Module>Infrastructure(connectionString)`,
   which runs `AddDbContext<<Module>DbContext>(o => PostgreSqlDbContextOptions.Apply(o, connectionString))` and
   resolves `I<Module>UnitOfWork` to that same scoped instance. No module calls `AddMPCorePostgreSql` or
   `AddMPCoreUnitOfWork`. The host calls `AddMPCoreFoundation` (the `TimeProvider`) and `AddMPCoreAggregateEvents`
   once. It owns no `DbContext`.
5. **Migrations per module:** in `Persistence/Migrations` of the module's Infrastructure project, with a
   design-time factory in the same project. As of SLS-125 nothing migrates at startup. Each module's migrations
   are a deployment step: `dotnet ef database update` locally, or one migration bundle per module. SLS-15's
   requirement for automatic migrations in Development is not implemented here and remains open; see
   "Constraints on future work".
6. **Boundaries:** a module's tables live only in its schema. No foreign key crosses a schema. Code outside the
   module's Infrastructure does not depend on its persistence namespace. Tests enforce all three.

## Consequences

- Each module can evolve its schema and migrations on its own. Five developers can work in separate modules
  without sharing a model snapshot.
- **No atomic cross-module write.** Each unit of work commits its own context in its own transaction. A
  use case that changes two modules needs a message or a compensating step, not one transaction. MP Core's
  module rules already prefer a message.
- A handler resolves `I<Module>UnitOfWork`, not `IUnitOfWork`. MP Core features that look for `IUnitOfWork`
  itself would need care if they are added: the idempotency store says "its handler must declare
  IUnitOfWork", and Wolverine's transaction middleware is another example.
- **Future constraint, for the owner to decide when messaging is added:** `UseMPCoreWolverine<TContext>` and
  the transactional outbox name one context per host. Adding Wolverine means choosing one of: an outbox owner
  per module; giving up the EF outbox for modules other than its owner; or separate hosts. It is not a blocker
  today, because `messaging: none` and Wolverine is not referenced.
- This differs from upstream MP Core guidance (one `AppDbContext`). That guidance is kept as it is and is not
  edited here.

## Rationale

- **SLS-125 requires it.** It asks for one DbContext and schema per module. Only options A and D give each
  module its own runtime context. Option D needs transaction plumbing that MP Core does not know about.
- **Module autonomy.** Each module owns its model, snapshot and migration history, so modules can change
  their schema separately and developers do not conflict on one shared snapshot.
- **The boundary is enforced, not just agreed.** A context that applies only its own module's mappings
  cannot reach another module's tables through EF. The architecture and integration tests check the
  schema, history and foreign-key rules.
- **MP Core is reused, not forked.** Each context derives from `MPCoreDbContext`, so timestamp stamping and
  aggregate-event draining still apply. Npgsql is configured through `PostgreSqlDbContextOptions.Apply`. The
  unit-of-work ports extend MP Core's `IUnitOfWork`. Only the registration helpers that assume one context
  per host (`AddMPCorePostgreSql<T>`, `AddMPCoreUnitOfWork<T>`) are not used.
- **One database is simpler to operate.** It means one connection string, one backup and one server, and the
  schemas still keep the modules apart.

## Deviation from MP Core

MP Core 0.10.1 assumes **one unit of work per host**: one `AppDbContext`, bound to the unkeyed
`IUnitOfWork`, and named once as the Wolverine transaction owner. Its catalogue lists "a second unit-of-work
context in one host" as *Not available*. This repository intentionally does otherwise:

| MP Core default | This repository |
|---|---|
| One `AppDbContext` per host | One `MPCoreDbContext` subclass per module |
| Unkeyed `IUnitOfWork` bound by `AddMPCoreUnitOfWork<T>` | No unkeyed `IUnitOfWork`. Each module has `I<Module>UnitOfWork : IUnitOfWork` |
| One migration history | One `<module>.__EFMigrationsHistory` per module |
| Registered with `AddMPCorePostgreSql<T>` | `AddDbContext<T>` with `PostgreSqlDbContextOptions.Apply` |

The deviation is limited to this repository. MP Core's packages and upstream guidance are not changed. The test
`UnitOfWorkCompositionTests.MP_Core_binds_IUnitOfWork_to_the_first_context_registered_and_ignores_a_second`
pins the MP Core behaviour that makes the default registration unsafe with several contexts. If an MP Core
upgrade changes that behaviour, this ADR must be revisited.

## Constraints on future work

These are binding until a later ADR replaces this one:

- **Keep module transaction boundaries independent.** Each module's unit of work commits only its own context.
- **Do not add a shared unit of work without saying so.** No unkeyed `IUnitOfWork`, no shared runtime
  `AppDbContext`, no context that applies more than one module's mappings, and no `DbConnection` or
  transaction shared between module contexts, unless a new ADR approves it.
- **Never assume a transaction is atomic across modules.** A use case that changes two modules is designed
  with a message or a compensating step.
- **Wolverine, the outbox and idempotency must support per-module contexts explicitly.** When they are
  introduced, they must work with this architecture and not fall back to a single host-wide transaction
  owner. The design of that integration (see the "Future constraint" consequence above) needs its own
  decision.
- **Automatic Development migrations (SLS-15) stay per module.** When a later SLS-15 subtask implements them,
  they run in Development only, apply each module's migrations through its own context and history, and never
  use `EnsureCreated`. If they run inside the host, the guard test
  `Nothing_in_the_host_or_a_module_creates_or_migrates_the_database` is changed deliberately, with an ADR,
  not deleted. See [persistence](../persistence.md#development-automatic-migrations-sls-15-requirement-not-yet-implemented).
- **Any change to this architecture requires a new ADR.**
