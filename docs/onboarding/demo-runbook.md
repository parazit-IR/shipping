# Walkthrough demo runbook

The commands the facilitator runs during the [team walkthrough](team-walkthrough.md), with the output to expect and
how to recover. Every expected output below was copied from the technical rehearsal on a fresh clone of `main`
([US-EN-1 T6 evidence](../testing/US-EN-1-T6-red-green.md)). Durations are that machine's, with warm image and NuGet
caches.

Commands are shown for bash (Linux, macOS, Git Bash) and, where they differ, Windows PowerShell. In PowerShell type
`curl.exe`, never `curl`: `curl` is an alias of `Invoke-WebRequest` in Windows PowerShell 5.1.

Rules for every demo:

- Run Demos 1, 2, 3 and 5 in a checkout of `main` with no uncommitted work. Run Demo 4 only in the disposable clone it
  creates.
- `compose.yaml` fixes the Compose project name to `shipping`. Every clone on a machine, in whatever directory, therefore
  shares the containers `shipping-*-1` and the one database volume `shipping_postgres-data`, whose password is the one
  it was created with. A second clone is not a second environment: it needs the same `.env` password (Demo 1 shows
  how), and its `docker compose up` replaces the containers the first clone started.
- Never run `docker compose down -v`, `docker volume rm` or any `prune` against the `shipping` project or any other
  project during the session. `docker compose down` keeps the database volume.
- If a demo fails, say so and use its **Recover** steps. Do not edit code, tests or `compose.yaml` live to make it
  pass.

## Demo 1 — Start the local environment

**Why it matters:** one command starts the same four services every developer and CI use: PostgreSQL, the migration
runner, the one API process and Traefik. Nothing needs the .NET SDK. **Time:** about 30 s with warm images; about
2 minutes on a first run (111 s measured in T5).

First check whether this machine already has a Shipping database:

```bash
docker volume ls -q --filter name=shipping_postgres-data
```

- **No output:** there is no database yet. Create `.env` with a new password (the first two lines after `docker info`
  below).
- **It prints `shipping_postgres-data`:** a database exists, with the password of the `.env` it was first started
  with. If this checkout has no `.env`, copy that `.env` here from the checkout you started it from (for example
  `cp ../shipping/.env .env`), and skip the two `.env` lines. A new password makes `migrate` fail with `28P01`.

```bash
docker info --format 'Server {{.ServerVersion}}'          # the daemon answers; anything else: start Docker first
cp .env.example .env                                     # only if there is no database yet; .env is git-ignored
sed -i "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 16)/" .env   # GNU sed; on macOS: sed -i ''
docker compose config --quiet                            # no output when compose.yaml and .env are valid
docker compose up --build -d --wait --wait-timeout 300
docker compose ps
docker compose ps -a migrate --format '{{.Service}}: {{.Status}}'
docker compose logs migrate
curl -i --fail --retry 15 --retry-delay 2 --retry-all-errors http://localhost:8000/health
curl -i http://localhost:8000/                           # no business endpoint: the API's own 404
curl -i http://127.0.0.1:8000/health                     # a host name Traefik does not route: Traefik's own 404
```

```powershell
docker info --format 'Server {{.ServerVersion}}'
Copy-Item .env.example .env
(Get-Content .env) -replace '^POSTGRES_PASSWORD=.*', "POSTGRES_PASSWORD=$([guid]::NewGuid().ToString('N'))" | Set-Content .env
docker compose config --quiet
docker compose up --build -d --wait --wait-timeout 300
docker compose ps
docker compose ps -a migrate --format '{{.Service}}: {{.Status}}'
docker compose logs migrate
curl.exe -i --fail --retry 15 --retry-delay 2 --retry-all-errors http://localhost:8000/health
curl.exe -i http://localhost:8000/
curl.exe -i http://127.0.0.1:8000/health
```

In PowerShell the volume check is the same command; run the `Copy-Item` and `-replace` lines only when it printed
nothing.

**Expected:**

