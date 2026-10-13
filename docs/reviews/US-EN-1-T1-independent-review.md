# US-EN-1 T1 independent review — 2026-10-13

## T1 fix verification update — 2026-10-13

The findings below describe the original review state. The requested T1 fixes were independently
checked against current source and applied without changing the module or host's business behavior.
US-EN-1 T1 is **ready for review** based on the applicable local checks listed here. This does not
claim a remote CI run or the historical RED/GREEN chronology.

| Original finding | Current disposition and evidence |
|---|---|
| Architecture false negatives | Fixed in `tests/Shipping.ArchitectureTests/SkeletonTests.cs`. The test evaluates MSBuild `ProjectReference`, `PackageReference`, and `FrameworkReference` items for every module project, checks the host and module project graph for cycles, resolves project paths, and applies per-layer rules. A separate adversarial fixture also checks forbidden framework, package, cross-module, and cycle edges. |
| Host composition gap | The architecture suite checks compiled calls from the actual host assembly to `AddSampleModule` and `MapSampleModule`. Unit tests invoke those registration methods and resolve any DI descriptors they add. Sample currently adds zero service descriptors and maps zero endpoints. A generated `Probe` module is compiled with a disposable web host that references its Api project and calls both entry points. |
| Inaccurate getting-started guide | `docs/getting-started.md` now uses `Shipping.slnx`, names only existing T1 projects and settings, and separates T1 from potential T2–T6 work. The README's module example was corrected to use the template's actual `AddProbeModule` and `MapProbeModule` names. |
| Skill and ADR conflict | `AGENTS.md` now records the accepted four-project repository convention and points to ADR-001. The upstream MP Core skill was not edited. |
| Template process handling | The template test uses an isolated `DOTNET_CLI_HOME`, async reads of both streams, a three-minute process timeout, cancellation-aware process termination, exit-code assertions, and `finally` cleanup of its disposable directory. |

### Actual local commands and results after the fixes

| Command | Observed result |
|---|---|
| `dotnet restore Shipping.slnx` | Exit 0; all projects up to date. |
| `dotnet build Shipping.slnx -c Release` | Exit 0; seven projects built, zero warnings, zero errors. |
| `dotnet test Shipping.slnx -c Release` | Exit 0; 2/2 unit and 6/6 architecture tests passed, zero failed or skipped. |

During development, one sandboxed solution build exited 1 with no diagnostics. The same Release
build passed outside the sandbox; the final build and test commands above passed. Earlier template
test runs failed because the disposable host lacked implicit usings and because a `TestModule` name
was paired with the wrong generated method name. The corrected `Probe` fixture passed. These failures
are recorded as observed, not presented as historical TDD evidence.

### Isolated negative experiments

Two copies under `/tmp/shipping-t1-negative-Tode6C` were made from the working tree. No mutation
was applied to the real repository. Each invalid dependency compiled, then the targeted architecture
test exited 1 with the expected assertion:

| Mutation | Targeted failure |
|---|---|
| Sample Domain adds `FrameworkReference` to `Microsoft.AspNetCore.App` | `Sample.Domain: forbidden framework Microsoft.AspNetCore.App` |
| Sample Infrastructure adds `ProjectReference` to Other Api | `Sample.Infrastructure: cross-module reference to Other.Api` |

The host call test was also challenged in those copies. Removing `AddSampleModule()` produced an
assertion failure at line 58; removing `MapSampleModule()` produced one at line 60. The copies were
compiled successfully before those targeted tests. Initial isolated `dotnet test` attempts were
aborted because sandboxed VSTest could not open its local socket; reruns outside the sandbox produced
the assertion failures shown above.

### Remaining limitations

- Sample's registration methods currently add no service or endpoint. The tests prove host call
  sites and direct method invocation, and will resolve any future service descriptors, but there is
  no business endpoint or service behavior to assert in T1.
- The generated module is compiled into a disposable host; its host is not started. Runtime behavior
  for later business modules needs tests when those modules exist.
