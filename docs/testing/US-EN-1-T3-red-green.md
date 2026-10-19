# US-EN-1 T3 (SLS-126) test evidence

All runs took place on 2026-10-18 on branch `feat/SLS-126-docker-compose`, which starts from the SLS-125 commit
`0c2828b`. The development machine runs Windows Server 2025 with .NET SDK 10.0.302. It reaches a remote Linux
Docker Engine 29.7.2 (AlmaLinux 9.8) through a forwarded TCP endpoint (`DOCKER_HOST=tcp://127.0.0.1:2375`).

## Why the runs happen on the engine host

That endpoint returns API calls and exit codes, but it drops the output of attached streams (`docker run`,
`docker exec`). Ports that containers publish are not reachable from the development machine, and the endpoint
dropped intermittently during the session. Run from Windows, the smoke tests would fail for reasons that have
nothing to do with the code. The same endpoint is why `Shipping.IntegrationTests` (Testcontainers) failed 9/9
on Windows before any SLS-126 change, with `No connection could be made because the target machine actively
refused it`.

Each recorded run therefore starts a throwaway runner container on the engine host: `docker:27.5.1-cli` for the
smoke tests, `mcr.microsoft.com/dotnet/sdk:10.0.402` for the .NET suite. The runner uses host networking and
the engine's socket, which is the same shape as a CI runner. It receives the working tree with `docker cp` and
returns its output through `docker logs`. The helper that does this is a local workaround and is not committed.
Two runner attempts died with exit code 255 and no output while the engine restarted containers. Both were
rerun, and nothing they started was left behind.

## RED

The tests were written first, `tests/compose/compose-smoke.sh` (21 tests), and run with no `compose.yaml` and no
`Dockerfile`. Full log: [evidence/US-EN-1-T3-red.txt](evidence/US-EN-1-T3-red.txt).

- Command: `bash tests/compose/compose-smoke.sh`. Exit code **1**. **Passed: 0, Failed: 21.**
- Every failure traces to the missing environment. Examples:
  - `test_compose_configuration_is_valid`: `no configuration file provided: not found`.
  - `test_api_image_runs_the_shipping_host…`: `no api image in the configuration`.
  - `test_postgres_is_healthy_by_pg_isready`: `no postgres container`.
  - `test_traefik_routes_requests_for_the_api_host_to_the_api`: `no answer from http://127.0.0.1:18000/… (Host: localhost)`.
  - `test_each_module_is_migrated_through_its_own_schema_and_history`: `Sample history in sample is '', expected '20261015081705_Baseline'`.
- An earlier RED attempt was run directly from Windows. It was discarded for two reasons. In
  `test_compose_refuses_to_start_without_a_database_password`, `mktemp` produced a `/tmp` path that Windows
  `docker` read as `C:\tmp`. That was a test defect, fixed with `cygpath`. And the endpoint drops `exec`/`run`
  output, as described above.

## GREEN

`Dockerfile`, `.dockerignore`, `compose.yaml` and `.env.example` were added.

- First run: 20/21. `test_only_the_proxy_entry_point_is_published_on_the_host` reported `api 8080 is published`.
  `compose.yaml` publishes no API port. The test read `docker compose port` output for a port that is only
  `EXPOSE`d as a binding. The test was rewritten to read the engine's real host bindings
  (`.NetworkSettings.Ports`). The rewrite's first run reported the only binding,
  `traefik:80/tcp->127.0.0.1:18000`, with a leading space, and the trim was fixed. Both were test defects, and the
  implementation did not change.
- Final GREEN run: exit code **0**, **Passed: 21, Failed: 0**. Log:
  [evidence/US-EN-1-T3-green.txt](evidence/US-EN-1-T3-green.txt).
- .NET suite, working tree, in the SDK runner on the engine host:
  `dotnet restore Shipping.slnx && dotnet build Shipping.slnx -c Release --no-restore && dotnet test Shipping.slnx -c Release --no-build`,
  exit code 0:
  - Build: **0 Warning(s), 0 Error(s)**
  - `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9 - Shipping.UnitTests.dll`
  - `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9 - Shipping.IntegrationTests.dll`
  - `Passed! - Failed: 0, Passed: 21, Skipped: 0, Total: 21 - Shipping.ArchitectureTests.dll`
- On Windows, before the change (baseline): Unit 9/9 and Architecture 21/21 passed. Integration was 0/9 because
  of the endpoint.

## REFACTOR

The `api` stage lost an environment variable it never used (`DOTNET_CLI_TELEMETRY_OPTOUT`; the runtime image
has no CLI). The smoke script's run marker moved next to the other run settings. Rerun: exit code **0**,
**Passed: 21, Failed: 0**. Log: [evidence/US-EN-1-T3-refactor.txt](evidence/US-EN-1-T3-refactor.txt).

## After the independent review