```text
$ docker compose ps                        (COMMAND and CREATED columns left out here)
NAME                  IMAGE                  SERVICE    STATUS                    PORTS
shipping-api-1        shipping-api           api        Up 7 seconds (healthy)    8080/tcp
shipping-postgres-1   postgres:17.6-alpine   postgres   Up 15 seconds (healthy)
shipping-traefik-1    traefik:v3.7.15        traefik    Up 15 seconds             127.0.0.1:8000->80/tcp

$ docker compose ps -a migrate --format '{{.Service}}: {{.Status}}'
migrate: Exited (0) 7 seconds ago

$ docker compose logs migrate              (first start on a new volume)
migrate-1  | Applying /migrations/sample-efbundle
migrate-1  | Error: libgssapi_krb5.so.2: cannot open shared object file: No such file or directory
migrate-1  | Failed executing DbCommand (41ms) ... FROM sample."__EFMigrationsHistory" ...
migrate-1  | Applying migration '20261015081705_Baseline'.
migrate-1  | Done.

$ curl -i ... http://localhost:8000/health
HTTP/1.1 200 OK
Cache-Control: no-store, no-cache
Content-Type: text/plain
Server: Kestrel

Healthy

$ curl -i http://localhost:8000/
HTTP/1.1 404 Not Found
Server: Kestrel

$ curl -i http://127.0.0.1:8000/health
HTTP/1.1 404 Not Found
Content-Type: text/plain; charset=utf-8

404 page not found
```

Point out:

- `migrate` is not in `docker compose ps` because it has exited. `ps -a` shows it with exit code 0. `api` started
  only after that (`depends_on: service_completed_successfully` in `compose.yaml`).
- On the first start the bundle logs a failed `SELECT` from `sample."__EFMigrationsHistory"`: the history table does
  not exist yet, EF Core creates it and continues. The `libgssapi_krb5.so.2` line is harmless too. Only a non-zero
  exit of `migrate` is a failure.
- `Server: Kestrel` means the answer came from the API. A `404` without it came from Traefik.
- Only `127.0.0.1:8000` is published. PostgreSQL has no host port.

**Recover:**

