# Getting started

This repository is the US-EN-1 T1 .NET 10 modular monolith foundation generated from MP Core
`0.10.1`. It has a REST host and a four-project Sample module, with no business behavior yet.
Read `.mpcore/template-manifest.json` for the recorded shape, transport, messaging, and MP Core version.

## T1 prerequisites and commands

Install the .NET 10 SDK and make the NuGet feed for `MPCore.*` packages available. No database,
message broker, or identity provider is needed to build and test T1.

From the repository root:

```bash
dotnet restore Shipping.slnx
dotnet build Shipping.slnx -c Release
dotnet test Shipping.slnx -c Release
```

The solution has `Shipping.Platform.Api`, four `Sample` projects, `Shipping.UnitTests`, and
`Shipping.ArchitectureTests`. The tests check the project dependency rules, module template, and
registration surface. Sample has no business endpoints.

To start the current host locally:

```bash
dotnet run --project src/Shipping.Platform.Api/Shipping.Platform.Api.csproj
```

`Program.cs` adds the MP Core foundation, calls `AddSampleModule`, and calls `MapSampleModule`.
`MapSampleModule` currently maps nothing. The host has no T1 database, authentication, health,
OpenAPI, or migration setup. The current `appsettings.json` contains only observability and logging
settings; `appsettings.Development.json` is empty. Do not treat this scaffold as a deployed API.

## Create another technical module

```bash
dotnet new install ./templates/Module
dotnet new shipping-module --name Probe --output src/Modules/Probe
dotnet sln Shipping.slnx add src/Modules/Probe/Shipping.Modules.Probe.*/*.csproj
```

Add a host `ProjectReference` to `Shipping.Modules.Probe.Api`, then call `AddProbeModule()` before
`builder.Build()` and `MapProbeModule()` after it in `src/Shipping.Platform.Api/Program.cs`. The template
creates Domain, Application, Infrastructure, and Api projects. See
[ADR-001](architecture/ADR-001-four-project-modules.md) for their dependency rules. Registering
handlers, persistence mappings, or resources becomes relevant when those capabilities exist.

## Later work

T2–T6 may add persistence, migrations, security, health probes, deployment infrastructure, and
business capabilities according to their own accepted requirements. The retained MP Core guides under
`docs/` and `.mpcore/skills/` describe patterns for such work; they do not mean those features are
implemented in T1. Do not add another transport or message broker without an approved scope change.

For the workflow, see [development workflow](development-workflow.md) and [AI skills](ai-skills.md).