The review found no BLOCKER or MAJOR. Its minor test and doc findings were corrected:

- The non-root and no-SDK checks now fail if `id` or `dotnet` cannot run, so they cannot pass vacuously.
- The "API absent" branch of the failed-migration test can now match.
- The suite gives Compose an empty env file, so a developer's `.env` cannot change what is tested.
- Timestamps are compared without GNU `date`.
- The project name must start with `shipping-smoke`.
- ADR-002's link to persistence.md was fixed after the heading rename.

Rerun: smoke **Passed: 21, Failed: 0**, exit code 0
([evidence/US-EN-1-T3-post-review.txt](evidence/US-EN-1-T3-post-review.txt)). .NET: 0 warnings, Unit 9/9,
Integration 9/9, Architecture 21/21.

## Flaky test found in the pre-commit verification

A pre-commit verification run on the same day (2026-10-18T11:08:31Z, same runner, working tree unchanged
since the post-review run) ended with exit code **1**, **Passed: 20, Failed: 1**:

- `test_a_failed_migration_prevents_the_api_from_starting`: `the failure is not visible in the migrate logs`.

The behaviour under test was correct in that run. `migrate` exited 1, `api` stayed `Created`, and the
`migrate` logs did contain `28P01: password authentication failed for user "shipping"`. The defect was in the
test. It piped `compose logs` into `grep -qi` under `set -o pipefail`. `grep -q` exits at the first match, so
`compose logs`, still writing Npgsql's stack trace, is killed by SIGPIPE. pipefail then reports the pipeline as
failed although the text matched. Whether it fails depends on how much output remains when grep exits, which
is why the post-review run passed. The same pattern, `seq 1 2000000 | grep -qi '^1$'` under pipefail, returned
141 in three of three tries. No other pipe in the script has this shape. Its `| head -n1` pipes read a few lines,
and their exit status is not checked.

Correction (test only, no implementation change): the test now captures the `migrate` logs first and searches
the captured text with a here-string, as the rest of the script does. It also fails if the logs cannot be read,
which is a new check, so the test is not weaker. The assertion is unchanged.

Rerun: three consecutive runs of the whole suite against the engine, each exit code **0**,
**Passed: 21, Failed: 0** (2026-10-18T11:26:25Z, 11:28:27Z, 11:30:24Z). Log:
[evidence/US-EN-1-T3-flake-fix.txt](evidence/US-EN-1-T3-flake-fix.txt). One earlier rerun attempt did not
start because the forwarded Docker endpoint refused connections for about six minutes. It produced no test
result and is not counted. The .NET suite was not rerun after this change, because it touches only the
shell script. The pre-commit verification had already run it on the same tree before the fix: 0 warnings,
Unit 9/9, Integration 9/9, Architecture 21/21.

Evidence file edit: line 51 of [evidence/US-EN-1-T3-red.txt](evidence/US-EN-1-T3-red.txt) had one trailing
space after `does not name the postgres service:`. The value printed there was empty, because no API container
existed during RED. The space was removed so `git diff --check` passes. The line still ends with nothing after
the colon, and no other byte of the log changed.

## Seeded faults (in scratch copies, never in the working tree)

| Fault in `compose.yaml` | Result |
|---|---|
| F1: the API waits for `migrate: service_started` instead of `service_completed_successfully` | `test_migrations_finish_successfully_before_the_api_starts` failed (`api started at …:07.398Z, before migrate finished at …:09.161Z`). `test_a_failed_migration_prevents_the_api_from_starting` failed (`compose up succeeded`). |
| F1: Docker socket mounted read-write | `test_traefik_has_no_dashboard_or_api_and_a_read_only_docker_socket` failed (`docker socket mount RW=true`) |
| F1: `postgres` publishes `127.0.0.1:15432:5432` | The tests passed, correctly: Docker creates no binding for a container that is only on the `internal` `backend` network. A separate probe showed `{"5432/tcp":null}` and that 15432 is unreachable on the engine host. |
| F2: the router rule is `Host(elsewhere.localhost)` | `test_traefik_routes_requests_for_the_api_host_to_the_api` failed (`the 404 did not come from the API`) |
| F2: `exposedByDefault=true` | `test_traefik_has_no_dashboard_or_api_and_a_read_only_docker_socket` failed (`containers are exposed by default`) |
| F3: the API publishes `127.0.0.1:18080:8080` | `test_only_the_proxy_entry_point_is_published_on_the_host` failed (`host bindings are 'api:8080/tcp->127.0.0.1:18080 traefik:80/tcp->127.0.0.1:18000'`) |

## Not covered here

- **CI:** the `compose-smoke` job has not run yet. No GitHub Actions result exists for this branch.
- **Docker Desktop (Windows/macOS) and Git Bash on Windows** were not run end to end, because of the endpoint
  described above.
- **Application health:** `GET /health` is SLS-15 T4. The API's readiness here is a TCP probe.
