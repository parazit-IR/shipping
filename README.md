# Shipping Platform

The backend of the Shipping Platform: a .NET 10 modular monolith built on MP Core 0.10.1. This page takes a new
developer from `git clone` to a running local environment, a green test run and a new module.

## Overview

- **One process, several modules.** The only executable is `src/Shipping.Platform.Api`, an ASP.NET Core host.
  Every module runs inside it. A module lives in `src/Modules/<Name>` as four projects (Domain, Application,
  Infrastructure, Api), and the host registers each one explicitly
  ([ADR-001](docs/architecture/ADR-001-four-project-modules.md)).
- **MP Core is a dependency, not source.** It comes from nuget.org as `MPCore.*` packages, pinned to 0.10.1. The
  choices made when this repository was generated (modular monolith, REST, no messaging) are recorded in
  `.mpcore/template-manifest.json`.
- **One database, one schema per module.** Each module owns its EF Core context, unit of work, PostgreSQL schema
  and migration history. No transaction spans two modules
  ([ADR-002](docs/architecture/ADR-002-module-persistence.md)).
- **What exists today (M0 foundation, US-EN-1).** The skeleton and module template, PostgreSQL persistence,
  the Docker Compose environment, and the `GET /health` readiness endpoint. `Sample` is a technical module with
  no entities and no endpoints. There are **no business workflows, no business endpoints and no authentication
  yet**.

## Prerequisites

