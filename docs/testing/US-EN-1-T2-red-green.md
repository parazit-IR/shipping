# US-EN-1 T2 (SLS-125) test evidence

All runs took place on 2026-10-15, on Windows Server 2025 with .NET SDK 10.0.302 and Docker 27.5.1 running
`postgres:17.6-alpine` through Testcontainers. The branch was `feat/SLS-125-database-efcore`. This evidence
covers the design in ADR-002: a runtime MP Core context, unit of work, schema and history per module. It
replaces the earlier evidence for the shared-`AppDbContext` design (option C), which has been removed.

## RED

The tests were rewritten first and run against the option C code. The commands were `dotnet build Shipping.slnx -c Release --no-restore`, then
`dotnet test <project> -c Release --no-build --logger "console;verbosity=normal"`.

- **Build:** `Shipping.UnitTests` did not compile. It reported 2 errors, `CS0234: The type or namespace name 'Ports' does not exist in the
  namespace 'Shipping.Modules.Sample.Application'`, because `ISampleUnitOfWork` did not exist yet.
- **`Shipping.ArchitectureTests`:** 21 total, 11 passed, 10 failed.
  - `Every_module_owns_exactly_one_runtime_context…`: `SampleDbContext is not an MPCoreDbContext.`
  - `Each_module_declares_its_own_unit_of_work_port…`: `Module Sample must declare exactly one ISampleUnitOfWork … found 0.`
  - `A_module_context_is_not_visible_outside_its_module`: `SampleDbContext is public; only its module may write through it.`
  - `The_host_declares_no_DbContext_of_its_own`: `A shared runtime context in the host: Shipping.Platform.Api.Persistence.AppDbContext`
  - `Each_module_registers_its_context_with_MP_Cores_provider_setup_and_never_binds_the_global_unit_of_work`:
    `The global IUnitOfWork is bound to one context: Program+<<Main>$>d__0.MoveNext calls AddMPCorePostgreSql`
  - `Each_module_context_uses_its_own_schema_by_default…`, `Each_module_has_a_baseline_migration…`,
    `Each_module_context_maps_only_its_own_entities…`, `No_foreign_key_crosses_a_schema`:
    `MissingMethodException: Constructor on type '…SampleDbContext' not found.` The old context had no MP Core
    constructor.
  - `Template_can_generate_and_compile_a_disposable_host`: `Assert.Contains() Failure: Sub-string not found`. The
    template had no internal MP Core context.
- **`Shipping.IntegrationTests`:** 9 total, 3 passed, 6 failed.
  - All 5 `ModuleMigrationTests` and
    `ModuleRuntimeTests.The_hosts_module_contexts_connect_to_the_one_database…` failed with
    `MissingMethodException: Constructor on type '…SampleDbContext' not found.`
- **Passed in RED, as expected:**
  - The three probe-only runtime tests. The probe modules are test code that already follow the target pattern.
    In RED they ran on MP Core plus the sink that the old host registered.
  - The startup-migration and Domain/Application provider guards.
  - The seeded schema and foreign-key rule tests.
  - The skeleton dependency tests.

## GREEN

- `dotnet tool restore`: "Restore was successful."
- `dotnet ef migrations add Baseline … --context SampleDbContext --output-dir Persistence/Migrations` generated
  `20261015081705_Baseline`. Its `Up` is empty because Sample has no entities. The snapshot has
  `HasDefaultSchema("sample")`.
- `dotnet restore Shipping.slnx`: success.
- `dotnet build Shipping.slnx -c Release --no-restore`: **0 Warning(s), 0 Error(s)**.
- `dotnet test Shipping.slnx -c Release --no-build`, exit code 0:
  - `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9 - Shipping.UnitTests.dll`
  - `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9 - Shipping.IntegrationTests.dll`
  - `Passed! - Failed: 0, Passed: 21, Skipped: 0, Total: 21 - Shipping.ArchitectureTests.dll`
- `git diff --check`: clean.

## Guards seen failing on seeded faults (all reverted)

| Fault | Failed tests |
|---|---|
| `AddSampleInfrastructure` uses `AddMPCorePostgreSql<SampleDbContext>`, and `SampleDbContext` no longer sets its history schema | Arch: `…never_binds_the_global_unit_of_work` (`AddSampleInfrastructure calls AddMPCorePostgreSql`) and `…records_its_migrations_there`. Unit: `Sample_module_registers_its_own_context…`, `The_host_binds_no_global_IUnitOfWork`, `A_second_module_neither_takes_over…`. Integration: `The_hosts_module_contexts…` (`IUnitOfWork` not null), `A_fresh_database…`, `Each_modules_migrations_apply_on_their_own…`, `One_modules_migration_history…` (`42P01: relation "sample.__EFMigrationsHistory" does not exist`) |
| `SampleDbContext` is made `public`, and `HasDefaultSchema` is removed | `A_module_context_is_not_visible_outside_its_module`, `Each_module_context_uses_its_own_schema_by_default…` |

## Production migration path

- `dotnet ef migrations has-pending-model-changes --context SampleDbContext`: "No changes have been made to the
  model since the last migration."
- `migrations script --idempotent` creates schema `sample` and `sample."__EFMigrationsHistory"`, and records
  `20261015081705_Baseline` there.
- `migrations bundle` was run twice against a fresh `postgres:17.6-alpine`, with the connection in
  `ConnectionStrings__PostgreSql` only:
  - First run: `Applying migration '20261015081705_Baseline'. Done.`
  - Second run: `No migrations were applied. The database is already up to date.`
- One bundle attempt failed with `28P01: password authentication failed`. Port 55432 was taken by an
  unrelated local process. That attempt is not product evidence.

## Not verified

- CI has not run these uncommitted changes.
- Wolverine, the outbox and idempotency are not installed (`messaging: none`), so their interaction with
  per-module units of work is analysed in ADR-002 but not executed.
- No test writes to Sample's own tables, because Sample has none. Runtime writes are shown through two test-only
  probe modules, registered in the real host's container in the same way as Sample.
