# Team walkthrough: the US-EN-1 foundation

A facilitator's guide for a 90-minute, hands-on session with the five developers of the Shipping Platform and the
Stream Lead (US-EN-1 T6). It covers the foundation delivered by T1–T5: the solution and module template, PostgreSQL
persistence, the Docker Compose environment, `GET /health` and the developer quickstart.

| | |
|---|---|
| Commands, expected output, recovery | [Demo runbook](demo-runbook.md) |
| Attendance, readiness, acceptance | [Team readiness checklist](team-readiness-checklist.md) |
| Technical rehearsal of every demo | [US-EN-1 T6 evidence](../testing/US-EN-1-T6-red-green.md) |
| Status | Prepared and technically rehearsed. The session with the team has not taken place yet. |

**What this session is not.** It presents no business feature, because none exists. There are no business endpoints,
no entities, no authentication, no OpenAPI document and no message broker in this repository yet. It is not a
microservices platform either: every module runs inside one ASP.NET Core process.

## Learning objectives

By the end of the session each developer can:

1. Say what `.mpcore/template-manifest.json` decides (modular monolith, REST, no messaging, MP Core 0.10.1) and why
   those choices are not revisited in a feature branch.
2. Name the four projects of a module, what belongs in each, and which references are allowed between them
   ([ADR-001](../architecture/ADR-001-four-project-modules.md)).
3. Explain how a module owns its persistence: its own context, unit of work, schema and migration history, and what
   that means for transactions across modules ([ADR-002](../architecture/ADR-002-module-persistence.md)).
4. Tell where Shipping follows MP Core and where it deliberately does not, and which documents describe MP Core's
   default rather than this repository.
5. Start the local environment, confirm the migrations ran and read the answer of `GET /health`.
6. Run the unit, architecture and integration tests and say which of them need Docker.
7. Create, register and migrate a new module, and say which shared files that touches.
8. Recognise the `503` of the API and the `404` of Traefik when the database is gone.
9. Describe the branch, test-first, review and CI workflow the team uses, and which parts of it are not enforced by
   repository settings.

## Prerequisites

Each participant, before the session (about 20 minutes, most of it downloads):

