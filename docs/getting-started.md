# Getting started

This repository is the US-EN-1 T1 .NET 10 modular monolith foundation generated from MP Core
`0.10.1`. It has a REST host and a four-project Sample module, with no business behavior yet.
Read `.mpcore/template-manifest.json` for the recorded shape, transport, messaging, and MP Core version.

## T1 prerequisites and commands

Install the .NET 10 SDK and make the NuGet feed for `MPCore.*` packages available. Building needs no
database, message broker or identity provider. Since T2 the tests need a running Docker engine, because
the integration tests start their own PostgreSQL.

From the repository root:

```bash
dotnet restore Shipping.slnx
dotnet build Shipping.slnx -c Release
dotnet test Shipping.slnx -c Release
```

The solution has `Shipping.Platform.Api`, four `Sample` projects, `Shipping.UnitTests`,
`Shipping.ArchitectureTests` and `Shipping.IntegrationTests`. The tests check the project dependency
rules, the module template, the registration surface, the persistence rules and the module migrations
against a real PostgreSQL. Sample has no business endpoints and no entities.

To start the current host locally:

```bash
dotnet run --project src/Shipping.Platform.Api/Shipping.Platform.Api.csproj
```

`Program.cs` adds the MP Core foundation and MP Core's aggregate event sink. It then calls
`AddSampleModule(databaseConnection)`, which registers Sample's own DbContext (T2), and `MapSampleModule`. `MapSampleModule` currently
maps nothing. The host needs `ConnectionStrings:PostgreSql` and stops at startup without it. Set it with
`dotnet user-secrets` or the `ConnectionStrings__PostgreSql` environment variable; no connection string is
committed. The host never migrates the database. Outside Compose, apply each module's migrations as described
in [persistence](persistence.md). `docker compose up` (T3) applies them automatically before the API starts; see
[local environment](local-environment.md). `GET /health` (T4) reports readiness; see
[local environment](local-environment.md#health-endpoint). There is still no authentication or OpenAPI setup. Do not
treat this scaffold as a deployed API.

`Shipping.IntegrationTests` needs a running Docker engine: it starts PostgreSQL 17.6 through Testcontainers.

## Create another technical module

```bash
dotnet new install ./templates/Module
dotnet new shipping-module --name Probe --output src/Modules/Probe
dotnet sln Shipping.slnx add src/Modules/Probe/Shipping.Modules.Probe.*/*.csproj
```

Add a host `ProjectReference` to `Shipping.Modules.Probe.Api`, then call `AddProbeModule(databaseConnection)` before
`builder.Build()` and `MapProbeModule()` after it in `src/Shipping.Platform.Api/Program.cs`. The template
creates Domain, Application, Infrastructure, and Api projects. See
[ADR-001](architecture/ADR-001-four-project-modules.md) for their dependency rules. Persistence is
already generated: the module has its own DbContext and schema; add a baseline migration
([persistence](persistence.md#adding-persistence-to-a-new-module)). Registering handlers or resources
becomes relevant when those capabilities exist.

## Later work

T2 added persistence and migrations ([persistence](persistence.md)). T3–T6 may add security, health
probes, deployment infrastructure, and
business capabilities according to their own accepted requirements. The retained MP Core guides under
`docs/` and `.mpcore/skills/` describe patterns for such work; they do not mean those features are
implemented in T1. Do not add another transport or message broker without an approved scope change.

For the workflow, see [development workflow](development-workflow.md) and [AI skills](ai-skills.md).
