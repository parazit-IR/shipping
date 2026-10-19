# US-EN-1 T5 test evidence

US-EN-1 T5: the README takes a new developer from `git clone` to a running environment, a green test run and a
new module in under 15 minutes. The Jira key of this subtask was not available when this was written.

All runs took place on 2026-10-19 on branch `feat/us-en-1-t5-readme-quickstart`, created from `main` at
`a75aab5` (SLS-124, SLS-125, SLS-126 and SLOS-127 merged).

| | |
|---|---|
| Machine | Windows Server 2025 Datacenter (10.0.26100) VM, 8 vCPU, 31 GiB, data-centre network |
| Docker | Engine 27.5.1 (Alpine Linux 3.21 in WSL 2), client 27.5.1, Compose v2.32.4, no buildx plugin |
| .NET | SDK 10.0.302 |
| Shells | Git Bash (MSYS2 bash 5.3, GNU sed, OpenSSL 3.5.7, curl 8.21.0); Windows PowerShell 5.1.26100 |

Not run on Linux Mint or macOS. The Docker engine was not running at the start of the session: its WSL boot
script failed (`failed to start containerd: exec: "containerd": executable file not found in $PATH`), so
`dockerd` was started by hand in the `docker-wsl` distribution for the session. Its configuration was not changed.

## Tests

| Suite | What it checks |
|---|---|
| `Shipping.ArchitectureTests/DocumentationTests` (new, 12 tests, static) | The README has the onboarding sections, with the Quickstart before the .NET workflow; the Quickstart runs clone, `.env`, `config`, `up --wait`, `ps`, `/health`, `down` in that order, needs no `dotnet`, waits and retries for a bounded time against the port and host `compose.yaml` publishes, and never deletes the volume; the build and test commands are the ones CI runs; the module instructions match the template's short name, its four projects, the host's registration lines and the migration folder, and build before `dotnet ef`; PowerShell blocks use `Copy-Item` and `curl.exe`; no literal password, `.env` git-ignored, `.env.example` empty; no onboarding document calls delivered work missing; every path named in the onboarding documents exists; every relative link and heading anchor in `README.md`, `docs/` and `src/Modules/README.md` resolves. |
| `Shipping.ArchitectureTests/SkeletonTests.Template_can_generate_and_compile_a_disposable_host` (existing) | The template generates and compiles a module. Not duplicated. |
| `tests/compose/compose-smoke.sh` (existing) | Compose validates, starts every service, migrates, and answers `200 Healthy` through Traefik. Not duplicated. |

The documented commands themselves were run by hand, as described below: the quickstart from fresh clones in Git
Bash and PowerShell, and the module steps in a disposable clone.

## RED

`DocumentationTests` was written before the README was changed and run against `main`'s documents:
[US-EN-1-T5-red.txt](evidence/US-EN-1-T5-red.txt). 1 passed, 11 failed. The failures were real: the README had
no Prerequisites, Quickstart, Architecture, Health endpoint, Troubleshooting or Documentation section, still said
"There is no health endpoint yet (T4)", had no `--wait`, bounded retry or PowerShell commands, and its module steps
had no migration command. `docs/capabilities.md` linked to `getting-started.md#changing-these-settings-later`, a
heading removed in T1. The password test passed: `.env.example` and `.gitignore` were already correct.

Two failures of `Paths_named_in_the_onboarding_documents_exist` were the test's own: it took the MIME type
`text/plain` and the git-ignored `compose.override.yml` for repository paths. The test was corrected (a path's
first segment must be a top-level directory; developer-created files are listed) before the documents changed.

## Findings from running the documented commands

| Found by | Finding | Change |
|---|---|---|
| Cold quickstart, attempt 1 | `git clone` on Windows fails with `Filename too long` under a deep directory: `…/Migrations/20261015081705_Baseline.Designer.cs` passes 260 characters. | Troubleshooting entry. The timed runs used a shorter directory. |
| Module walkthrough, attempt 1 | `dotnet ef migrations add` right after generating a module fails with `NETSDK1004: Assets file … not found`: the new projects were never restored. The steps in `getting-started.md` and `persistence.md` had the same gap. | README builds first; `persistence.md` says so; `getting-started.md` points to the README. The test checks the order. |
| Module walkthrough, attempt 1 | `dotnet sln add` of the Api project adds its three referenced projects too (the glob reported the other three as already present). | README adds the Api project only: no shell glob, so the same command works in PowerShell. |
| Changed-password run | `migrate` logs a misleading `libgssapi_krb5.so.2: cannot open shared object file` line before `28P01: password authentication failed`. | Troubleshooting entry. |
| Quickstart | In Windows PowerShell 5.1 `curl` is an alias of `Invoke-WebRequest`. | PowerShell block uses `curl.exe`; the test forbids bare `curl` there. |

