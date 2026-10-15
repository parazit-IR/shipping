# US-EN-1 T1 test evidence

## RED — 2026-10-13

Command: `dotnet test tests/Shipping.ArchitectureTests/Shipping.ArchitectureTests.csproj -c Release --logger 'console;verbosity=normal'`

The first sandboxed attempt failed during NuGet restore with `NU1301` because `api.nuget.org` was denied; it was not counted as RED. After network permission, restore succeeded and 3 tests ran, 3 failed:

| Test | Actual failure | Cause |
|---|---|---|
| Solution_contains_one_host_and_four_sample_projects | `Shipping.slnx is missing` | No solution had been created. |
| Sample_project_references_point_inward_and_never_to_host | Missing `Shipping.Modules.Sample.Domain.csproj` | No module projects existed. |
| Template_can_generate_and_build_a_second_module | `Module template is missing` | No reusable template existed. |

The architecture-library assertion was added after the first RED run and is verified in the final GREEN run.

MP Core CLI 0.10.1 produced the scaffold using the requested generator options and `--skip-verify --non-interactive`. Its command exited 70 after file creation because the .NET template engine could not write `/home/kamali/.templateengine/packages.json` in this workspace. The generated host and framework guidance were inspected and adapted; no success status is attributed to that CLI command.

## GREEN

On 2026-10-13, `dotnet restore Shipping.slnx -m:1 -p:BuildInParallel=false --verbosity minimal` succeeded: all seven projects restored or were up to date.

`dotnet build Shipping.slnx -c Release --no-restore -m:1 -p:BuildInParallel=false` succeeded with 0 warnings and 0 errors. This compiled the host, all four Sample projects, and both test projects.

`dotnet test Shipping.slnx -c Release --no-build -m:1 -p:BuildInParallel=false --logger 'console;verbosity=normal'` succeeded: 4 architecture tests passed, 1 unit test passed, 0 failed. The template test generated `TestModule`, built its four projects, and registered all four in a temporary `.slnx`.

## REFACTOR

The MP Core generated PostgreSQL context, migration design-time factory, broker configuration, and probe files were excluded from this T1 skeleton. The generated framework guides and canonical AI skills were retained. The host uses `MPCore.Hosting` 0.10.1; Domain and Application projects use the corresponding MP Core packages. Module registration is explicit and contains no business endpoint. `git diff --check` succeeded.

One unqualified `dotnet test` invocation hit the local sandbox's named-pipe restriction (`MSB1025`, socket permission denied) during parallel MSBuild startup. The serial command above passed. This was an execution-environment failure, not a test assertion failure.

After the final architecture assertion was added, a sandboxed VSTest run was also aborted when it could not bind its local test socket. The same serial command was rerun with network/socket permission and passed again: 4 architecture tests and 1 unit test, 0 failed.

GitHub Actions has not executed on these uncommitted changes. No CI-green claim is made.
