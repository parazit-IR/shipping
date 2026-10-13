# US-EN-1 T1 final verification — 2026-10-13

## Scope and state

Branch: `feat/m0-modular-monolith-foundation`. The working tree already contained a modified `README.md` and untracked T1 files before this review. No commit, push, reset, or unrelated deletion was made. The manifest records `modular-monolith`, `rest`, `messaging: none`, and MP Core `0.10.1`. The host and module project references resolve `MPCore.Hosting`, `MPCore.Application`, and `MPCore.Domain` at `0.10.1` in `project.assets.json`. The repository's ADR-001 authorizes four projects per module despite the upstream `mpcore-implement-ddd-module` skill's one-project default.

Acceptance criterion: the T1 skeleton must build, compose the Sample module in the real host, generate a reusable four-project module, and enforce the stated dependency direction without adding business functionality.

## Confirmed issues before modification

1. `ModuleRegistrationTests.Sample_module_registration_can_be_resolved_through_di` passed with zero added service descriptors. Its `foreach` body never ran, so its name and assertion implied dependency resolution that did not occur. The pre-change focused test passed 2/2; this was a demonstrated false positive.
2. The existing host test only scanned IL for method calls. That proves references to `AddSampleModule` and `MapSampleModule`, but does not prove the host starts. The Sample module currently registers no services and maps no endpoints, so there is no service or route to resolve as behavioral proof of those calls.
3. The template test generated and compiled a second module but did not assert exact project count, references, or stale namespaces. Inspection found no confirmed defect in template output; this was a coverage gap.

## Changes and tests

Changed `tests/Shipping.UnitTests/ModuleRegistrationTests.cs`: the test now explicitly asserts that Sample adds zero descriptors and builds a provider with `ValidateOnBuild` and `ValidateScopes`. It no longer claims to resolve module services. The endpoint contract test remains; Sample has no endpoint to query.

Changed `tests/Shipping.UnitTests/Shipping.UnitTests.csproj`: added a host project reference and `Microsoft.AspNetCore.Mvc.Testing` 10.0.11. Added `tests/Shipping.UnitTests/HostCompositionTests.cs`: `WebApplicationFactory<Program>` starts the actual host, resolves `IServiceScopeFactory`, and handles an unmapped request with 404. This exercises host construction and the real top-level configuration path, without database or OIDC test overrides.

Changed `tests/Shipping.ArchitectureTests/SkeletonTests.cs`: the existing disposable template test now generates `Verification`, asserts exactly four projects and exact inward `ProjectReference` edges, checks generated C# for the new namespace and absence of `Shipping.Modules.Sample`, and compiles a disposable host that calls `AddVerificationModule` and `MapVerificationModule` in Release. The existing test also checks each output DLL.

No production source, template, or CI workflow was changed. This report is the only new documentation file.

## RED and GREEN evidence

The pre-change focused unit command exited 0 and reported `Total tests: 2, Passed: 2`, including the misleading DI test. Source inspection showed `AddSampleInfrastructure` returns the input collection unchanged, so the loop had zero iterations. This is RED evidence for the quality of the old assertion, although it is a green test process; no original T1 test-first chronology is claimed.

An initial sandbox attempt at that focused command exited 1 before tests ran because MSBuild could not create its named pipe (`SocketException (13): Permission denied`). The same command outside the sandbox produced the 2/2 result above. This was an environment failure, not a test assertion failure.

The first targeted template test run after the new assertions aborted in the sandbox because VSTest could not bind a local socket (`SocketException (13): Permission denied`). The identical command outside the sandbox exited 0 and passed 1/1. No assertion failure was observed in the new template or host tests, and there was no production defect to fix. Therefore a conventional failing-test RED phase for production code was **not demonstrated**. The corrected tests were green on their first executable run. This limitation is retained rather than inventing a RED result.