Reported, not changed (outside T5): the API log shows `Executed DbCommand … SELECT 1` for each health probe (EF
Core's `CanConnectAsync` on Npgsql), while `local-environment.md` says of the check that "Nothing is queried".
Nothing of a module is read or written, but a query does run.

## Quickstart timing

The Docker-only path of the README, run as a script from a fresh `git clone` of `main` (`a75aab5`) in Git Bash.
Clock: from the start of `git clone` to the `200 Healthy` answer of the bounded `curl`. Not measured: reading the
README, installing Docker, editing `.env` by hand (the `sed` one-liner was used), and the .NET build and tests.
Logs: [US-EN-1-T5-quickstart-timing.txt](evidence/US-EN-1-T5-quickstart-timing.txt).

| Run | Cache | Exit codes | `up --build --wait` | Total |
|---|---|---|---|---|
| 1 | Cold: no .NET SDK, ASP.NET or Traefik image, no build or NuGet cache in the engine. `postgres:17.6-alpine` was already present. | all 0 | 103 s | **111 s** (04:59:45Z–05:01:36Z) |
| 2 | Warm: images and build cache from run 1; fresh clone, new volume | all 0 | 19 s | **26 s** (05:03:10Z–05:03:36Z) |
| PowerShell | Warm; the README's PowerShell block verbatim (log at the end of the same file) | all 0 | | 25 s |

Differences between the timed Git Bash script and the README: the script's `curl` used `--retry 30` and
`-H 'Host: localhost'` (the README has `--retry 15` and no header, which was verified to route the same way), and
the script sent one extra unretried request first. None of this changed the time: that first request after
`up --wait` returned was already `200` from Kestrel in both timed runs, so no retry happened. The Traefik `404`
window documented in the README did not occur here. These are a data-centre VM's numbers. A first run
on a home connection downloads the same images and packages (the .NET SDK image is the largest) and will be
slower; it has not been measured, nor has a run on Linux Mint.

The .NET path was not timed from a cold NuGet cache: the packages were already in the machine's cache. With a warm
cache, the times of restore, Release build and the full test run are in the final regression log (below).

## Module walkthrough

The README's "Create a module" commands, run in disposable copies with `DOTNET_CLI_HOME` set inside them (so the
template install did not touch the user's template store), module name `Booking`:
[US-EN-1-T5-module-walkthrough.txt](evidence/US-EN-1-T5-module-walkthrough.txt).

| Attempt | Tree | Result |
|---|---|---|
| 1 | clone of `main`, the steps as previously documented | `dotnet ef migrations add` failed with `NETSDK1004` (see Findings) |
| 2 | clone of `main`, the README steps | every command exited 0; tests: 12 unit, 15 integration, 21 architecture passed |
| 3 | this branch's working tree (README and `DocumentationTests` included) | every command exited 0; tests: 12 unit, 15 integration, **33** architecture passed (21 + the 12 documentation tests) |

Attempt 3 answers the independent review's finding that a developer who follows the example literally must not
break the documentation tests: an earlier version of `Readme_module_instructions_match_the_template_and_the_host`
asserted that `src/Modules/Booking` does not exist, and that assertion was removed. In attempts 2 and 3
`Shipping.Modules.Booking.Infrastructure.dll` was in both test outputs, so the convention-based persistence and
migration tests covered the new module. The copies were deleted afterwards.

## Troubleshooting messages reproduced

| Message in the README | Reproduced by |
|---|---|
| `required variable POSTGRES_PASSWORD is missing a value` | `docker compose config --quiet` with `.env` copied from `.env.example` unchanged (exit 15) |
| `Bind for 127.0.0.1:8000 failed: port is already allocated` | `up` while a throwaway container held `127.0.0.1:8000` |
| `service "migrate" didn't complete successfully: exit 1` and `28P01: password authentication failed for user "shipping"` | `up` from a fresh clone with a new password against the volume of the previous run |
| `Docker is either not running or misconfigured` | integration tests with `DOCKER_HOST=tcp://127.0.0.1:1` |
| `Filename too long` | `git clone` under a deep directory on Windows |
| Traefik `404 page not found` without `Server: Kestrel` | `curl -H 'Host: other' http://localhost:8000/health` |

Each run removed only what it created: its containers, networks and the `shipping_postgres-data` volume (which did
not exist before), and the throwaway port-holding container. Images pulled for the runs were kept.

## GREEN and regression

The final run on this branch's working tree: [US-EN-1-T5-green.txt](evidence/US-EN-1-T5-green.txt). It runs the CI
commands (`dotnet restore`, Release build, `dotnet test Shipping.slnx -c Release --no-build`), `docker compose
config --quiet` with a throwaway password, and `bash tests/compose/compose-smoke.sh`.

The smoke test ran in a `docker:27.5.1-cli` container with `bash` and `curl` added, started from the Docker CLI
inside WSL, with the repository and the engine's socket mounted and host networking, as its own `shipping-smoke`
project. This is the Linux setup CI uses. The Windows Docker CLI on this machine receives no container output, so
in Git Bash the same script failed 7 of 24 checks: [US-EN-1-T5-smoke-git-bash.txt](evidence/US-EN-1-T5-smoke-git-bash.txt)
has the run and the diagnosis. The README and `local-environment.md` therefore do not claim Git Bash support for the
smoke test.

### REFACTOR

After GREEN, the independent review's findings were applied: the retry check also requires `--fail` and
`--retry-all-errors` (without them curl does not retry Traefik's `404`); the example-module assertion was removed
(above); the README now names curl 7.71 as the minimum, the `docker` group on Linux, `SHIPPING_HOST` in the Traefik
`404` row, and that `docker compose ps` lists `migrate` only with `-a`. The documentation tests were rerun after
each change, and the final run in `US-EN-1-T5-green.txt` was made after all of these changes. A second
independent review then only asked for the README to stop naming macOS for the smoke test; after that one-cell
edit the 12 documentation tests were run again.
