# ADR-001: Four projects per module

Status: Accepted for US-EN-1 T1.

## Context

MP Core 0.10.1 generates modular-monolith modules as one main project with Domain, Application, and Infrastructure folders. T1 explicitly requires separate Domain, Application, Infrastructure, and Api projects. The generator's host also includes PostgreSQL persistence wiring, which belongs to a later task here.

## Decision

Each Shipping module owns four projects under `src/Modules/<Name>`. Domain has no project references. Application may reference Domain. Infrastructure references Application to implement its ports when ports exist. Api references Application and Infrastructure to expose one service and endpoint registration entry point. The executable host references each module's Api project and calls both entry points explicitly. No module contains `Program.cs`.

The local `dotnet new shipping-module` template creates these projects. MP Core packages remain pinned to 0.10.1; no framework source is copied. The host uses `MPCore.Hosting` for its foundation. Sample contains no business types, adapters, or endpoints.

## Consequences

Compiler boundaries make inward dependencies visible, at the cost of four project files and explicit registration per module. Five developers can own separate module directories and add their own registration lines to the host and solution. Cross-module calls must use an approved contract or message; no module references another module's Infrastructure project. Database mapping and handler discovery registration will be added when a later task introduces those capabilities.