- Package rules inspect direct evaluated references. They do not audit transitive NuGet dependencies
  or every possible source-level API use in future modules.
- Remote CI and historical tests-first chronology remain unverified.

## Confirmed findings, ordered by severity

### HIGH — Architecture tests permit forbidden dependencies

- **Location:** `tests/Shipping.ArchitectureTests/SkeletonTests.cs:34-40,50-64`.
- **Problem:** Compiled checks look for selected namespace dependencies, while project-file checks omit `FrameworkReference` and `PackageReference` and permit cross-module references other than selected Infrastructure names. These checks do not enforce Domain's package boundary or the ban on references to another module's main projects.
- **Evidence:** In a disposable copy, adding `<FrameworkReference Include="Microsoft.AspNetCore.App" />` to Sample Domain still yielded **4/4 architecture tests passing**. After adding a second module, adding a `ProjectReference` from Sample Infrastructure to that module's Api also yielded **4/4 passing**. Both altered projects compiled. The working tree was not changed for these experiments.
- **Impact:** CI can be green while the principal module boundaries are broken.
- **Recommended fix:** Validate evaluated project and framework/package references against a per-layer allowlist for every module, resolve paths to project identities, reject cross-module main-project references, and retain compiled type checks for source-level dependencies.
- **Verification:** Run the same two mutations on disposable copies and require the targeted architecture test to fail for each; run the clean suite and require it to pass.

### MEDIUM — Registration tests do not exercise host composition

- **Location:** `tests/Shipping.UnitTests/ModuleRegistrationTests.cs:9-12`; `tests/Shipping.ArchitectureTests/SkeletonTests.cs:83-89`.
- **Problem:** The unit test checks only that `AddSampleModule` returns the same collection. The template test checks generated method names and a separate temporary solution, without compiling a host that references and invokes the new module. Neither test proves that `Program.cs` calls registration or maps endpoints.
- **Evidence:** The tests cited contain no host invocation or host project reference check. The actual host currently calls both Sample methods at `src/Shipping.Platform.Api/Program.cs:14,17`.
- **Impact:** A future omitted host registration could pass tests while the module is inert at runtime.
- **Recommended fix:** Add a host composition check or lightweight host startup test that verifies the module's service and endpoint registration path; make a generated-module integration test compile a disposable host after following the documented steps.
- **Verification:** Remove a registration call in a disposable copy and require the new test to fail.

### MEDIUM — Generated getting-started guidance describes absent T1 features

- **Location:** `docs/getting-started.md:31,48-50,55-80,101-118,139-165`.
- **Problem:** It names a nonexistent `Shipping.Platform.slnx`, test project, health test, persistence project, health paths, and migration factory, and describes database, broker, security, and configuration behavior absent from this T1 host. The actual solution is `Shipping.slnx`; the only host source is `Program.cs`.
- **Evidence:** Compare these claims with `Shipping.slnx`, `src/Shipping.Platform.Api/Program.cs`, the two appsettings files, and `rg --files src tests`.
- **Impact:** A reader following this guide will issue invalid commands and may assume unimplemented security or health behavior exists.
- **Recommended fix:** Mark generated guidance as future-state or revise it to the actual T1 scaffold. Keep later-task guidance separate from current operational instructions.
- **Verification:** Execute each documented T1 command from a clean checkout and compare each named file and endpoint to the tree/runtime.

### LOW — Repository skill and accepted ADR prescribe different module shapes

- **Location:** `.mpcore/skills/mpcore-implement-ddd-module/SKILL.md:12-14`; `docs/architecture/ADR-001-four-project-modules.md:3-13`.
- **Problem:** The canonical skill still instructs a one-project module for `modular-monolith`; the accepted T1 ADR and task require four projects. This is a guidance conflict, not a defect in the current four-project implementation.
- **Evidence:** Read both cited files and `.mpcore/template-manifest.json` (`shape: modular-monolith`).
- **Impact:** A later contributor following the skill could create a module with a different shape.
- **Recommended fix:** Record a repository-specific T1 exception in maintained guidance after owner review; do not change the four-project structure as part of this review.
- **Verification:** Confirm future module instructions lead to the same four projects generated by the template.

