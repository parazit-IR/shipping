# US-EN-1 T6 test and rehearsal evidence

US-EN-1 T6: a reproducible team walkthrough of the foundation, with a facilitator guide, a demo runbook and a
readiness checklist ([docs/onboarding](../onboarding/team-walkthrough.md)). The Jira key of this subtask was not
available when this was written.

All runs took place on 2026-10-19 on branch `feat/us-en-1-t6-team-walkthrough`, created from `main` at `95fd2ec`
(T5, PR #6, merged). The runtime demonstrations ran in fresh clones of `main` from GitHub, because the documents
being written did not change any code.

| | |
|---|---|
| Machine | Windows Server 2025 Datacenter (10.0.26100) VM, 8 vCPU, data-centre network |
| Docker | Engine 27.5.1 (Alpine Linux 3.21 in WSL 2, `DOCKER_HOST=tcp://127.0.0.1:2375`); Windows client Compose v2.32.4; WSL client Compose v2.33.0 |
| .NET | SDK 10.0.302 |
| Shells | Git Bash (MSYS2); Windows PowerShell 5.1.26100; `sh` inside the `docker-wsl` WSL distribution |

Not run on Linux Mint, macOS or Docker Desktop. As in T5, the Docker engine was stopped at the start: its WSL boot
script fails with `failed to start containerd: exec: "containerd": executable file not found in $PATH` because its
PATH lacks `/usr/local/bin`. `dockerd` was started by hand for the session with that PATH set. Its configuration was
not changed.

**What this evidence proves and what it does not.** It shows that the documents' checks pass and that every
demonstration in the runbook runs on one machine as written. It does not show that a session was held, that any
developer attended or understood it, or that the Stream Lead saw or accepted it. Those are recorded by people in the
[readiness checklist](../onboarding/team-readiness-checklist.md), which is empty.

## Parent story gates

T6 adds documents and documentation checks only. For the parent story's gates it points to evidence that already
exists:

| Gate | Evidence for T6 | Applies to T6 |
|---|---|---|
| NFR-ARCH-1 (architecture) | [ADR-001](../architecture/ADR-001-four-project-modules.md), [ADR-002](../architecture/ADR-002-module-persistence.md); `SkeletonTests` and `PersistenceRulesTests` (layer and module references, one context and schema per module, no cross-schema key, nothing migrates at startup); [T1 review](../reviews/US-EN-1-T1-final-verification.md), [T2 evidence](US-EN-1-T2-red-green.md). The walkthrough teaches these rules and Demo 4 shows them covering a new module. | Explained and demonstrated; no new architecture |
| NFR-OPS-1 (operations) | `compose.yaml`, the `migrate` service, `GET /health`, `tests/compose/compose-smoke.sh` (24/24 on this branch, below); [T3](US-EN-1-T3-red-green.md), [T4](US-EN-1-T4-red-green.md), [T5](US-EN-1-T5-red-green.md) evidence. Demos 1, 2 and 5 rehearse them. | Explained and demonstrated; no new operational behaviour |
| OpenAPI | None configured; the documents say so. | No: T6 adds no endpoint |
| Time-zone serialization | No payload with a date exists. | No: T6 adds no API or domain code |
| Mutation score | No mutation testing is configured; the documents say so. | No: no new production code to mutate |

## Tests

`tests/Shipping.ArchitectureTests/WalkthroughDocumentationTests.cs` (new, 16 tests) extends the T5
`DocumentationTests` class and reuses its helpers. The T5 checks for passwords, stale statements and paths now cover
the three onboarding documents as well, and one stale-statement pattern was added for the "Team walkthrough: SLS-15 T6"
line of `docs/local-environment.md`.

| Check | What it guards |
|---|---|
| Guide structure | Headings for objectives, prerequisites, facilitator setup, agenda, questions, success criteria, handoff. |
| Agenda | Timed rows, contiguous from 0, ending between 60 and 90 minutes. |
| Guide links | The runbook, the checklist, this evidence, the README, both ADRs, local environment, persistence, development workflow, modules guide. |
| Demonstrations in the guide | Every runbook demo is linked; each part that links Demos 1–5 says what to expect, why it matters and how to recover. |
| Runbook structure | Demos 1–6; Demos 1–5 have commands, expected results, a reason and recovery steps. |
| Demo 1 | bash and PowerShell: `docker info`, `.env`, `config --quiet`, `up --build -d --wait --wait-timeout`, `ps`, `migrate`, then a bounded, retrying `/health` call, in that order. |
| Demo 2 | Names `SampleDbContext`, Sample's migrations folder and the schema read from `SamplePersistence.cs`; opens `psql` in the `postgres` service; reads `migrate`'s log. |
| Demo 3 | The three CI commands and a `dotnet test` per test project found under `tests/`. |
| Demo 4 | bash and PowerShell: `git clone` first, then `DOTNET_CLI_HOME`, the template's short name, solution, host reference, build, connection variable, migration for `WalkthroughDbContext`, tests, in that order; never a clone into `.`; the host lines are Sample's, renamed. |
| Demo 5 | `stop postgres`, `/health`, `start postgres`, `/health` in order; explains `503`, `404` and `Server: Kestrel`. |
| Safety | In command blocks (not output blocks): no `down -v`, `--volumes` or `--rmi all` except in a line starting `docker compose -p shipping-walkthrough down`; no `prune`, `volume/image/container rm`, `docker rm/rmi/kill/stop`, `docker compose rm/kill`, forced push or hard reset. |
| PowerShell | `curl.exe`, never the `curl` alias. |
| Checklist parts | Agent verification, attendance, readiness, Stream Lead acceptance and blockers apart; Pass, Fail and Blocked available. |
| Checklist is empty | No ticked box, no date, every Developer, Stream Lead and Facilitator cell empty, all five developer slots present, ownership, blocker and question tables empty beyond their first column. |
| No claims | No sentence saying a session was held, developers completed it or the Stream Lead approved it; no broker, authentication or OpenAPI described as present; no `/openapi`, `/swagger` or bearer token in a command. |
| Jira keys | No key other than those the repository already names (SLS-15, SLS-124, SLS-125, SLS-126, SLOS-127). |

The checks are static. Whether the commands work was established by running them (below).

## RED

**First RED.** The tests were written before any onboarding document existed and run against the branch, which then
equalled `main`: [US-EN-1-T6-red.txt](evidence/US-EN-1-T6-red.txt). `dotnet test tests/Shipping.ArchitectureTests -c
Release --no-build --filter FullyQualifiedName~DocumentationTests`: exit code 1, **28 tests, 9 passed, 19 failed**.

- 18 failures: `DirectoryNotFoundException` for `docs/onboarding/team-walkthrough.md` (10), `demo-runbook.md` (6) and
  `team-readiness-checklist.md` (2). The T5 password and path checks are among them, because they now read the new
  documents.
- 1 failure: `docs/local-environment.md still says 'Team walkthrough:** SLS-15 T6.'`, the stale statement.
- The 9 passing tests are the T5 README checks and the link check, which had no new link to resolve yet.

### Changes to the tests after the first RED

The test file changed after that run. The independent review found this from the line numbers in the RED stack
traces; an earlier version of this section did not disclose it, and said that no test was changed. Every change:

| When | Change | Why | Effect on strictness |
|---|---|---|---|
| While writing Demo 4, before GREEN | The deletion rule exempted lines containing `-p shipping-walkthrough` from the `down -v` ban (constant `DemoProject`). | Demo 4's optional step removes the volume of the Compose project it creates itself; the rule is about volumes the demo did not create. | Relaxed. Too broadly: any line merely containing the text was exempt. |
| After the review | The exemption is anchored to lines starting `docker compose -p shipping-walkthrough down `. The rule also bans `--rmi all`, `volume/image/container rm`, `docker compose rm/kill`. | Review finding 13. | Tightened. |
| After the review | The emptiness check also requires the ownership, blocker and question tables to be empty beyond their first column. | Review finding 13. | Tightened. |
| After the review | The command checks read only command blocks, not ```` ```text ```` blocks that show expected output. | The negative controls showed the deletion rule reading `$ docker compose -p shipping-walkthrough down -v ...` in an output block, where the `$ ` prompt defeated the anchored exemption, so the positive control failed. | Neutral for commands; output blocks are no longer checked as commands. |

**Final-test RED and controls.** To show the final tests still fail for the right reasons, they were copied into a
scratch clone of `main` (no T6 document) and run:
[US-EN-1-T6-red-final-and-controls.txt](evidence/US-EN-1-T6-red-final-and-controls.txt).

1. Final-test RED: **28 tests, 9 passed, 19 failed**, the same 19 for the same reasons as the first RED.
2. Positive control, with the T6 documents copied in: **28 passed**.
3. Negative controls, one violation injected at a time and the file restored afterwards. Each named check failed and
   named the injected text: `docker compose down -v`; `docker compose -p shipping-walkthrough-x down -v` (the exemption
   is anchored); `docker image rm shipping-api`; a name in an attendance row; `Pass` in a developer's readiness
   column; a prefilled blocker owner; a ticked box; "The walkthrough was delivered to all five developers."; "Kafka is
   configured for module events."; an invented key (`SLS-` followed by `999`; written apart here, because this
document is checked too); a bare `curl -i` in a PowerShell block. **11 of 11
   failed as they must.**
4. Positive control again after restoring every file: **28 passed**.

In both RED runs every failure was a missing file or the stale line. The negative controls are what show the safety,
claims, checklist and key checks can fail on content.

## Technical rehearsal

Every demonstration was run before the documents' expected output was written, and that output was copied from these
runs. Logs: [US-EN-1-T6-rehearsal.txt](evidence/US-EN-1-T6-rehearsal.txt). No password or connection string with a
password is in them: the generated password was never printed, and `migrate`'s log was filtered for the word.

| Section | Run | Result | Duration |
|---|---|---|---|
| Demo 1 | Git Bash, fresh clone of `main` (`95fd2ec`) | All commands exit 0; `200 Healthy` | 32 s, warm images |
| Demo 2 | Git Bash for files and `migrate`; WSL Docker CLI for `psql` | Schema and history as documented; second run applies nothing | about 1 min |
| Demo 3 | Git Bash, same clone | 12 unit, 33 architecture, 15 integration passed; 0 failed, 0 skipped | about 1.5 min |
| Demo 4 | Git Bash (runbook bash blocks, with the optional Compose step) | All commands exit 0; tests pass with the new module | 263 s |
| Demo 4 | Windows PowerShell 5.1 (runbook PowerShell blocks, without the optional step) | All commands exit 0; tests pass with the new module; clone and both variables removed | 100 s |
| Demo 5 | Git Bash; direct Kestrel check through the WSL Docker CLI | `503`, then Traefik `404` once unhealthy, recovery to `200` | about 2.5 min per run, two runs |
| Demo 6 | Discussion; facts checked: branch names in `git branch -a`, `.github/workflows/ci.yml`, GitHub's public API for `main` (`"protected": false`) | — | — |

Not run: a session with people, the timing of the whole 90-minute agenda, the alternative failure (a failing
migration), which the Compose smoke test covers, and Demo 1 on a cold cache (T5 measured 111 s).

### Demo 1 — Local environment

`docker info` answered `Server 27.5.1`; `docker compose config --quiet` printed nothing and exited 0;
`docker compose up --build -d --wait --wait-timeout 300` returned after 22 s with `postgres` and `api` healthy;
`docker compose ps -a migrate` showed `Exited (0)`. `curl -i --fail --retry 15 --retry-delay 2 --retry-all-errors
http://localhost:8000/health` answered `200 OK`, `Server: Kestrel`, `Healthy` on the first try: the transient Traefik
`404` did not occur. `curl -i http://localhost:8000/` answered `404` with `Server: Kestrel`, and
`curl -i http://127.0.0.1:8000/health` Traefik's own `404 page not found`.

On a new volume `migrate`'s log shows `Failed executing DbCommand` for the `SELECT` from `sample."__EFMigrationsHistory"`
before applying `20261015081705_Baseline`, and the `libgssapi_krb5.so.2` line T5 recorded. Both are normal, and the
runbook now says so.

### Demo 2 — Module persistence

In `psql`: schemas `public` and `sample`; one relation, `sample.__EFMigrationsHistory`; one row,
`20261015081705_Baseline` with product version `10.0.11`. `docker compose start migrate` then logged
`No migrations were applied. The database is already up to date.` and exited 0.

**Windows and Linux differ here on this machine.** The same `psql` command through the Windows Docker CLI (Git Bash)
exited 0 and returned no output, the limitation T5 diagnosed for attached and `exec` streams. Through the Docker CLI
inside WSL, against the same engine, the output arrived. The runbook and the guide's facilitator setup tell the
facilitator to check this beforehand and name the fallback.

### Demo 3 — Tests

On the clone of `main`: `dotnet restore` 5 s, Release build 10 s with 0 warnings and 0 errors, then per project
`Passed!` with `Failed: 0, Skipped: 0`: 12 unit (6 s), 33 architecture (51 s), 15 integration (23 s, PostgreSQL through
Testcontainers on the WSL engine). The `[exit 0]` printed after each test command in the log is that of `grep`, which
filtered the output; the `Passed!` lines are the result.

### Demo 4 — Module creation

Bash, as written in the runbook, in `$(mktemp -d)/shipping-demo` with `DOTNET_CLI_HOME` inside the clone: the template
installed and generated four projects; `dotnet sln add` of the Api project added all four; `dotnet add reference`
and three lines in `Program.cs` (applied with `sed` instead of an editor); `dotnet build` 0 errors;
`dotnet ef migrations add Baseline ... --context WalkthroughDbContext` printed `Done.` and created
`20261019060608_Baseline.cs`, its designer file and `WalkthroughDbContextModelSnapshot.cs`; `dotnet test Shipping.slnx`:
12 unit, 15 integration, 33 architecture passed, 0 failed, 0 skipped. Optional step: `docker compose -p
shipping-walkthrough up --build -d --wait` (138 s, a new image build) applied `sample-efbundle` and then
`walkthrough-efbundle`; `/health` on port 8001 answered `200`; `down -v --rmi local` removed that project's volume,
networks and two images and nothing else; the clone was deleted.

PowerShell 5.1, the runbook's PowerShell blocks verbatim, in `$([IO.Path]::GetTempPath())shipping-demo-<random>`: every
command exited 0, the migration was `20261019061142_Baseline`, `dotnet test Shipping.slnx` passed 12 unit,
15 integration and 33 architecture tests, and the clean-up removed the clone and unset `DOTNET_CLI_HOME` and
`ConnectionStrings__PostgreSql`. No behavioural difference from bash was seen. In the log, the SDK's first-run text is
interleaved with `System.Management.Automation.RemoteException` lines: an artefact of the rehearsal script redirecting
stderr in Windows PowerShell 5.1 (removed from the evidence file), not of the runbook commands.

Findings that changed the runbook:

- The generated `Baseline` migration has an empty `Up()`, like Sample's: the schema is created with the module's
  history table when the migration is applied. A draft sentence saying the migration creates the schema was
  corrected.
- The first `dotnet` command with a new `DOTNET_CLI_HOME` runs the SDK's first-run step and reports installing the
  ASP.NET Core development certificate. The user's certificate store still held only its earlier `CN=localhost`
  certificate afterwards, so nothing was added here; the runbook says what happens on a machine without one.

### Demo 5 — PostgreSQL unavailable

Run 1, through Traefik, after `docker compose stop postgres`:

| Since stop | `/health` through Traefik | `docker compose ps api` |
|---|---|---|
| 2 s | `503 Service Unavailable`, `Server: Kestrel`, `Unhealthy` | `(healthy)` |
| up to 113 s | `503`, `Server: Kestrel` | `(healthy)` |
| 144 s | `404 Not Found`, no `Server` header, `404 page not found` | `(unhealthy)` |
| 10 s after `start postgres` | `200 OK`, `Server: Kestrel`, `Healthy` | `(healthy)` |

Run 2 waited until the API was `unhealthy` (about 2 min 20 s), then asked both ways: through Traefik `404 page not
found`; directly inside the container, through the WSL Docker CLI, `HTTP/1.1 503 Service Unavailable`,
`Server: Kestrel`, `Unhealthy`. After `start postgres` the retrying `curl` received Traefik's `404` twice, then `200`.
Only the `postgres` container of the rehearsal's `shipping` project was stopped and started; nothing was deleted.

### Rehearsal of the commands changed after review

Every runbook command the review changed was run again (end of
[the rehearsal log](evidence/US-EN-1-T6-rehearsal.txt)):

- **Demo 1, shared volume:** `docker volume ls -q --filter name=shipping_postgres-data` printed the volume. In a second
  fresh clone, `cp ../shipping/.env .env`, `config --quiet`, `up --build -d --wait` exited 0; `migrate` logged
  `No migrations were applied`; `/health` `200`. Its `up` took over the same `shipping-*` containers, as the runbook
  now says. `ps -a migrate --format '{{.Service}}: {{.Status}}'` printed `migrate: Exited (0) ...`;
  `curl -i http://127.0.0.1:8000/health` returned Traefik's `404 page not found`.
- **Demo 5 recovery:** `docker compose restart api` exited 0; the retrying `curl` got Traefik's `404` three times, then
  `200`, `Server: Kestrel`.
- **Demo 4, run A** (new `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` and `DOTNET_NOLOGO=1`; PowerShell optional Compose
  block): bash passed, without the first-run banner or certificate message; PowerShell passed every step including the
  optional one (`walkthrough-efbundle` applied, `200` on port 8001, only that project's volume and images removed),
  **but its clean-up failed**: `Remove-Item` reported `Access ... Microsoft.AspNetCore.Mvc.Testing.Tasks.dll is
  denied`. `DOTNET_CLI_HOME` also moves the NuGet cache into the clone, and a reused MSBuild node held a task DLL from
  it. `dotnet build-server shutdown` released it and the clone was deleted. The runbook's clean-up now starts with
  that command, and a recovery row describes the error.
- **Demo 4, run B** (runbook as final): bash 205 s, PowerShell 244 s, both with the optional Compose step. Every command
  exited 0, tests 12 + 15 + 33 passed, both clones and their temporary parent removed, no variable left set, no
  `shipping-walkthrough` volume left.

Not rehearsed: `dotnet new uninstall` in the recovery row (the situation it recovers from was never created).

### GitHub and MP Core checks

Read-only, unauthenticated requests to GitHub's public API on 2026-10-19 at 06:23:30Z (output in
[the rehearsal log](evidence/US-EN-1-T6-rehearsal.txt)):

| Request | Answer |
|---|---|
| `GET /repos/parazit-IR/shipping/branches/main` | `200`, `"protected": false` |
| `GET /repos/parazit-IR/shipping/rules/branches/main` | `200`, `[]` (no ruleset applies to `main`) |
| `GET /repos/parazit-IR/shipping/rulesets` | `200`, `[]` |
| `GET /repos/panahister/mpcore/releases?per_page=5` | `200`, tags `v1.0.1`, `v1.0.0`, `v0.10.1`, `v0.10.0`, `v0.9.3` |

An unauthenticated request sees only what is public. The documents therefore say that no protection or ruleset was
visible, and that the repository owner must confirm what is configured. MP Core is not upgraded; the newer releases are
mentioned only so the team knows 0.10.1 is a deliberate pin.

## GREEN and regression

After the documents were written: `dotnet test tests/Shipping.ArchitectureTests -c Release --no-build --filter
FullyQualifiedName~DocumentationTests`, exit code 0, **28 passed, 0 failed, 0 skipped**. The first run after writing
them had 2 failures, both genuine: the evidence files this document links to did not exist yet, and the guide named
`` `.csproj` `` in code formatting, which the T5 path check reads as a repository path. Both were fixed in the documents,
not in the tests. The test changes made between the first RED and this run are listed under
[Changes to the tests after the first RED](#changes-to-the-tests-after-the-first-red).

## Full test suite and smoke test

On this branch's working tree (base `95fd2ec` plus the uncommitted T6 changes):
[US-EN-1-T6-green.txt](evidence/US-EN-1-T6-green.txt).

| Command | Exit | Result |
|---|---|---|
| `dotnet restore Shipping.slnx` | 0 | |
| `dotnet build Shipping.slnx -c Release --no-restore` | 0 | 0 warnings, 0 errors |
| `dotnet test Shipping.slnx -c Release --no-build` | 0 | Unit 12/12, integration 15/15, architecture **49/49** (33 + 16 new); 0 failed, 0 skipped |
| `docker compose config --quiet` (generated password, not printed) | 0 | no output |
| `bash tests/compose/compose-smoke.sh` | 0 | **24 passed, 0 failed** (265 s) |

The smoke test ran, as in T3–T5, in a `docker:27.5.1-cli` runner with `bash` and `curl`, started through the WSL Docker
CLI with the working tree mounted and host networking, because this VM's Windows Docker CLI loses container output.
The first runner attempt failed before starting (`docker run` exit 125: Git Bash rewrote `-w /repo` into a Windows
path); it was rerun with `MSYS_NO_PATHCONV=1`. Both attempts are in the log. The smoke test removed only its own
`shipping-smoke` project; no `shipping-smoke` container or volume remained afterwards.

**After the review fixes** (tests and documents changed): [US-EN-1-T6-final.txt](evidence/US-EN-1-T6-final.txt).
`dotnet restore` exit 0; Release build exit 0, 0 warnings, 0 errors; `dotnet test Shipping.slnx -c Release --no-build`
first exited 1 with 48/49 architecture tests: `Walkthrough_documents_and_evidence_name_no_invented_Jira_key` caught
this document naming the negative control's invented key literally. The document was reworded, the check left as it
was, and the rerun exited 0: **unit 12/12, integration 15/15, architecture 49/49, 0 failed, 0 skipped**. The smoke test
was not rerun after the review: no file it builds or tests (`compose.yaml`, `Dockerfile`, `src/`,
`tests/compose/`) changed since its 24/24 run.

GitHub Actions has not run for this branch: nothing has been pushed. CI status is unknown until a pull request runs.

## Independent review

A separate review agent, which had not written the changes, reviewed the diff, the documents against the repository,
the evidence and the logs, read-only. Verdict: **CHANGES_REQUIRED**, with 2 MAJOR, 10 MINOR and 2 SUGGESTION findings
and no BLOCKER. It found no claim of human participation and no claim of a capability that does not exist.

| # | Severity | Finding | Resolution |
|---|---|---|---|
| 1 | MAJOR | `compose.yaml` fixes the project name `shipping`, so a fresh clone with a newly generated password fails with `28P01` against an existing volume | Runbook: the shared project and volume explained, a volume check before creating `.env`, copy `.env` from the checkout that created the volume, the source named in the `28P01` row. Guide: facilitator setup changed. Rehearsed in a second clone. |
| 2 | MAJOR | The test file changed after RED and the evidence did not say so, and the GREEN text said no test changed | [Changes to the tests after the first RED](#changes-to-the-tests-after-the-first-red); final-test RED, positive and negative controls run and recorded. |
| 3 | MINOR | Three expected outputs were not from the rehearsal | `--format` added to `ps -a migrate`; the `127.0.0.1` request rehearsed; trimmed `ps` columns marked. |
| 4 | MINOR | "MP Core 1.0.x" unsupported | Checked and recorded under [GitHub and MP Core checks](#github-and-mp-core-checks); the guide links it. |
| 5 | MINOR | Authentication shown as an open decision; AGENTS.md rules not taught | Part 1 talking point on AGENTS.md's non-negotiable rules; the open item is now the configuration of the bearer resource server. |
| 6 | MINOR | Optional Compose step had no PowerShell variant; no macOS `sed` note | PowerShell block added and rehearsed; note added. |
| 7 | MINOR | Recovery hid a possible change to the user-wide template store | Recovery row with `dotnet new uninstall` added. |
| 8 | MINOR | `up -d --wait` would not recover an unhealthy API | `docker compose restart api`, rehearsed. |
| 9 | MINOR | Branch protection claim incomplete and without raw evidence | Rulesets endpoints checked and recorded; wording "no protection or ruleset visible; the owner must confirm"; checklist item 10 reworded. |
| 10 | MINOR | "Module ownership is decided by the Stream Lead" was invented | Presented as a proposal for the session; the checklist records how it was decided. |
| 11 | MINOR | No NFR traceability | [Parent story gates](#parent-story-gates). |
| 12 | MINOR | `HasDefaultSchema("sample")`, two vs three Program.cs lines, Api may also reference Domain, a missing package | Corrected. |
| 13 | SUGGESTION | Deletion and emptiness checks too weak; no negative control | Tightened (see the changes table); 11 negative controls. |
| 14 | SUGGESTION | Dev certificate and leftover `mktemp` parent in Demo 4 | `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`, `DOTNET_NOLOGO=1`; the parent is deleted. Rehearsing this found the locked-file problem above. |

**Re-review** by the same agent, read-only: **APPROVE_WITH_MINOR**. All 14 findings resolved; the `Commands` change judged
a legitimate fix, not a loosening; the claim that the smoke test needed no rerun confirmed. New: one MINOR (Demo 4
duration quoted from the pre-fix run) and suggestions (two guide rows not aligned with the runbook, test-file hashes in
the controls evidence, `${DEMO:?}` in the bash clean-up, `--format` missing from one expected-output header). All were
applied; the documentation checks were run again afterwards: 28/28. The re-review approves the technical work; it is
not a human review of the pull request.
