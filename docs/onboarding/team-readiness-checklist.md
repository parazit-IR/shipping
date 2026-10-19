# Team readiness checklist

The record of the [team walkthrough](team-walkthrough.md) (US-EN-1 T6). Copy this file, or fill it in on a branch, for
each session. Four parts are kept apart on purpose, because each proves something different:

| Part | Proves | Filled in by |
|---|---|---|
| [Technical verification by the agent](#technical-verification-by-the-agent) | The demonstrations run on a real machine | The implementation agent, with evidence |
| [Walkthrough attendance](#walkthrough-attendance) | Who was there | Each participant |
| [Individual developer readiness](#individual-developer-readiness) | Each developer can do it on their own machine | Each developer, for their own column |
| [Stream Lead demonstration acceptance](#stream-lead-demonstration-acceptance) | The Stream Lead saw the demonstration and decided | The Stream Lead only |

Nobody fills in another person's row or column. Every result is one of:

| Status | Meaning |
|---|---|
| Pass | Done on your own machine and it worked as the runbook says. |
| Fail | Tried, and it did not work. Add a row to the blocker table. |
| Blocked | Could not try (no Docker, no access, missing tool). Add a row to the blocker table. |
| N/A | Does not apply to you, with the reason in the blocker table's notes. |

## Technical verification by the agent

What the implementation agent ran before any session, on one Windows Server VM with Docker Engine in WSL 2. Results,
timings and console output are in the [US-EN-1 T6 evidence](../testing/US-EN-1-T6-red-green.md). This part shows that
the material works. It does not show that anyone attended, understood or accepted it.

| Check | Result | Evidence |
|---|---|---|
| Demo 1: environment starts, `migrate` exits 0, `GET /health` answers `200 Healthy` | Passed in rehearsal | [Demo 1](../testing/US-EN-1-T6-red-green.md#demo-1--local-environment) |
| Demo 2: `sample` schema and `sample."__EFMigrationsHistory"`; second `migrate` run applies nothing | Passed in rehearsal (`psql` output only through the WSL Docker CLI) | [Demo 2](../testing/US-EN-1-T6-red-green.md#demo-2--module-persistence) |
| Demo 3: restore, build, unit, architecture and integration tests | Passed in rehearsal | [Demo 3](../testing/US-EN-1-T6-red-green.md#demo-3--tests) |
| Demo 4: module generated, registered, migrated and tested in a disposable clone, bash and PowerShell | Passed in rehearsal | [Demo 4](../testing/US-EN-1-T6-red-green.md#demo-4--module-creation) |
| Demo 5: `503` from Kestrel, Traefik `404` once unhealthy, recovery to `200` | Passed in rehearsal | [Demo 5](../testing/US-EN-1-T6-red-green.md#demo-5--postgresql-unavailable) |
| Documentation checks and the full test suite on the T6 branch | See evidence | [Tests](../testing/US-EN-1-T6-red-green.md#full-test-suite-and-smoke-test) |

## Walkthrough attendance

Each participant writes their own name and Yes or No. Leave a row empty if the slot was not used.

| Slot | Name | Attended (Yes / No) | Part of the session missed, if any | Notes |
|---|---|---|---|---|
| Developer 1 | | | | |
| Developer 2 | | | | |
| Developer 3 | | | | |
| Developer 4 | | | | |
| Developer 5 | | | | |
| Stream Lead | | | | |
| Facilitator | | | | |

Session date, place or call link, and the commit of `main` used: to be written here by the facilitator.

## Individual developer readiness

Each developer fills in only their own column, on their own machine. Use Pass, Fail, Blocked or N/A. The second column
says how to check; the [runbook](demo-runbook.md) has the commands and the expected output.

| # | Item | How to verify | Developer 1 | Developer 2 | Developer 3 | Developer 4 | Developer 5 |
|---|---|---|---|---|---|---|---|
| 1 | Correct repository and branch | `git remote -v` shows `parazit-IR/shipping`; `git switch main` and `git pull --ff-only` succeed | | | | | |
| 2 | Docker connectivity | `docker info` and `docker compose version` answer | | | | | |
| 3 | Local startup | Demo 1: `docker compose up --build -d --wait --wait-timeout 300` exits 0; `docker compose ps -a migrate` shows `Exited (0)` | | | | | |
| 4 | `GET /health` answers `200` | `curl -i --fail --retry 15 --retry-delay 2 --retry-all-errors http://localhost:8000/health` shows `200 OK`, `Server: Kestrel`, `Healthy` | | | | | |
| 5 | Solution builds | `dotnet build Shipping.slnx -c Release` with 0 errors | | | | | |
| 6 | Relevant tests pass | Demo 3: every test project `Passed!`, `Failed: 0`, `Skipped: 0` | | | | | |
| 7 | Module boundaries understood | Explains the four projects and their allowed references, and why no module references another | | | | | |
| 8 | Module template can be generated | Demo 4 in a disposable clone: build, baseline migration and tests pass; the clone is deleted afterwards | | | | | |
| 9 | Migrations and schema ownership understood | Explains own context, schema and history per module, who applies migrations, and why a two-module change is not one transaction | | | | | |
| 10 | PR and review workflow | Explains branch per task, tests first, independent review and CI, and knows which of these rules are enforced by repository settings and which are team agreements | | | | | |
| 11 | Failure reading | Explains `503` from Kestrel versus Traefik's `404` (Demo 5) | | | | | |

Machine (OS, Docker version) per developer, for reproducing a Fail later:

| Slot | OS | Docker Engine / Desktop version | .NET SDK |
|---|---|---|---|
| Developer 1 | | | |
| Developer 2 | | | |
| Developer 3 | | | |
| Developer 4 | | | |
| Developer 5 | | | |

## Module ownership

Filled in during the session, not by this document. No rule in the repository says who decides module ownership; the
walkthrough proposes the Stream Lead. Write down here how it was decided. One row per module or planned bounded
context.

How ownership is decided:

| Module | Owner | Agreed with | Notes |
|---|---|---|---|
| Sample (technical, no behaviour) | | | |
| | | | |
| | | | |

## Blockers and follow-up

Every Fail or Blocked item above gets a row. A follow-up owner is a person, agreed with them.

| # | Slot | Item # | Status (Fail / Blocked) | What happened (error text) | Follow-up owner | Follow-up action | Resolved (Yes / No) |
|---|---|---|---|---|---|---|---|
| 1 | | | | | | | |
| 2 | | | | | | | |
| 3 | | | | | | | |

Questions from the session that could not be answered there:

| # | Question | Asked by | Who will answer | Answer or link |
|---|---|---|---|---|
| 1 | | | | |
| 2 | | | | |

## Stream Lead demonstration acceptance

Filled in by the Stream Lead only. Acceptance of the demonstration is a separate decision from the code review of the
pull request that adds these documents.

| Item | Stream Lead |
|---|---|
| Name | |
| Saw Demo 1 (local environment and `GET /health`) | |
| Saw Demo 2 (module persistence) | |
| Saw Demo 3 (tests) | |
| Saw Demo 4 (module creation) | |
| Saw Demo 5 (failure and recovery) | |
| Discussed Demo 6 (parallel workflow) | |
| All five developer columns complete, or the gaps recorded as blockers | |
| Decision (Accepted / Changes required / Not yet) | |
| Conditions or follow-ups | |
| Date | |