## Git hygiene

`.gitignore` was expanded using the [official VisualStudio.gitignore](https://github.com/github/gitignore/blob/main/VisualStudio.gitignore) as a reference, with rules for .NET output, IDE state, test/coverage/Stryker output, NuGet packages, local secrets, databases, logs, and OS metadata. It does not ignore all DLL, JSON, XML, YAML, SQL, or configuration files, or the entire `.mpcore` directory. `git check-ignore -v .idea/workspace.xml` reported `.gitignore:21:.idea/`. `git check-ignore` did not match the manifest, skills inventory, template metadata, appsettings files, solution, props, or CI workflow.

`git status --short` initially showed modified `README.md` and untracked T1 files, `.idea/`, and `shipping.zip`. After the ignore update, `.idea/` and `shipping.zip` disappeared from status. `git ls-files` lists **only `README.md`**. The requested bare `git ls-files --ignored --exclude-standard` returned exit 128 because Git requires `-o` or `-c` with `-i`; `git ls-files --ignored --exclude-standard -o` listed IDE files, the archive, and generated build output. **Incorrectly tracked generated files: none. Git index cleanup: none; no `git rm --cached` was needed.** Local files were preserved.

The two reachable commits contain only `README.md`. A history search for common key/password signatures returned no matching files; a current source/config filename and content search found no credential files or secret values. This is a bounded pattern search, not a complete secret audit. No historical exposure was found, so no credential rotation is indicated from the evidence reviewed.

## Independent architecture review

`Shipping.slnx` lists one web SDK host, four Sample projects, and two test projects. A search of `src/**/*.csproj` found exactly one `Microsoft.NET.Sdk.Web` project; the four module projects use `Microsoft.NET.Sdk`. `Program.cs` is the only host entry point and explicitly calls `AddMPCoreFoundation`, `AddSampleModule`, and `MapSampleModule`. The host references Sample Api and `MPCore.Hosting` 0.10.1. Domain references `MPCore.Domain` 0.10.1 and no project. Application references Domain and `MPCore.Application` 0.10.1. Infrastructure references Application. Module Api references Application and Infrastructure. The graph is acyclic and has no current cross-module edge. The module registration methods are empty scaffolding; no business rule, adapter, endpoint, database, broker, or second transport was found in current T1 source.

The four-project shape and direct `Api → Infrastructure` dependency are explicit ADR choices; they are not treated as violations. MP Core source is not copied into the project tree. Package resolution and Release compilation confirm the listed NuGet integration, but runtime framework behavior beyond this empty host was not exercised.

## Module template verification

In `/tmp/shipping-t1-review-negative-20261013`, `dotnet new install` and `dotnet new shipping-module --name AuditProbe` both exited 0 using an isolated `DOTNET_CLI_HOME`. Generated output had exactly four `.csproj` files and expected `Shipping.Modules.AuditProbe.*` namespaces and DLL names. `rg 'Sample'` over generated source/project files returned no matches. Project references were Application → Domain, Infrastructure → Application, and Api → Application plus Infrastructure; no circular path exists. `dotnet build` of the generated Api project in Release, serial mode, exited 0 with zero warnings/errors. The README's wildcard `dotnet sln Shipping.slnx add ...` exited 0 and added all four projects; its additional duplicate notices are harmless because dependent projects were added transitively. A serial Release build of that disposable solution exited 0 with zero warnings/errors.

No source file edits were needed to **create and compile** the module. Host adoption remains an explicit manual project reference plus two `Program.cs` calls per `README.md:21`; that documented composition step was inspected but not independently compiled for `AuditProbe`.

## Adversarial test review

| Test | What it verifies | Limit |
|---|---|---|
| `Solution_contains_one_host_and_four_sample_projects` | Solution entries, web SDK count, template metadata exists | Fixed Sample names; metadata existence says nothing about generation correctness. |
| `Compiled_layers_obey_dependency_direction` | Selected source-level dependencies in compiled Sample assemblies | Omits package/framework references and arbitrary cross-module assemblies; empty marker classes make parts vacuous. |
| `Sample_project_references_point_inward_and_never_to_host` | Some textual `ProjectReference` patterns | Does not resolve paths or enforce a complete graph policy; confirmed cross-module false negative. |
| `Template_can_generate_and_build_a_second_module` | Official engine generation, Release compile, four DLLs, method names, temporary solution additions | Does not inspect residual names/references or compile host adoption; installs template into shared test CLI home. |
| `Sample_module_registration_is_available_to_the_host` | Return value identity | Passes for a no-op method and does not prove host calls it. |

## Attempts to falsify correctness

| Assumption challenged | Method | Actual result |
|---|---|---|
| Domain cannot depend on web framework | Add `Microsoft.AspNetCore.App` framework reference to Domain in disposable copy, run architecture tests | Build and 4/4 tests passed: confirmed false negative. |
| Modules cannot reference another module's main project | Add Sample Infrastructure → AuditProbe Api project reference in disposable copy, run architecture tests | Build and 4/4 tests passed: confirmed false negative. |
| Template substitutions and paths work for another name | Generate `AuditProbe`, search for `Sample`, inspect references, compile Release | Four projects, no residual `Sample`, expected graph, 0-warning build. |
| README solution registration command works | Execute wildcard `dotnet sln add` on disposable solution, build it serially | Four projects added; solution build passed. |
| A green local run proves prior TDD chronology | Compare evidence file with reachable Git history | Current GREEN reproduced; RED/refactor chronology not established. |

## RED/GREEN evidence and local commands

`docs/testing/US-EN-1-T1-red-green.md` describes a RED run, later GREEN run, and refactor, but no preserved raw test output or commits containing those stages are available. Reachable Git history has only two README commits; all T1 code and the evidence document are untracked. **Tests-first, actual prior RED execution, production-code ordering, and refactor chronology: UNVERIFIED.** The current GREEN state is reproducible:

| Command | Exit | Observed outcome |
|---|---:|---|
| `dotnet restore` | 0 | All projects up to date. |
| `dotnet build -c Release` (sandbox) | 1 | No warnings or errors printed; likely local MSBuild execution restriction, cause not established. |
| `dotnet build -c Release` (outside sandbox) | 0 | Seven projects built, zero warnings/errors. |
| `dotnet test -c Release` | 0 | Unit: 1 passed; architecture: 4 passed; 0 failed/skipped. |

## CI review

`.github/workflows/ci.yml:9-18` uses `setup-dotnet@v4` with `10.0.x`, restores and builds `Shipping.slnx`, and tests that same solution including both test projects. Shell failures propagate under default GitHub Actions behavior; no `continue-on-error`, error-suppressing command, or Python dependency is present. The workflow inherits the test gaps above. **Remote GitHub Actions execution: NOT VERIFIED** because this work is uncommitted and no run was inspected.

## Claims that could not be independently verified

- Historical RED execution and test-first/refactor chronology.
- Remote CI outcome.
- Actual host adoption and runtime behavior for a newly generated module; only project/solution compilation was performed.
- Completeness of the secret scan against unknown credential formats or binary archives.
- Authentication behavior of the empty host at runtime; no protected business endpoint exists in T1 to probe.

## Residual risks and verdict

The currently clean dependency graph can regress without an architecture test failure, as both controlled mutations demonstrated. Generated project creation works, but host adoption lacks an executable regression test. Stale generated guidance may mislead operators. The untracked T1 files, including this report and `.gitignore`, will not be part of CI until separately staged and committed by the owner.

**Final verdict: PASS WITH ISSUES** for the present T1 scaffold and template; **architecture enforcement is inadequate** and TDD chronology remains **UNVERIFIED**. No production code was changed, and no commit, push, PR, or index cleanup was performed.
