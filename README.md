# Shipping Platform

US-EN-1 T1 provides a .NET 10 modular monolith skeleton on MP Core 0.10.1. The only executable is `src/Shipping.Platform.Api`. `Sample` demonstrates a four-project module without business behavior.

US-EN-1 T2 adds PostgreSQL persistence through MP Core's EF Core package. Each module owns its own runtime MP Core DbContext and unit of work, its schema, its migrations and its migration history, all in one database. See [persistence](docs/persistence.md) and [ADR-002](docs/architecture/ADR-002-module-persistence.md).

## Build and test

```bash
dotnet restore Shipping.slnx
dotnet build Shipping.slnx -c Release --no-restore
dotnet test Shipping.slnx -c Release --no-build
```

`Shipping.IntegrationTests` starts PostgreSQL 17.6 in a container through Testcontainers, so it needs a running Docker engine. To run the host, set `ConnectionStrings:PostgreSql` in user secrets or the environment; no connection string is committed.

## Run locally with Docker Compose

US-EN-1 T3 adds the local environment: PostgreSQL, the API and Traefik, with each module's migrations applied before the API starts.

```bash
cp .env.example .env          # set POSTGRES_PASSWORD
docker compose up --build     # http://localhost:8000 through Traefik
docker compose down           # the database volume is kept
```

See [local environment](docs/local-environment.md) for services, configuration, persistence and limits. There is no health endpoint yet (T4).

## Create a module

```bash
dotnet new install ./templates/Module
dotnet new shipping-module --name Probe --output src/Modules/Probe
dotnet sln Shipping.slnx add src/Modules/Probe/Shipping.Modules.Probe.*/*.csproj
```

Add a project reference from `src/Shipping.Platform.Api` to the new module's Api project. In `Program.cs`, call `AddProbeModule(databaseConnection)` before `Build()` and `MapProbeModule()` after it. Then add the module's baseline migration, as described in [persistence](docs/persistence.md#adding-persistence-to-a-new-module). Keep handlers and adapters within that module. Add handler discovery when a later task introduces handlers.

The template has Domain, Application, Infrastructure, and Api projects. [ADR-001](docs/architecture/ADR-001-four-project-modules.md) records the dependency rules and how this differs from MP Core's default module layout. [Test evidence](docs/testing/US-EN-1-T1-red-green.md) records the actual RED and GREEN runs.

The generated MP Core guides and canonical AI skills are retained under `docs/` and `.mpcore/skills/`. Their descriptions of persistence and business work apply to later tasks, not this skeleton.