| To… | You need |
|---|---|
| Run the [Quickstart](#quickstart) (Docker only) | Git; Docker Engine or Docker Desktop with Compose v2 (`docker compose version`); internet access to pull images from Docker Hub and `mcr.microsoft.com` and NuGet packages from nuget.org (the images build the host); port `8000` free on `127.0.0.1`; `curl` 7.71 or later (`curl --version`; Linux Mint 20 and Ubuntu 20.04 ship 7.68). **No .NET SDK.** On Linux, your user must be allowed to use Docker (see [Troubleshooting](#troubleshooting)). |
| Build and test ([Build and test](#build-and-test)) | .NET 10 SDK (`dotnet --list-sdks`); access to nuget.org; a running Docker engine, because the integration tests start PostgreSQL through Testcontainers; `bash` on Linux for the Compose smoke test (macOS untested). |

Commands are shown for bash on Linux (the primary path, also macOS and Git Bash). Windows PowerShell versions
are given where a command differs.

## Quickstart

Starts PostgreSQL, applies every module's migrations, starts the API and Traefik, and checks `GET /health`.

**1. Clone and configure.** `.env` is git-ignored: the password you put there never leaves your machine.

```bash
git clone https://github.com/parazit-IR/shipping.git
cd shipping
cp .env.example .env
```

Open `.env` and set `POSTGRES_PASSWORD` to a local development password of your own (not a real one; no `;`).
Or let the shell generate one:

```bash
sed -i "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 16)/" .env   # GNU sed; on macOS: sed -i ''
```

**2. Start and check.**

```bash
docker compose config --quiet                            # no output when compose.yaml and .env are valid
docker compose up --build -d --wait --wait-timeout 300   # builds the images, returns once every service is healthy
docker compose ps                                        # postgres and api (healthy), traefik Up; -a also lists migrate, Exited (0)
curl -i --fail --retry 15 --retry-delay 2 --retry-all-errors http://localhost:8000/health
```

Expected answer, from the API itself (`Server: Kestrel`), among other headers:

```text
HTTP/1.1 200 OK
Cache-Control: no-store, no-cache
Content-Type: text/plain
Server: Kestrel

Healthy
```

For a few seconds after `up` returns, Traefik can answer its own `404 page not found` (no `Server: Kestrel`)
until it has picked up the healthy API container. The `curl` above retries for at most about 30 seconds. With a
curl older than 7.71, which lacks `--retry-all-errors`, leave that option out and rerun the command a few times. If
`up` fails or `curl` gives up, look at `docker compose ps -a` and `docker compose logs <service>`, then at
[Troubleshooting](#troubleshooting).

Windows PowerShell: the same steps. Use `curl.exe`, because `curl` is an alias of `Invoke-WebRequest` in Windows
PowerShell 5.1.

```powershell
git clone https://github.com/parazit-IR/shipping.git
cd shipping
Copy-Item .env.example .env
(Get-Content .env) -replace '^POSTGRES_PASSWORD=.*', "POSTGRES_PASSWORD=$([guid]::NewGuid().ToString('N'))" | Set-Content .env
docker compose config --quiet
docker compose up --build -d --wait --wait-timeout 300
docker compose ps
curl.exe -i --fail --retry 15 --retry-delay 2 --retry-all-errors http://localhost:8000/health
```

**3. Stop.**

```bash
docker compose logs -f api   # follow the API's log (Ctrl+C to stop following)
docker compose down          # removes the containers and networks; the database volume and its data stay
```

The next `docker compose up` reuses the volume and applies only migrations that are missing. Deleting the volume
is not part of normal cleanup; see [Troubleshooting](#troubleshooting) for the one case that needs it.

**How long it takes.** Measured on 2026-10-19 on a Windows Server 2025 VM (8 vCPU, Docker Engine 27.5.1 in WSL 2,
Compose v2.32.4, a data-centre connection): 111 s from `git clone` to `200 Healthy` with no .NET or Traefik image
and no build cache present (the PostgreSQL image was already pulled), and 26 s with a warm cache. It has not yet
been timed on Linux Mint or on a home connection, where the first image and package download takes longer. See
[US-EN-1 T5 evidence](docs/testing/US-EN-1-T5-red-green.md).

The environment, its services and its settings are described in [local environment](docs/local-environment.md).

## Build and test

Needs the .NET 10 SDK and a running Docker engine. These are the commands CI runs:

```bash
dotnet restore Shipping.slnx
dotnet build Shipping.slnx -c Release --no-restore
dotnet test Shipping.slnx -c Release --no-build
```

| Project | What it checks | Needs Docker |
|---|---|---|
| `tests/Shipping.UnitTests` | Host composition, module registration, the health endpoint's composition and answers, unit-of-work registration. | No |
| `tests/Shipping.ArchitectureTests` | Project dependency rules between layers and modules, the module template (generated and compiled in a temporary directory), persistence rules, and the documentation checks for this README. | No |
| `tests/Shipping.IntegrationTests` | Each module's migrations, schema and runtime context, and `GET /health`, against PostgreSQL 17.6 started by Testcontainers. | Yes |

Run one project with `dotnet test tests/Shipping.UnitTests -c Release`, for example.

The local environment has its own smoke test. It builds and starts `compose.yaml` as a separate Compose project,
`shipping-smoke`, on port 18000 with a generated password, checks services, routing, migrations, `GET /health`
and data persistence, and then removes only that project's containers, networks, volume and images. It leaves
your `shipping` project and `.env` alone. It is verified on Linux (CI and a Linux container). It is not verified
in Git Bash on Windows: on one Windows machine the Docker CLI there received no container output and 7 checks failed
([T5 evidence](docs/testing/US-EN-1-T5-red-green.md#green-and-regression)).

```bash
bash tests/compose/compose-smoke.sh
```

CI (`.github/workflows/ci.yml`) runs the `dotnet` commands above in the `build-and-test` job and the smoke test
in the `compose-smoke` job.

## Architecture

```text
Client (curl, browser)
  │  http://localhost:8000, bound to 127.0.0.1 only
  ▼
traefik            routes Host(localhost) to the api container while Docker reports it healthy
  │  edge network
  ▼
api                one ASP.NET Core process: src/Shipping.Platform.Api
  │                  ├── Sample module    (Domain, Application, Infrastructure, Api)
  │                  └── <next module>    (same four projects, registered in Program.cs)
  │  backend network (internal: no route out, no proxy on it)
  ▼
postgres           PostgreSQL 17.6: one database, one schema and migration history per module
  ▲
  │  once postgres is healthy, before api starts
migrate            one EF Core migration bundle per module, applied in turn; then exits
```

`docker compose up` starts the services in that order: `api` starts only if `migrate` exits with 0. The host
itself never migrates the database. All modules are deployed together in the one `api` process. They talk to each
other only through approved contracts or messages, never through another module's internals or tables. No
message broker is configured (`messaging: none` in the manifest).

## Create a module

Create a module only for an approved bounded context. A new module has its own schema and migrations and needs
no change to `compose.yaml` or the `Dockerfile`. The example name `Booking` stands for yours.

**1. Generate the four projects and add them to the solution.**

```bash
dotnet new install ./templates/Module
dotnet new shipping-module --name Booking --output src/Modules/Booking
dotnet sln Shipping.slnx add src/Modules/Booking/Shipping.Modules.Booking.Api/Shipping.Modules.Booking.Api.csproj
dotnet add src/Shipping.Platform.Api/Shipping.Platform.Api.csproj reference src/Modules/Booking/Shipping.Modules.Booking.Api/Shipping.Modules.Booking.Api.csproj
```

The template creates `Shipping.Modules.Booking.Domain`, `Shipping.Modules.Booking.Application`,
`Shipping.Modules.Booking.Infrastructure` (with `BookingDbContext` in schema `booking`) and
`Shipping.Modules.Booking.Api`. Adding the Api project to the solution also adds the three projects it references.

**2. Register the module in the host.** In `src/Shipping.Platform.Api/Program.cs`, next to Sample's lines:

```csharp
using Shipping.Modules.Booking.Api;
// before builder.Build():
builder.Services.AddBookingModule(databaseConnection);
// after builder.Build():
app.MapBookingModule();
```

**3. Add its baseline migration.** Build first: `dotnet ef` needs the new projects restored. The design-time
factory requires `ConnectionStrings__PostgreSql` to be set, but adding a migration does not connect.

```bash
dotnet build Shipping.slnx
dotnet tool restore
export ConnectionStrings__PostgreSql="Host=localhost;Database=shipping"
dotnet ef migrations add Baseline --project src/Modules/Booking/Shipping.Modules.Booking.Infrastructure --startup-project src/Shipping.Platform.Api --context BookingDbContext --output-dir Persistence/Migrations
```

In PowerShell, set the variable with `$env:ConnectionStrings__PostgreSql = "Host=localhost;Database=shipping"`;
the other commands are the same.

**4. Test it.** `dotnet test Shipping.slnx` finds the module by convention: the architecture tests check its
project dependencies and persistence rules, and the integration tests apply its migrations to their own schema.
`docker compose up --build` then migrates and runs it too.

Business code inside the module follows [development workflow](docs/development-workflow.md) and the skills in
`.mpcore/skills/`. Persistence details: [persistence](docs/persistence.md#adding-persistence-to-a-new-module).

## Health endpoint

`GET /health` is a **readiness** check: can the host serve traffic, which needs its database? Each module checks
that its own context can connect to PostgreSQL. It needs no token and is the only endpoint the host maps; there
are no business endpoints and no separate liveness endpoint.

| Answer | Meaning |
|---|---|
| `200` `Healthy`, `Server: Kestrel` | Every module reaches PostgreSQL. |
| `503` `Unhealthy`, `Server: Kestrel` | At least one module cannot reach PostgreSQL within 2 s. |
| `404 page not found`, no `Server` header | Traefik's own answer: the API is not (yet) healthy in Docker, so it is out of rotation, or the request's host is not `SHIPPING_HOST` (default `localhost`). |

The body is the status word only. Details, including what happens while the database is down:
[local environment](docs/local-environment.md#health-endpoint).

## Troubleshooting

Start with `docker compose ps -a` (state and health of each service) and `docker compose logs <service>`.

| Symptom | Cause and fix |
|---|---|
| `Cannot connect to the Docker daemon` or `error during connect` | Docker is not running. Start Docker Desktop, or on Linux `sudo systemctl start docker`. `docker info` must work before anything else. |
| Linux: `permission denied while trying to connect to the Docker daemon socket` | Your user may not use Docker. Prefix the commands with `sudo`, or add yourself to the `docker` group (`sudo usermod -aG docker $USER`, then log in again); that group is equivalent to root on the machine. |
| `required variable POSTGRES_PASSWORD is missing a value` | `.env` is missing or its `POSTGRES_PASSWORD` is empty. See Quickstart step 1. |
| `Bind for 127.0.0.1:8000 failed: port is already allocated` | Something else uses port 8000 (find it with `ss -ltnp` on Linux, `Get-NetTCPConnection -LocalPort 8000` in PowerShell). Set `SHIPPING_HTTP_PORT=8001` in `.env` and use that port in the `curl` command. |
| `postgres` is not healthy | `docker compose logs postgres`. A full disk or a damaged volume shows there. |
| `service "migrate" didn't complete successfully: exit 1` | `docker compose logs migrate`. The API does not start until every migration succeeds. |
| `migrate` log shows `28P01: password authentication failed for user "shipping"` | `POSTGRES_PASSWORD` was changed after the volume was created; PostgreSQL keeps the first one. Put the old value back in `.env`. If the local data is disposable, `docker compose down -v` deletes the volume and every row in it, and the next `up` starts empty with the new password. A `libgssapi_krb5.so.2` line just before the error is harmless. |
| `/health` answers `503 Unhealthy` | The API runs but cannot reach PostgreSQL: check `docker compose ps postgres` and `docker compose logs postgres`. Once PostgreSQL is back, `/health` returns `200` after the one pending connection attempt ends (up to 15 s). |
| `/health` answers `404 page not found` without `Server: Kestrel` | Traefik has no healthy API to route to: wait and retry, check `docker compose ps api`, and check that `SHIPPING_HOST` in `.env` is unset or `localhost`. |
| `dotnet restore` fails (`NU1101`, `NU1301`) | nuget.org is unreachable or blocked. `dotnet nuget list source` must list nuget.org; behind a proxy, set `HTTPS_PROXY`. The image build restores the same packages and fails the same way. |
| Integration tests fail with `Docker is either not running or misconfigured` | Testcontainers cannot reach Docker from the shell that runs `dotnet test`. Run `docker info` in that shell. A daemon on a non-default address needs `DOCKER_HOST`. |
| `git clone` on Windows: `Filename too long` | The checkout path is too deep for Windows. Clone into a short path such as `C:\src`, or run `git config --global core.longpaths true`. |
| PowerShell: `curl` rejects `-i` or `--retry` | `curl` is `Invoke-WebRequest` in Windows PowerShell. Type `curl.exe`. |

## Documentation

- [Getting started](docs/getting-started.md): running the host without Compose and the solution layout.
- [Local environment](docs/local-environment.md): Compose services, networks, configuration, the health endpoint
  in detail, smoke tests.
- [Persistence](docs/persistence.md): module contexts, migrations, production migration bundles.
- [Development workflow](docs/development-workflow.md): from requirement to shipped behaviour.
- [Architecture decisions](docs/architecture/): ADR-001 (four projects per module), ADR-002 (module persistence).
- [Modules](src/Modules/README.md): module rules and the upstream MP Core module guidance.
- [AI skills](docs/ai-skills.md) and `.mpcore/skills/`: the procedures assistants follow in this repository.
- Test evidence for each task: `docs/testing/`.
