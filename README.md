# Shipping Platform

US-EN-1 T1 provides a .NET 10 modular monolith skeleton on MP Core 0.10.1. The only executable is `src/Shipping.Platform.Api`. `Sample` demonstrates a four-project module without business behavior.

## Build and test

```bash
dotnet restore Shipping.slnx
dotnet build Shipping.slnx -c Release --no-restore
dotnet test Shipping.slnx -c Release --no-build
```

## Create a module

```bash
dotnet new install ./templates/Module
dotnet new shipping-module --name Probe --output src/Modules/Probe
dotnet sln Shipping.slnx add src/Modules/Probe/Shipping.Modules.Probe.*/*.csproj
```

Add a project reference from `src/Shipping.Platform.Api` to the new module's Api project. In `Program.cs`, call `AddProbeModule()` before `Build()` and `MapProbeModule()` after it. Keep handlers and adapters within that module; add handler discovery and persistence mapping when later tasks introduce them.

The template has Domain, Application, Infrastructure, and Api projects. [ADR-001](docs/architecture/ADR-001-four-project-modules.md) records the dependency rules and how this differs from MP Core's default module layout. [Test evidence](docs/testing/US-EN-1-T1-red-green.md) records the actual RED and GREEN runs.

The generated MP Core guides and canonical AI skills are retained under `docs/` and `.mpcore/skills/`. Their descriptions of persistence and business work apply to later tasks, not this skeleton.