| What | Check |
|---|---|
| Git and a clone of `main` | `git -C shipping log -1 --oneline` |
| Docker Engine or Docker Desktop with Compose v2 | `docker info` and `docker compose version` both answer |
| .NET 10 SDK | `dotnet --list-sdks` lists a `10.0` SDK |
| curl 7.71 or later | `curl --version` (`curl.exe --version` in PowerShell) |
| Port 8000 free on `127.0.0.1` | nothing else is listening on it |
| A first start, to download the images | the README [Quickstart](../../README.md#quickstart) once, ending with `docker compose down` |
| A first build, to fill the NuGet cache | `dotnet build Shipping.slnx` once |
| Ten minutes of reading | the README [Overview](../../README.md#overview) and [Architecture](../../README.md#architecture) |

The full list of prerequisites, including the Linux `docker` group and the Windows long-path problem, is in the
README [Prerequisites](../../README.md#prerequisites) and [Troubleshooting](../../README.md#troubleshooting).

## Facilitator setup

The day before:

1. Use a checkout of `main` with no uncommitted work, and note its commit. Every checkout on one machine shares the
   Compose project `shipping` and its database volume (the project name is fixed in `compose.yaml`). If you use a new
   clone and the volume already exists, copy the `.env` of the checkout that created it instead of generating a
   password (runbook, Demo 1).
2. Run Demos 1, 2, 3 and 5 of the [runbook](demo-runbook.md) once from start to end, then `docker compose down`.
   This warms the images and the NuGet cache: a cold first start took 111 s in T5, a warm one about 30 s.
3. Run Demo 4 once, including its clean-up, so you know how long the build takes on your machine.
4. Check whether your Docker CLI returns output from `docker compose exec`: run
   `docker compose exec postgres psql -U shipping -d shipping -c "select 1"` while the stack is up. On the rehearsal VM
   (Windows Server, Docker Engine in WSL 2) the Windows CLI returned nothing. If yours does the same, run Demo 2's
   `psql` and Demo 5's direct check from a Linux or WSL shell.
5. Share the [readiness checklist](team-readiness-checklist.md) with the participants and the Stream Lead.

Thirty minutes before: `docker compose up --build -d --wait --wait-timeout 300` in the session clone, so Demo 1
starts from a known state. Open in the editor: `src/Shipping.Platform.Api/Program.cs`, the Sample module's four
projects, `compose.yaml`, ADR-001 and ADR-002.

## Agenda

| Time | Part | Runbook |
|---|---|---|
| 0–10 min | 1. Platform overview and architectural decisions | — |
| 10–25 min | 2. Solution and module layering | — (code tour) |
| 25–40 min | 3. Docker Compose, database and migrations | Demo 1, Demo 2 |
| 40–50 min | 4. Health endpoint and tests | Demo 3, Demo 5 |
| 50–70 min | 5. Live module creation | Demo 4 |
| 70–80 min | 6. Parallel development workflow | Demo 6 |
| 80–90 min | 7. Questions, verification, ownership and next steps | checklist |

Demo 5 waits about 2.5 minutes for Docker's health checks. Start it at the beginning of Part 4 and present the tests
while it runs.

## Part 1 — Platform overview and architectural decisions (0–10 min)

Show `.mpcore/template-manifest.json`. It records `shape: modular-monolith`, `transport: rest`, `messaging: none` and
MP Core `0.10.1`. These were decided when the repository was generated. Changing one is a scope change for the owner,
never part of a feature ([AGENTS.md](../../AGENTS.md)).

Talking points:

- **One process.** `src/Shipping.Platform.Api` is the only executable. Every module is a set of class libraries
  loaded into it, and all modules are deployed together.
- **MP Core is a dependency.** It comes from nuget.org as `MPCore.*` packages pinned to 0.10.1. Its source is never
  copied here. Upstream already publishes 1.0.0 and 1.0.1 (GitHub releases, checked for the
  [T6 evidence](../testing/US-EN-1-T6-red-green.md#github-and-mp-core-checks)). This repository stays on 0.10.1: an
  upgrade is a separate decision for the owner, and ADR-002 must be revisited if MP Core's unit-of-work registration
  changes.
- **Non-negotiable rules** ([AGENTS.md](../../AGENTS.md)): the host is a bearer-only resource server, and identity comes
  only from the validated token through `ICurrentActorAccessor`, never from a body, query, route value or header.
  Login, signup, OTP and password handling are never implemented here. No credential, realm URL or connection string
  is committed. No transport or broker is added that the manifest does not record. Authorization and assertions are
  never weakened to make something pass.
- **What exists:** the skeleton and module template (T1), module-owned PostgreSQL persistence (T2), Docker Compose with
  automatic Development migrations (T3), `GET /health` (T4), the README quickstart (T5).
- **What does not exist yet:** business endpoints, entities, authentication configuration (the bearer model above is
  fixed; it is not wired yet), OpenAPI, messaging, CQRS handlers, production deployment. `Sample` is a technical module with no behaviour.
- **Two accepted decisions** shape everything else: [ADR-001](../architecture/ADR-001-four-project-modules.md) (four
  projects per module) and [ADR-002](../architecture/ADR-002-module-persistence.md) (one context, schema and
  migration history per module).

Ask: *Where do you check whether this repository has a message broker, and what do you do if a story seems to need
one?* (The manifest says `messaging: none`; adding one is the owner's decision, raised before implementation.)

## Part 2 — Solution and module layering (10–25 min)

### The host

Walk through `src/Shipping.Platform.Api/Program.cs` from top to bottom:

1. `AddMPCoreFoundation(...)`: MP Core's host foundation from `MPCore.Hosting` and `MPCore.Observability`, including
   the `TimeProvider` every module context uses. Console logging goes through MP Core's redacting pipeline; the
   default ASP.NET Core providers are removed on purpose.
2. `ConnectionStrings:PostgreSql` is required. The host stops at startup without it, and no connection string is
   committed (user secrets, or the environment in Compose).
3. `AddMPCoreAggregateEvents()`: registered once for every module context.
4. `AddSampleModule(databaseConnection)`: the module's single service entry point.
5. `AddHealthChecks()`, then `MapHealthChecks("/health")`: `GET` only, anonymous on purpose so probes need no token.
   The host registers no check of its own; each module adds one for its own context.
6. `MapSampleModule()`: the module's single endpoint entry point. For Sample it maps nothing.

The host is the composition root: modules are registered by explicit lines, never by assembly scanning. The
architecture test `Compiled_host_calls_sample_service_and_endpoint_registration` reads the compiled host to check that
both calls are there.

### The four projects of a module

```text
src/Modules/Sample/
  Shipping.Modules.Sample.Domain/          entities, value objects, business rules, domain events
  Shipping.Modules.Sample.Application/     use cases (commands, queries) and the ports they need
  Shipping.Modules.Sample.Infrastructure/  DbContext, mappings, migrations, adapters, the module's health check
  Shipping.Modules.Sample.Api/             Add<M>Module and Map<M>Module: the module's registration surface
```

| Project | Belongs here | In Sample today | May reference |
|---|---|---|---|
| Domain | Aggregates, entities, value objects, business rules, domain events | `AssemblyReference` only | `MPCore.Domain`, nothing else |
| Application | Commands and queries with their handlers; ports (interfaces) for what the use cases need | `ISampleUnitOfWork` | Its Domain; MP Core abstraction packages only (no EF Core, no ASP.NET) |
| Infrastructure | Adapters for the ports: `SampleDbContext`, mappings, migrations, design-time factory, `SampleDatabaseHealthCheck`, `AddSampleInfrastructure` | All of these, with no entities | Its Application and Domain (Sample references Application, and Domain through it); `MPCore.Persistence.EntityFrameworkCore.PostgreSql`, `Microsoft.Extensions.Diagnostics.HealthChecks` |
| Api | `AddSampleModule`, `MapSampleModule`; later the module's endpoints | Both, mapping nothing | Its Application, Infrastructure and Domain (Sample references the first two); ASP.NET Core |
| Host | Composition only | `Program.cs` | Each module's Api project |

Dependencies point inward: Api → Infrastructure → Application → Domain. In Hexagonal (ports and adapters) terms,
Application declares a port (`ISampleUnitOfWork`), Infrastructure provides the adapter (`SampleDbContext` implements
it), and the Api endpoints will be the driving side. No module references another module's projects at all. How
modules will talk to each other (a Contracts project, messages) is not decided yet.

These rules are tests, not conventions:

| Test | Checks |
|---|---|
| `SkeletonTests.All_module_project_dependencies_obey_boundaries` | Every module project's evaluated references: allowed layer edges, no cross-module reference, the packages Domain and Application may use, no ASP.NET in Domain or Application, no cycles. |
| `SkeletonTests.Evaluated_references_reject_adversarial_edges` | The rule above fails when it should: it adds forbidden references to a copy and expects errors. |
| `SkeletonTests.Compiled_sample_layers_obey_dependency_direction` | The compiled assemblies obey the same direction. |
| `PersistenceRulesTests.Domain_and_Application_never_see_Entity_Framework_or_Npgsql` | No EF Core or Npgsql type reaches the inner layers. |
| `PersistenceRulesTests.A_module_context_is_not_visible_outside_its_module` | Only the module's Infrastructure sees its context. |

### MP Core: what is reused and where Shipping differs

| Topic | MP Core's default (its skills and generated guides) | Shipping | Decided in |
|---|---|---|---|
| Module layout | One project per module, layers as folders | Four projects per module | ADR-001 |
| Persistence | One shared `AppDbContext`, one unkeyed `IUnitOfWork` per host | One `MPCoreDbContext` subclass, one `I<Module>UnitOfWork`, one schema and history per module; no global `IUnitOfWork` | ADR-002 |
| Context registration | `AddMPCorePostgreSql<T>`, `AddMPCoreUnitOfWork<T>` | `AddDbContext<T>` with MP Core's `PostgreSqlDbContextOptions.Apply` | ADR-002 |
| Handler discovery | Wolverine, `Hosting/HandlerAssemblies.cs` | Not present: `messaging: none`, no handlers yet | manifest |
| Security, OpenAPI | Bearer resource server, OpenAPI document | Not configured yet; `/health` is already marked anonymous for when it is | later stories |

Reused unchanged: `AddMPCoreFoundation`, `MPCoreDbContext` (timestamps from the `TimeProvider`, aggregate events
drained before the write), `PostgreSqlDbContextOptions.Apply`, `IUnitOfWork` from `MPCore.Persistence.Abstractions`,
`AddMPCoreAggregateEvents`.

**Reading order when documents disagree:** the manifest, then the ADRs, then the README,
[persistence](../persistence.md) and [local environment](../local-environment.md). `docs/architecture.md` and most of
[src/Modules/README.md](../../src/Modules/README.md) are MP Core's generated text. They describe MP Core's default
(one-project modules, `AppDbContext`, Wolverine, authentication, OpenAPI), not this repository, and say so in their
first paragraphs. The skills in `.mpcore/skills/` are procedures for assistants; for module creation, AGENTS.md
overrides the one-project layout of `mpcore-implement-ddd-module` with ADR-001.

### Request flow

```text
Client ──► Traefik ──────────────► api container ──────────────► ASP.NET Core routing
          127.0.0.1:8000            Kestrel on :8080                ├─ GET /health          (host)
          Host(localhost) only;     one process, all modules        └─ module endpoints     (Map<M>Module: none today)
          healthy containers only
```

Today the only route is `/health`; any other path gets the API's own `404`. When a business capability is added, its
request will continue inward: a module endpoint in Api calls an Application use case; the Domain aggregate enforces
the rule; the use case saves through the module's unit-of-work port; the Infrastructure adapter (`<Module>DbContext`)
writes to the module's schema. That path is described in [development workflow](../development-workflow.md); none of
it exists in Sample.

**Code tour as a demonstration:**

| Action | Expected observation | Why it matters | If it fails |
|---|---|---|---|
| Open the four project files of Sample | Each references only the projects in the table above | The compiler enforces the direction | A different reference is a defect: the architecture tests must fail on it (Demo 3) |
| Open `Program.cs` | Sample's three lines (`using`, `AddSampleModule`, `MapSampleModule`), `/health`, nothing scanned | One reviewable composition root | — |

Ask: *A Domain class needs to know whether a record exists in the database. Where does that code go?* (A port in
Application, implemented in Infrastructure; Domain stays free of EF Core.)

## Part 3 — Docker Compose, database and migrations (25–40 min)

**Demo 1** ([runbook](demo-runbook.md#demo-1--start-the-local-environment)): start the environment, confirm `migrate`
exited with 0, call `GET /health`.

**Demo 2** ([runbook](demo-runbook.md#demo-2--inspect-module-persistence)): show Sample's context and migrations, the
`sample` schema and `sample."__EFMigrationsHistory"` in `psql`, and a second `migrate` run that applies nothing.

| Demo | Expected observation | Why it matters | If it fails, recover by |
|---|---|---|---|
| 1 | `postgres` and `api` healthy, `migrate` `Exited (0)`, `200 Healthy` with `Server: Kestrel` | One command, same environment for everyone; the API starts only after every module's migrations | The runbook's table; `docker compose down` and `up` again keeps the volume |
| 2 | Schemas `public` and `sample`; `Baseline` in `sample.__EFMigrationsHistory`; second run: `No migrations were applied` | Each module's history is its own; the runner is idempotent | Run `psql` from a Linux or WSL shell if `exec` prints nothing |

What to explain about persistence ownership:

- **One PostgreSQL instance, one database** (`shipping`), **one schema per module.** One connection string, one
  backup, and still a hard line between modules.
- **The module owns its context.** `SampleDbContext` is `internal` to Sample's Infrastructure project; no other
  module and not the host can reach it. It maps only Sample's entities, into `sample`.
- **The module owns its migrations and history.** Its own snapshot in `Persistence/Migrations`, its own
  `sample.__EFMigrationsHistory`. Two developers adding migrations to two modules never edit the same file.
- **The migration runner.** The `migrate` service runs one EF Core bundle per module, each through its own context,
  before the API starts. The `Dockerfile` finds the modules by convention, so a new module needs no Compose change.
  The host itself never migrates; in production each bundle is a deployment step
  ([persistence](../persistence.md#production)).
- **No cross-module reach.** No foreign key crosses a schema; another module's rows are referenced by identifier only.
  Each save commits one module's schema: a change to two modules is never one transaction, and must be designed with
  a message or a compensating step when that need arises.

Ask: *Developer A adds a migration to module X while developer B adds one to module Y. What conflicts?* (Nothing in
the migrations or snapshots. Only `Program.cs`, `Shipping.slnx` and the host's project file if the modules are new.)

## Part 4 — Health endpoint and tests (40–50 min)

Start **Demo 5** ([runbook](demo-runbook.md#demo-5--failure-postgresql-unavailable)) first: stop PostgreSQL, show the
`503`, and come back to it after the tests.

**Demo 3** ([runbook](demo-runbook.md#demo-3--run-the-tests)): restore, build, then the unit, architecture and
integration test projects.

| Demo | Expected observation | Why it matters | If it fails, recover by |
|---|---|---|---|
| 5 | `503 Unhealthy` from Kestrel at once; after about 2.5 minutes Traefik's own `404`; `200` again within about 10 s of `start postgres` | `/health` is readiness; Traefik routes only to healthy containers, so a `404` without `Server: Kestrel` is not a routing bug | `docker compose start postgres`; wait up to 15 s; `docker compose restart api` if the API stays unhealthy |
| 3 | `Build succeeded`, 0 warnings; every project `Passed!`, `Failed: 0`, `Skipped: 0` | The boundaries of Part 2 and 3 are executable; CI runs the same commands | `docker info` in the same shell for Testcontainers; never edit a test to make it pass |

What to explain:

- **`GET /health`** answers `200 Healthy` when every module's context can open a connection, `503 Unhealthy` when one
  cannot within 2 s, and only the status word: no check names, hosts or exceptions. It needs no token. There is no
  separate liveness endpoint. Details: [local environment](../local-environment.md#health-endpoint).
- **Unit tests** need no Docker. **Architecture tests** need no Docker either; they include the template check, which
  generates and compiles a module in a temporary directory, and the documentation checks for the README and this
  guide. **Integration tests** start PostgreSQL 17.6 through Testcontainers, so they need a reachable Docker engine.
- **The Compose smoke test** (`tests/compose/compose-smoke.sh`) runs the whole environment as `shipping-smoke` on port
  18000 and removes only that project. It is verified on Linux and in CI's `compose-smoke` job.
- **Not configured:** mutation testing, coverage thresholds, load tests.

Ask: *Through Traefik you get `404 page not found` from `/health`. Name two possible causes.* (The API is not healthy
in Docker yet or any more; or the request's host name is not `SHIPPING_HOST`.)

## Part 5 — Live module creation (50–70 min)

**Demo 4** ([runbook](demo-runbook.md#demo-4--create-a-module-in-a-disposable-clone)): in a fresh clone in a temporary
directory, generate the module `Walkthrough`, add it to the solution, reference its Api project from the host, add its
two lines and `using` to `Program.cs`, build, add the baseline migration, run the tests. Optionally start the clone's
stack as the Compose project `shipping-walkthrough` on port 8001 to watch `migrate` apply the new bundle. Then delete
the clone.

| Expected observation | Why it matters | If it fails, recover by |
|---|---|---|
| Four projects generated; `WalkthroughDbContext` in schema `walkthrough`; a `Baseline` migration; all tests pass with no test code changed | A new bounded context costs the same few steps for everyone, and the rules cover it by convention | The runbook's table; at worst delete the clone and start again. If the variables were set before the first `dotnet` command, nothing outside the clone changed |

Stress:

- Create a real module only for an approved bounded context, in your own branch, with the README
  [Create a module](../../README.md#create-a-module) steps.
- The only shared files a module touches are `Shipping.slnx`, `src/Shipping.Platform.Api/Shipping.Platform.Api.csproj`
  and `src/Shipping.Platform.Api/Program.cs`.
- Business code inside the module follows [development workflow](../development-workflow.md): requirement and
  acceptance criteria first, then domain rule, use case, persistence, endpoint.

Ask: *What would break if you forgot the `MapWalkthroughModule()` line? And the `dotnet ef migrations add`?* (Nothing
today, because Walkthrough maps no endpoint, but its endpoints would never be reachable later. Without the migration,
`Each_module_has_a_baseline_migration_and_no_pending_model_change` fails.)

## Part 6 — Parallel development workflow (70–80 min)

**Demo 6** ([runbook](demo-runbook.md#demo-6--parallel-team-development)) is a discussion: one branch per task from an
up-to-date `main`, one owner per module, small pull requests, tests first with RED → GREEN → REFACTOR evidence,
independent review, no cross-module internals, migrations per module, CI green before merging.

Be precise about what is enforced. CI runs on every push and pull request. On 2026-10-19 GitHub's public API, asked
without credentials, showed no branch protection and no rulesets for `main`. Treat required reviews and required checks
as team agreements until the repository owner confirms what is configured. Do not present them as enforced.

Ask: *Two of you need to change Sample at the same time. What do you agree before you start?* (Who owns which change;
who adds migrations, in which order; whether one change waits for the other's merge.)

## Part 7 — Questions, verification, ownership and next steps (80–90 min)

1. Open questions from the team. Write down any you cannot answer as a blocker or follow-up in the checklist; do not
   improvise an answer about a decision that has not been made.
2. Each developer fills in their column of the [readiness checklist](team-readiness-checklist.md), running the
   commands on their own machine where they have not already. A failed or blocked item is recorded as such, with a
   follow-up owner.
3. Agree how module ownership is decided. No rule exists in the repository yet; the proposal for the session is that
   the Stream Lead decides. Record the result in the checklist.
4. The Stream Lead records what they saw and their decision in the checklist's acceptance section.
5. Next steps: business capabilities arrive as approved stories. Decisions still open, from the ADRs and the local
   environment notes: how modules call each other; messaging and an outbox that work with per-module contexts; the
   configuration of the bearer resource server (its model is fixed by AGENTS.md); OpenAPI; a read-only Docker socket
   proxy for Traefik; TLS on the local entry point.

## Questions to ask participants

Use these to check understanding during the parts above or at the end. The expected answer is in brackets.

1. Which file decides whether we have a message broker? [`.mpcore/template-manifest.json`: `messaging: none`.]
2. May Application reference EF Core? [No: only Domain and MP Core abstraction packages. A test fails otherwise.]
3. May module X reference module Y's Infrastructure to read its table? [No project reference across modules at all;
   no foreign key across schemas. The mechanism for cross-module calls is still to be decided.]
4. Who applies migrations when you run `docker compose up`? [The `migrate` service, one bundle per module, before the
   API starts. Never the API.]
5. Your new migration is not applied after `docker compose up`. Where do you look? [`docker compose logs migrate`; was
   the image rebuilt (`--build`)?]
6. `/health` says `503`. What does that tell you, and what not? [Some module's context cannot reach PostgreSQL. It does
   not say which: the body has no detail on purpose; the logs do.]
7. Which tests need Docker? [Integration tests (Testcontainers) and the Compose smoke test.]
8. A change touches modules X and Y and must be all-or-nothing. Can one transaction do it? [No: each module commits
   its own schema. It needs a message or a compensating step, and a design decision.]
9. Is `docs/architecture.md` a description of this repository? [Only partly: it is MP Core's generated guide; the
   ADRs and the README win where they differ.]
10. Is a pull request blocked until someone reviews it? [No branch protection or ruleset was visible on 2026-10-19:
    treat it as a team agreement until the repository owner confirms otherwise.]

## Common failure scenarios

| Symptom | Likely cause | Safe recovery |
|---|---|---|
| `Cannot connect to the Docker daemon`, `error during connect` | Docker not running | Start Docker; `docker info` must answer first |
| `required variable POSTGRES_PASSWORD is missing a value` | No `.env`, or an empty password | `cp .env.example .env` and set a local password |
| `port is already allocated` | Port 8000 in use | `SHIPPING_HTTP_PORT=8001` in `.env` |
| `migrate` exits 1 with `28P01: password authentication failed` | `.env` password changed after the volume was created | Copy the `.env` of the checkout that first started the `shipping` project on this machine. Deleting the volume loses its data: only for your own, disposable data, and never during the session |
| `migrate` log shows `libgssapi_krb5.so.2` or a failed `SELECT` from `__EFMigrationsHistory` | Normal on the first start | None: only a non-zero exit is a failure |
| `/health`: `404 page not found` without `Server: Kestrel` | Traefik has no healthy API to route to, or wrong host name | Wait and retry; `docker compose ps api`; `SHIPPING_HOST` |
| `/health`: `503 Unhealthy` | PostgreSQL unreachable | `docker compose ps postgres`, `docker compose logs postgres` |
| `docker compose exec` prints nothing | The rehearsal VM's Windows Docker CLI loses `exec` output | Run it from a Linux or WSL shell |
| Integration tests: `Docker is either not running or misconfigured` | Testcontainers cannot reach Docker | `docker info` in the same shell; `DOCKER_HOST` if needed |
| `NETSDK1004` from `dotnet ef` | New projects not restored | `dotnet build Shipping.slnx` first |
| `Filename too long` on Windows | Deep checkout path | Clone into a short path, or `git config --global core.longpaths true` |

More: README [Troubleshooting](../../README.md#troubleshooting) and each demo's **Recover** table in the
[runbook](demo-runbook.md).

## Success criteria

The session has succeeded when:

- all seven parts were covered, Demos 1–5 were shown or their recovery path was used and explained;
- every developer slot in the [readiness checklist](team-readiness-checklist.md) is complete, with each item marked
  Pass, Fail, Blocked or N/A by that developer, and every Fail or Blocked item has a follow-up owner;
- each developer can answer most of the questions above without help;
- the Stream Lead has recorded which demonstrations they saw and their decision;
- no participant's machine was left with deleted volumes or containers of other projects.

The technical precondition, that every demonstration runs, was established by the rehearsal before the session
([US-EN-1 T6 evidence](../testing/US-EN-1-T6-red-green.md)). It says nothing about whether the team understood it.

## Handoff checklist

For the facilitator, after the session:

- [ ] Attendance recorded in the checklist by the participants themselves.
- [ ] Every developer's readiness column filled in by that developer.
- [ ] Every Fail or Blocked item has an owner and a follow-up in the checklist's blocker table.
- [ ] How module ownership is decided agreed, and the owners recorded.
- [ ] Stream Lead's acceptance section filled in by the Stream Lead.
- [ ] Questions that could not be answered recorded as follow-ups, not answered by guesswork.
- [ ] The Jira subtask updated by a person, with a link to the completed checklist.
- [ ] Session clones and the `shipping-walkthrough` Compose project, if started, removed. The `shipping` project
      stopped with `docker compose down`, never `down -v`.

## Reference documentation

- [README](../../README.md): quickstart, build and test, architecture, creating a module, troubleshooting.
- [ADR-001](../architecture/ADR-001-four-project-modules.md) four projects per module;
  [ADR-002](../architecture/ADR-002-module-persistence.md) module persistence.
- [Local environment](../local-environment.md): Compose services, networks, startup order, health endpoint.
- [Persistence](../persistence.md): module contexts, migrations, production bundles.
- [Development workflow](../development-workflow.md): from requirement to shipped behaviour.
- [Modules](../../src/Modules/README.md): Shipping's notes, then MP Core's upstream module guidance.
- [AI skills](../ai-skills.md) and `.mpcore/skills/`: the procedures assistants follow here.
- Evidence of each task: `docs/testing/`.
