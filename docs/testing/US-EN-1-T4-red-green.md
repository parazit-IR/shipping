# US-EN-1 T4 (SLOS-127) test evidence

All runs took place on 2026-10-18 on branch `feat/SLOS-127-health-endpoint`, created from the SLS-126 commit
`37a0f54` (SLS-125 and SLS-126 are not yet merged to `main`). The environment and the reason runs happen in a
throwaway runner container on the Docker engine host are the same as for T3: see
[US-EN-1 T3](US-EN-1-T3-red-green.md#why-the-runs-happen-on-the-engine-host). The .NET suite ran in
`mcr.microsoft.com/dotnet/sdk:10.0.402`. The smoke tests first ran in `docker:27.5.1-cli` with `apk add bash curl`.
From 12:22Z the engine host could not reach the Alpine mirror (`Permission denied`; two attempts never reached the
application), so later smoke runs used the SDK image with the static `docker` CLI and its compose/buildx plugins
copied from `docker:27.5.1-cli`. The runner helper is a local workaround and is not committed. The evidence logs
are the runners' output with trailing whitespace removed and nothing else changed.

Contract under test: `GET /health` is readiness. `200 Healthy` when every module's own context reaches PostgreSQL,
`503 Unhealthy` otherwise, `GET` only, no token, status word only. See
[local environment](../local-environment.md#health-endpoint).

## Tests

| Suite | Tests added |
|---|---|
| `Shipping.UnitTests/HealthEndpointCompositionTests` | `/health` mapped on the real host, `GET` only, `AllowAnonymous`; one bounded check per module, none from the host; `POST /health` is `405` |
| `Shipping.IntegrationTests/HealthEndpointTests` (real PostgreSQL) | `200 Healthy`, `text/plain`, `no-store`, no token; nothing migrated or created on an empty database; `503 Unhealthy` while the database refuses connections and `200` once it accepts them; `503` within the timeout when opening a connection ignores cancellation; the request-scoped module context is never used; concurrent requests share one running connection attempt |
| `tests/compose/compose-smoke.sh` | the api container's health check is `GET /health`; `200 Healthy` from Kestrel through Traefik; with postgres stopped, `503 Unhealthy` from Kestrel for every request, and `200` after it starts again |

## RED

Tests only, no production change ([unit, Windows](evidence/US-EN-1-T4-red-unit-windows.txt),
[.NET](evidence/US-EN-1-T4-red-dotnet.txt), [smoke](evidence/US-EN-1-T4-red-smoke.txt)):

- Unit: exit 1, 3 failed / 9 passed. `Assert.Single()` found no `/health` endpoint; `POST /health` was `404`, not
  `405`; registrations were `[]`, not `["sample-database"]`.
- Integration: exit 1, 3 failed / 9 passed, each `Expected: OK` or `ServiceUnavailable`, `Actual: NotFound`.
  Architecture 21/21.
- Smoke: exit 1, 21 passed / 3 failed. The health check was still the TCP probe; `/health` through Traefik was
  `404` with `Server: Kestrel` (routed to the API, no endpoint).

## GREEN, first version

`MapHealthChecks("/health")` with `GET`-only metadata and `AllowAnonymous`; `AddHealthChecks()` in the host; a
`SampleDatabaseHealthCheck` (`CanConnectAsync`) registered by the module through `HealthCheckServiceOptions`; the
Compose health check changed to a bash `GET /health`.

The module first called `AddHealthChecks()` itself. Two existing SLS-125 unit tests then failed: they build a bare
module container with `ValidateOnBuild`, and the health check service needs host logging. The tests were not
changed; the module now adds only its registration and the host registers the service, as it does the time
provider. Then .NET 45/45 and smoke 24/24 ([.NET](evidence/US-EN-1-T4-green1-dotnet.txt),
[smoke](evidence/US-EN-1-T4-green1-smoke.txt)). Both are superseded by the fixes below.

## Defects found by the failure scenarios, and their fixes

1. **The 2 s timeout was not enforced.** A one-off scenario run
   ([log](evidence/US-EN-1-T4-failure-scenarios-before-fix.txt)) stopped postgres: the first request got `503` at
   once, later ones got no answer within curl's 5 s, and the API logged checks completing after 10-12 s. Opening a
   connection to the stopped container did not stop when the check's token was cancelled. The smoke test had only
   looked at the first answer.
   - RED: the smoke test now requires three further `503`s from Kestrel; it failed with no answer
     ([smoke](evidence/US-EN-1-T4-timeout-red-smoke.txt)). An integration test stalls connection opening for 10 s
     regardless of the token; it took 10.2 s ([.NET](evidence/US-EN-1-T4-timeout-red-dotnet.txt)).
   - First fix: `CanConnectAsync(ct).WaitAsync(ct)` on the request-scoped context. .NET passed, 46/46
     ([log](evidence/US-EN-1-T4-waitasync-green-dotnet.txt)).
2. **That fix produced a 500.** The smoke test failed ([log](evidence/US-EN-1-T4-waitasync-red-smoke.txt)): the
   scope disposed the context while its abandoned attempt was still connecting, Npgsql threw `Can't close,
   connection is in state Connecting` outside the check, and the Development exception page returned the stack
   trace and request headers through Traefik. The integration suite had not caught it, because its stall runs
   before the connection starts connecting.
   - Fix: the check runs each attempt on a context of its own, built from the module's options, and disposes it
     only when the attempt ends. Smoke then 24/24.
   - Guard: an integration test makes the request-scoped context throw if resolved. Run against the earlier
     design (patched into the runner copy only), it failed `Expected: OK, Actual: InternalServerError`, 1 failed /
     4 passed ([log](evidence/US-EN-1-T4-scoped-context-guard-red-dotnet.txt)).

3. **Abandoned attempts were not bounded.** A focused independent review, against the Npgsql 10.0.3 and EF Core
   10 sources, found that `CanConnectAsync` opens the context's own pooled connection. A running attempt takes a
   slot of the pool business traffic uses, from before it connects until it ends (up to 15 s). Every timed-out
   request left one more, so anonymous requests during an outage or a slow database could fill the 100 slots.
   - RED: five concurrent requests with connection opening stalled: all `503`, but `Expected: 1, Actual: 5`
     openings ([log](evidence/US-EN-1-T4-single-attempt-red-dotnet.txt)).
   - Fix: a singleton per module (`SampleDatabaseHealthCheck.Attempt`) holds the attempt still running; a check
     waits for it, bounded by its own timeout, instead of starting another. A per-container singleton, not a
     static, so test hosts in one process do not share an attempt. The repeated review approved it.

## REFACTOR

After the independent review: comments corrected (the check's root cause is stated as observed timing, not an
unproven DNS cause; lifetimes documented), the integration timing bound widened from 4 s to 6 s for a cold first
request, an always-true assertion and the assertion that no `/health/live` path exists removed, the smoke helper's
wording fixed. Behaviour unchanged.

## Final

The runs of the tree before fix 3 passed too (.NET 47/47, smoke 24/24, failure scenarios); they are kept as
superseded evidence: [.NET](evidence/US-EN-1-T4-pre-single-attempt-dotnet.txt),
[smoke](evidence/US-EN-1-T4-pre-single-attempt-smoke.txt),
[scenarios](evidence/US-EN-1-T4-failure-scenarios-pre-single-attempt.txt).

- .NET, final tree, exit 0, 0 warnings, 0 errors: Unit 12/12, Integration 15/15, Architecture 21/21 (48)
  ([log](evidence/US-EN-1-T4-final-dotnet.txt)).
- Smoke, final tree, exit 0: **Passed: 24, Failed: 0** ([log](evidence/US-EN-1-T4-final-smoke.txt)). The 21
  SLS-126 tests are unchanged and pass.
- Failure scenarios, final tree ([log](evidence/US-EN-1-T4-failure-scenarios-final.txt)). `Server: Kestrel`
  marks an answer from the API; none marks Traefik's own:

| Scenario | Through Traefik | Direct to Kestrel |
|---|---|---|
| Stack healthy | `200 Healthy`, Kestrel; `POST` `405`, Kestrel | `200`, 36 ms |
| postgres stopped | `503 Unhealthy`, Kestrel: 0.04 s, then 2.03, 2.01, 2.01, 2.02 s; 20 concurrent requests: all `503` in about 2 s | `503`, 2036 ms |
| postgres stopped about 2 min 20 s: api `unhealthy` in Docker | `404 page not found`, no `Server` (Traefik) | `503`, 2056 ms |
| postgres started again | `200 Healthy`, Kestrel, about 1 s after it was healthy | `200`, 22 ms |
| api stopped | `404`, no `Server` (Traefik) | — |
| api started again | `200 Healthy`, Kestrel | — |

  No `500`, exception page or `Can't close` in the API log. A timed-out check is logged by ASP.NET Core as
  "threw an unhandled exception after ~2000ms" and answered `503`. Right after `up --wait`, Traefik answered its
  own `404` for about 2 s before routing to the API.

## Not covered

- CI: the branch has not been pushed, so no GitHub Actions run exists yet.
- Npgsql logs `libgssapi_krb5.so.2: cannot open shared object file` once when it connects: the runtime image has
  no Kerberos library. Harmless; not changed here.
- No test waits for a stalled attempt to end and then checks recovery. Starting a fresh attempt after one has
  ended is covered by the refuse-then-accept test, whose attempts fail at once, and by the Compose recovery runs. The integration tests run with
  `Pooling=false`, so sharing the pool is covered through the attempt count, not observed in the pool itself.
- A stalled test attempt (10 s) outlives its disposed test host. It uses that test's own host and database only.
- If every waiting check has already timed out, an exception from an abandoned attempt's `DisposeAsync` would go
  unobserved. `CanConnectAsync` itself returns `false` on errors; nothing else in the attempt is expected to throw.
- Compose runs the API in `Development` (SLS-126), so an unhandled exception returns the developer exception
  page, stack trace and request headers included, through Traefik. The health endpoint no longer throws, but other
  endpoints could. A follow-up for SLS-15, not changed here.