The focused unit run after changes exited 0, `Total tests: 3, Passed: 3`, including real-host startup. The targeted template run exited 0, `Total tests: 1, Passed: 1`.

## Full local verification

Commands were run from the repository root through the required `rtk proxy` wrapper, without changing their .NET arguments:

| Command | Exit | Relevant output |
|---|---:|---|
| `dotnet restore Shipping.slnx` | 0 | `All projects are up-to-date for restore.` |
| `dotnet build Shipping.slnx -c Release --no-restore` | 0 | `Build succeeded. 0 Warning(s). 0 Error(s).` Seven projects built. |
| `dotnet test Shipping.slnx -c Release --no-build --logger 'console;verbosity=normal'` | 0 | Unit: 3 passed. Architecture: 6 passed. Total discovered 9, passed 9, failed 0, skipped 0. |

The full test output included `Real_host_builds_and_serves_requests_with_its_configured_services`, `Compiled_host_calls_sample_service_and_endpoint_registration`, `All_module_project_dependencies_obey_boundaries`, `Evaluated_references_reject_adversarial_edges`, and `Template_can_generate_and_compile_a_disposable_host`, all passed. The full build had no warnings or errors. The template test used an isolated `DOTNET_CLI_HOME` and deleted its generated directory on completion.

## Attempts to falsify correctness

| Claim | Experiment and observation | Status |
|---|---|---|
| Sample DI registration resolves dependencies | Before correction, the test passed even though the registration method added zero descriptors. This falsified the old test's stated claim. The replacement asserts zero explicitly and only validates the provider construction contract. | VERIFIED as empty; dependency resolution is not applicable |
| Host is correctly composed | `WebApplicationFactory<Program>` started the real host and served a request; the compiled-call test found both Sample calls. Since both methods currently have no observable module effect, runtime invocation of each cannot be independently observed. | VERIFIED for startup; NOT VERIFIED behaviorally for each no-op call |
| Architecture rules detect forbidden edges | The existing `Evaluated_references_reject_adversarial_edges` test copies the Sample projects, adds forbidden Domain framework/package and cross-module project edges, then a cycle. It passed only after asserting each violation was reported. | VERIFIED for those mutations |
| Template is reusable | The test generated `Verification` in a temporary directory, checked four project files and exact references, rejected stale Sample C# namespaces, and built all four DLLs and a disposable host in Release. | VERIFIED |
| Project builds independently | Restore and Release build passed from the repository root with zero warnings/errors. | VERIFIED locally |
| Tests detect regressions | Architecture mutation fixtures detect specified project-edge violations; the old DI test was proven vacuous and replaced. No mutation was run against the real host's no-op registration or mapping. | VERIFIED in part; host-call behavior limited |
| CI covers relevant projects | `.github/workflows/ci.yml` installs `10.0.x`, restores/builds/tests `Shipping.slnx`, uses Release for build/test, has no `continue-on-error`, and has no secrets or T2/T3 service dependency. Both test projects are in the solution. | VERIFIED statically; remote run NOT VERIFIED |

## CI and remaining limitations

The workflow needs no edit. `gh` is unavailable in this environment (`rtk: Failed to execute command: gh: No such file or directory`), so no authenticated branch run, commit SHA, or job result was inspected. **REMOTE CI NOT VERIFIED — changes have not been pushed or no workflow run is available.** YAML inspection is not a green CI run.

Sample is intentionally an empty scaffold. There are no module services to resolve and no module endpoints to request. The host startup test and compiled-call test together support composition, but they cannot provide an observable runtime marker for two no-op methods without adding artificial production behavior. NetArchTest checks compiled placeholder types; the stronger evaluated project-reference rules cover current layer edges. Future business types and infrastructure registrations require new behavioral tests when introduced.

## Recommendation

**READY FOR PR for US-EN-1 T1 local review**, with remote CI and the no-op invocation limitation explicitly unverified. The local definition of done is met; no T2–T6 or business capability was added.