| Symptom | Safe action |
|---|---|
| `docker info` fails, `Cannot connect to the Docker daemon`, `error during connect` | Start Docker Desktop, or `sudo systemctl start docker` on Linux. Nothing else works until `docker info` does. |
| `required variable POSTGRES_PASSWORD is missing a value` | `.env` is missing or its password is empty: repeat the two `.env` lines. |
| `port is already allocated` | Set `SHIPPING_HTTP_PORT=8001` in `.env` and use port 8001 in the `curl` commands. |
| `service "migrate" didn't complete successfully: exit 1` | `docker compose logs migrate`. A `28P01: password authentication failed` means `.env` has a new password and the volume an old one. The old one is in the `.env` of the checkout that first started the `shipping` project on this machine: copy that file here and run `up` again. |
| `curl` gives up with `404` and no `Server: Kestrel` | `docker compose ps api`: the API is not healthy yet, or `SHIPPING_HOST` in `.env` is not `localhost`. |
| Anything else | `docker compose ps -a`, `docker compose logs <service>`, then the README [Troubleshooting](../../README.md#troubleshooting). `docker compose down` and `docker compose up --build -d --wait --wait-timeout 300` is always safe: it keeps the volume. |

## Demo 2 — Inspect module persistence

**Why it matters:** each module owns its context, its schema and its migration history
([ADR-002](../architecture/ADR-002-module-persistence.md)). Five developers can add migrations to five modules
without touching a shared model snapshot. **Time:** 3–5 minutes. Needs Demo 1 running.

Show the files, in the editor or with `ls`:

| File | What to point out |
|---|---|
| `src/Modules/Sample/Shipping.Modules.Sample.Infrastructure/Persistence/SampleDbContext.cs` | `internal sealed class SampleDbContext : MPCoreDbContext, ISampleUnitOfWork`; `HasDefaultSchema(SamplePersistence.Schema)`; history table in that schema too. |
| `src/Modules/Sample/Shipping.Modules.Sample.Infrastructure/Persistence/SamplePersistence.cs` | `Schema = "sample"`, and the mappings of this assembly only. |
| `src/Modules/Sample/Shipping.Modules.Sample.Infrastructure/Persistence/Migrations` | `20261015081705_Baseline.cs`, its designer file and `SampleDbContextModelSnapshot.cs`: Sample's snapshot, nobody else's. |
| `src/Modules/Sample/Shipping.Modules.Sample.Application/Ports/ISampleUnitOfWork.cs` | The port handlers will depend on. There is no host-wide `IUnitOfWork`. |
| `Dockerfile`, stage `bundles` | One `dotnet ef migrations bundle` per `Shipping.Modules.*.Infrastructure` project, found by convention. |

Then open `psql` in the database container and type the three lines of the `sql` block:

```bash
docker compose exec postgres psql -U shipping -d shipping
```

```sql
\dn
\dt sample.*
SELECT "MigrationId", "ProductVersion" FROM sample."__EFMigrationsHistory";
\q
```

Finally, run the migration runner again to show that it applies only what is missing:

```bash
docker compose start migrate
docker compose ps -a migrate
docker compose logs migrate
```

**Expected:**

```text
      List of schemas
  Name  |       Owner
--------+-------------------
 public | pg_database_owner
 sample | shipping

 Schema |         Name          | Type  |  Owner
--------+-----------------------+-------+----------
 sample | __EFMigrationsHistory | table | shipping

       MigrationId       | ProductVersion
-------------------------+----------------
 20261015081705_Baseline | 10.0.11

$ docker compose logs migrate        (the last lines, after the second run)
migrate-1  | No migrations were applied. The database is already up to date.
migrate-1  | Done.
```

Sample has no tables of its own yet, only its history. A new module adds a schema of its own next to `sample`.

Point out:

- The API never migrates (`Nothing_in_the_host_or_a_module_creates_or_migrates_the_database` checks it). In
  Development the `migrate` service does it before the API starts. In production each module's bundle is a
  deployment step ([persistence](../persistence.md#production)).
- No foreign key may cross a schema, and only the module's Infrastructure sees its context. Another module refers
  to Sample's rows by identifier only.

**Recover:**

| Symptom | Safe action |
|---|---|
| `psql` prints nothing and returns at once, but exit code 0 | Seen on the rehearsal VM: its Windows Docker CLI receives no output from `exec`. Run the same command from a Linux or WSL shell, or show [the rehearsal output](../testing/US-EN-1-T6-red-green.md#demo-2--module-persistence). |
| `role "shipping" does not exist` | `.env` sets another `POSTGRES_USER`: use that name with `-U` and `-d`. |
| `service "postgres" is not running` | Demo 1 is not up: `docker compose up -d --wait --wait-timeout 300`. |

## Demo 3 — Run the tests

**Why it matters:** the architecture rules are tests, not conventions. A reference from Domain to EF Core, from one
module to another, or a context that maps into another schema fails the build in CI. **Time:** about 2 minutes with
warm caches. Needs the .NET 10 SDK and a running Docker engine (for the integration tests).

```bash
dotnet restore Shipping.slnx
dotnet build Shipping.slnx -c Release --no-restore
dotnet test tests/Shipping.UnitTests -c Release --no-build
dotnet test tests/Shipping.ArchitectureTests -c Release --no-build
dotnet test tests/Shipping.IntegrationTests -c Release --no-build
dotnet test Shipping.slnx -c Release --no-build            # all three at once, as CI runs them
```

The commands are the same in PowerShell.

**Expected:** `Build succeeded.` with `0 Warning(s)` and `0 Error(s)` (warnings are errors in this repository), then
one `Passed!` line per project with `Failed: 0` and `Skipped: 0`. The counts grow with the code; the rehearsal's
are in the [T6 evidence](../testing/US-EN-1-T6-red-green.md#demo-3--tests).

| Project | What it proves | Docker |
|---|---|---|
| `tests/Shipping.UnitTests` | The real host builds; `/health` is mapped for `GET` only and needs no token; each module registers its own context and unit of work and no global `IUnitOfWork`. | No |
| `tests/Shipping.ArchitectureTests` | Layer and module references (evaluated with MSBuild), the module template generates and compiles, persistence rules (one context per module, own schema, no cross-schema foreign key, nothing migrates at startup), documentation checks. | No |
| `tests/Shipping.IntegrationTests` | Each module's migrations, schema and runtime context, and `GET /health` 200/503, against PostgreSQL 17.6 started by Testcontainers. | Yes |

`bash tests/compose/compose-smoke.sh` tests the Compose environment end to end in its own project, `shipping-smoke`,
on port 18000. It takes several minutes; mention it rather than run it live. It is verified on Linux and in CI.

**Recover:**

| Symptom | Safe action |
|---|---|
| Integration tests fail with `Docker is either not running or misconfigured` | Testcontainers cannot reach Docker from this shell: `docker info` in the same shell. A daemon on a non-default address needs `DOCKER_HOST`. Run the unit and architecture projects meanwhile. |
| `NU1101`, `NU1301` on restore | nuget.org is unreachable: `dotnet nuget list source`, proxy settings. |
| A test fails | Show the failure; do not edit the test. Rerun that project once. A second failure is a blocker for the checklist. |

## Demo 4 — Create a module in a disposable clone

**Why it matters:** adding a bounded context is mechanical and the same for everyone: four projects from
`templates/Module`, one reference and three lines in the host, one baseline migration. The tests pick the module up by
convention. **Time:** 6–8 minutes, most of it build and test. The module name `Walkthrough` is for the demonstration
only; it is never committed.

Everything happens in a fresh clone in a temporary directory, so the checkout you work in is not touched.
`DOTNET_CLI_HOME` keeps the template installation inside that clone too. Because a new `DOTNET_CLI_HOME` makes the
next `dotnet` command run the SDK's first-run step, `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` stops that step from
creating an HTTPS development certificate in your user's certificate store, and `DOTNET_NOLOGO=1` hides the welcome
text. `DOTNET_CLI_HOME` also moves the NuGet package cache into the clone, so its first restore downloads every package
again: allow for it on a slow connection.

```bash
DEMO="$(mktemp -d)/shipping-demo"
git clone https://github.com/parazit-IR/shipping.git "$DEMO"
cd "$DEMO"
export DOTNET_CLI_HOME="$DEMO/.dotnet-home" DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_NOLOGO=1
dotnet new install ./templates/Module
dotnet new shipping-module --name Walkthrough --output src/Modules/Walkthrough
dotnet sln Shipping.slnx add src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Api/Shipping.Modules.Walkthrough.Api.csproj
dotnet add src/Shipping.Platform.Api/Shipping.Platform.Api.csproj reference src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Api/Shipping.Modules.Walkthrough.Api.csproj
```

```powershell
$demo = Join-Path ([IO.Path]::GetTempPath()) "shipping-demo-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
git clone https://github.com/parazit-IR/shipping.git $demo
Set-Location $demo
$env:DOTNET_CLI_HOME = "$demo\.dotnet-home"; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"; $env:DOTNET_NOLOGO = "1"
dotnet new install ./templates/Module
dotnet new shipping-module --name Walkthrough --output src/Modules/Walkthrough
dotnet sln Shipping.slnx add src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Api/Shipping.Modules.Walkthrough.Api.csproj
dotnet add src/Shipping.Platform.Api/Shipping.Platform.Api.csproj reference src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Api/Shipping.Modules.Walkthrough.Api.csproj
```

Then edit `src/Shipping.Platform.Api/Program.cs` in the clone, next to Sample's three lines:

```csharp
using Shipping.Modules.Walkthrough.Api;
// before builder.Build():
builder.Services.AddWalkthroughModule(databaseConnection);
// after builder.Build():
app.MapWalkthroughModule();
```

Build, add the baseline migration, test. Adding a migration does not connect to a database, but the design-time
factory requires the variable to be set.

```bash
dotnet build Shipping.slnx
dotnet tool restore
export ConnectionStrings__PostgreSql="Host=localhost;Database=shipping"
dotnet ef migrations add Baseline --project src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Infrastructure --startup-project src/Shipping.Platform.Api --context WalkthroughDbContext --output-dir Persistence/Migrations
dotnet test Shipping.slnx
```

```powershell
dotnet build Shipping.slnx
dotnet tool restore
$env:ConnectionStrings__PostgreSql = "Host=localhost;Database=shipping"
dotnet ef migrations add Baseline --project src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Infrastructure --startup-project src/Shipping.Platform.Api --context WalkthroughDbContext --output-dir Persistence/Migrations
dotnet test Shipping.slnx
```

**Optional, 3 minutes: the new module in Compose.** In the clone, start the stack as its own Compose project on port
8001, so your `shipping` project and its volume are untouched. The `migrate` service bundles the new module without
any change to `compose.yaml` or the `Dockerfile`.

This project has a volume of its own (`shipping-walkthrough_postgres-data`), so a new password is right here, unlike in
Demo 1.

```bash
cp .env.example .env && sed -i "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 16)/" .env   # macOS: sed -i ''
echo "SHIPPING_HTTP_PORT=8001" >> .env
docker compose -p shipping-walkthrough up --build -d --wait --wait-timeout 300
docker compose -p shipping-walkthrough logs migrate
curl -i --fail --retry 15 --retry-delay 2 --retry-all-errors http://localhost:8001/health
docker compose -p shipping-walkthrough down -v --rmi local
```

```powershell
Copy-Item .env.example .env
(Get-Content .env) -replace '^POSTGRES_PASSWORD=.*', "POSTGRES_PASSWORD=$([guid]::NewGuid().ToString('N'))" | Set-Content .env
Add-Content .env 'SHIPPING_HTTP_PORT=8001'
docker compose -p shipping-walkthrough up --build -d --wait --wait-timeout 300
docker compose -p shipping-walkthrough logs migrate
curl.exe -i --fail --retry 15 --retry-delay 2 --retry-all-errors http://localhost:8001/health
docker compose -p shipping-walkthrough down -v --rmi local
```

Clean up: stop the build servers the demo started (they keep files of the clone open, and on Windows the delete then
fails with "Access ... is denied"), leave the clone and delete it, and unset the variables so later `dotnet` commands
in this shell use your normal settings. `dotnet build-server shutdown` stops only reusable build processes; your next
build starts them again.

```bash
dotnet build-server shutdown
cd ~ && rm -rf "$(dirname "${DEMO:?}")"
unset DOTNET_CLI_HOME DOTNET_GENERATE_ASPNET_CERTIFICATE DOTNET_NOLOGO ConnectionStrings__PostgreSql
```

```powershell
dotnet build-server shutdown
Set-Location $HOME; Remove-Item -Recurse -Force $demo
Remove-Item Env:DOTNET_CLI_HOME, Env:DOTNET_GENERATE_ASPNET_CERTIFICATE, Env:DOTNET_NOLOGO, Env:ConnectionStrings__PostgreSql
```

**Expected:**

```text
$ dotnet new install ./templates/Module
Success: ...\shipping-demo\templates\Module installed the following templates:
Template Name                 Short Name       Language  Tags
Shipping four-project module  shipping-module            Shipping/Module

$ dotnet new shipping-module --name Walkthrough --output src/Modules/Walkthrough
The template "Shipping four-project module" was created successfully.

src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Api/Shipping.Modules.Walkthrough.Api.csproj
src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Application/Shipping.Modules.Walkthrough.Application.csproj
src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Domain/Shipping.Modules.Walkthrough.Domain.csproj
src/Modules/Walkthrough/Shipping.Modules.Walkthrough.Infrastructure/Shipping.Modules.Walkthrough.Infrastructure.csproj

$ dotnet sln Shipping.slnx add .../Shipping.Modules.Walkthrough.Api.csproj
    (adds the Api project and the three projects it references)

$ dotnet ef migrations add Baseline ...
Done. To undo this action, use 'ef migrations remove'

Persistence/Migrations: <timestamp>_Baseline.cs, <timestamp>_Baseline.Designer.cs, WalkthroughDbContextModelSnapshot.cs
    (Up() is empty, as in Sample's Baseline: the module has no entities yet. Applying it creates the schema
     "walkthrough" with walkthrough."__EFMigrationsHistory", as for Sample in Demo 2.)

$ dotnet test Shipping.slnx
Passed!  - Failed: 0 ... Shipping.UnitTests.dll
Passed!  - Failed: 0 ... Shipping.IntegrationTests.dll
Passed!  - Failed: 0 ... Shipping.ArchitectureTests.dll

$ docker compose -p shipping-walkthrough logs migrate          (optional step, lines that matter)
migrate-1  | Applying /migrations/sample-efbundle
migrate-1  | Applying migration '20261015081705_Baseline'.
migrate-1  | Done.
migrate-1  | Applying /migrations/walkthrough-efbundle
migrate-1  | Applying migration '<timestamp>_Baseline'.
migrate-1  | Done.

$ docker compose -p shipping-walkthrough down -v --rmi local
 Volume shipping-walkthrough_postgres-data  Removed            (that project's volume, networks and images only)
```

The final rehearsal of these steps took 205 s in bash and 244 s in PowerShell from `git clone` to the deleted clone,
each including about 100–130 s for the optional Compose build, on a data-centre connection.

Point out: the template wrote `WalkthroughDbContext` (schema `walkthrough`), `IWalkthroughUnitOfWork`,
`AddWalkthroughModule` and `MapWalkthroughModule`; the architecture and integration tests now cover Walkthrough as
well as Sample without a line of test code changing. The only shared files a new module touches are
`Shipping.slnx`, `src/Shipping.Platform.Api/Shipping.Platform.Api.csproj` and `src/Shipping.Platform.Api/Program.cs`.
For a real module, use the [README](../../README.md#create-a-module) steps in your own checkout, only for an approved
bounded context.

**Recover:**

| Symptom | Safe action |
|---|---|
| `NETSDK1004: Assets file ... not found` from `dotnet ef` | The new projects were never restored: run `dotnet build Shipping.slnx`, then `dotnet ef` again. |
| `Set ConnectionStrings__PostgreSql ...` from `dotnet ef` | The variable is not set in this shell: set it as shown. |
| `Filename too long` on `git clone` (Windows) | The temporary path is too deep: use a short directory such as `C:\t6demo` for `$demo`. |
| `dotnet new install` ran before `DOTNET_CLI_HOME` was set, or reports a conflict | The clone's template was registered in your user-wide template store. Before deleting the clone, unset `DOTNET_CLI_HOME` and run `dotnet new uninstall <clone>/templates/Module` with the clone's full path, then set the variables and repeat. |
| Deleting the clone fails: `Access to the path '...Tasks.dll' is denied` (Windows) | A build server still holds a file of the clone's package cache: `dotnet build-server shutdown` with the clone's `DOTNET_CLI_HOME` still set, then delete again. |
| Anything else | Delete the clone and start Demo 4 again. If the variables were set before the first `dotnet` command, nothing outside the clone (and the `shipping-walkthrough` project, if you started it) changed. |

## Demo 5 — Failure: PostgreSQL unavailable

**Why it matters:** `GET /health` is readiness, not liveness. It reports whether the host can serve traffic, which
needs the database, and Traefik stops routing to an API that Docker reports unhealthy. Seeing both answers once
avoids misreading a Traefik `404` as a routing bug. **Time:** about 3 minutes, most of it waiting for Docker's
health checks; talk through Demo 3 while you wait. Needs Demo 1 running. It stops and starts one container of the
`shipping` project and deletes nothing.

```bash
docker compose stop postgres
curl -i http://localhost:8000/health        # at once: 503 Unhealthy, Server: Kestrel
docker compose ps api                       # still (healthy) for about the next 2 minutes
docker compose ps api                       # repeat until it says (unhealthy), about 2.5 minutes
curl -i http://localhost:8000/health        # now Traefik's own 404: the API is out of rotation
docker compose start postgres
curl -i --fail --retry 15 --retry-delay 2 --retry-all-errors http://localhost:8000/health
```

```powershell
docker compose stop postgres
curl.exe -i http://localhost:8000/health
docker compose ps api
docker compose ps api
curl.exe -i http://localhost:8000/health
docker compose start postgres
curl.exe -i --fail --retry 15 --retry-delay 2 --retry-all-errors http://localhost:8000/health
```

**Expected** (rehearsal timings):

| Since `stop postgres` | Through Traefik | `docker compose ps api` |
|---|---|---|
| 0 s to about 2 minutes | `HTTP/1.1 503 Service Unavailable`, `Server: Kestrel`, body `Unhealthy` | `Up ... (healthy)` |
| about 2.5 minutes (144 s in the rehearsal) | `HTTP/1.1 404 Not Found`, no `Server` header, body `404 page not found` | `Up ... (unhealthy)` |
| up to about 10 s after `start postgres` | Traefik's `404` a few more times, then `200 OK`, `Server: Kestrel`, `Healthy` | `Up ... (healthy)` |

Why `503` first and `404` later: the API answers `503` itself as soon as its database check fails (2 s timeout). Docker
marks the container `unhealthy` only after 20 failed probes 5 s apart, and from then on Traefik has no healthy
container to route to and answers `404` itself. Asked directly, Kestrel still answers `503` during that phase. The
rehearsal checked this from inside the container:

```bash
docker compose exec api bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080; printf "GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3; cat <&3'
```

```text
HTTP/1.1 503 Service Unavailable
Server: Kestrel

Unhealthy
```

The body is only the status word: no check name, host, connection string or exception.

**Recover:**

| Symptom | Safe action |
|---|---|
| `/health` stays `503` after `start postgres` | Wait up to 15 s: one pending connection attempt has to end first. Then `docker compose ps postgres` and `docker compose logs postgres`. |
| `/health` stays `404` for more than a minute after `start postgres` | `docker compose ps api`. If it is still `unhealthy`, `docker compose restart api`, then the retrying `curl` again (keeps the volume). |
| The direct `exec` check prints nothing | On the rehearsal VM the Windows Docker CLI loses `exec` output: run it from a Linux or WSL shell, or skip it. |

Alternative failure, not run live: a migration that fails stops `migrate` with a non-zero exit, and the API never
starts (`service "migrate" didn't complete successfully: exit 1`). The Compose smoke test covers it
(`test_a_failed_migration_prevents_the_api_from_starting`).

## Demo 6 — Parallel team development

**Why it matters:** five developers share one repository, one host and one database. The boundaries shown in Demos 2–4
are what let them work without waiting for each other. This part is a discussion with the team, using the
repository's own history as the example.

1. **One branch per task**, created from an up-to-date `main`: `git switch main`, `git pull --ff-only`,
   `git switch -c feat/<key>-<short-description>`. The history shows the pattern (`feat/SLS-125-database-efcore`,
   `feat/SLOS-127-health-endpoint`, `feat/us-en-1-t5-readme-quickstart`).
2. **One owner per module**, or an explicit agreement before two people change the same module. Who decides ownership
   is not written down anywhere in this repository yet. Proposal for the session to confirm or change: the Stream Lead
   decides, and the result goes into the [readiness checklist](team-readiness-checklist.md).
3. **Small pull requests**, one capability each.
4. **Tests before implementation**: write the failing test, record RED, make it pass, refactor. Each task so far keeps
   its evidence in `docs/testing/`.
5. **Independent review**: someone who did not write the change reviews it.
6. **No cross-module internals**: a module never references another module's projects; its context is `internal`.
   How modules call each other is not decided yet (`messaging: none`, no Contracts project): raise it before you need
   it.
7. **Migrations stay per module**: each module has its own snapshot and history, so two developers adding migrations
   to two modules never conflict. Two people adding migrations to the same module must agree on the order.
8. **CI before merging**: `.github/workflows/ci.yml` runs `build-and-test` and `compose-smoke` on every push and pull
   request.

Expect merge conflicts only in the files every module touches: `Shipping.slnx`,
`src/Shipping.Platform.Api/Shipping.Platform.Api.csproj` and `src/Shipping.Platform.Api/Program.cs`. They are one line
per module: keep both sides.

**What is not enforced, as far as could be seen:** on 2026-10-19 GitHub's public API, asked without credentials, reported
branch `main` as not protected and returned no rulesets for the repository or for `main`
([evidence](../testing/US-EN-1-T6-red-green.md#github-and-mp-core-checks)). Treat required reviews, required CI checks
and reviewer assignment as team agreements until the repository owner confirms what is configured; do not tell the
team they are enforced.
