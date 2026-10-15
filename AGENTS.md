# Shipping.Platform — instructions for Codex

This backend was generated from MP Core `0.10.1`. MP Core is consumed as NuGet packages;
its source is never copied here and is never edited from this repository.

## Start every task this way

1. **Read `.mpcore/template-manifest.json`.** It records `shape`, `transport` and `messaging` for
   this repository. Those are decisions already made, not defaults to revisit, and they determine
   which guidance applies.
2. **Establish the actual state.** Inspect the tree and the module you are about to touch before
   proposing anything. Do not rely on what a previous session said.
3. **Confirm the business behaviour and its acceptance criteria.** Restate the requirement in one
   sentence. If it is ambiguous, list the specific ambiguities and ask. Never invent a business rule,
   status, limit or workflow to fill a gap.
4. **Select the one relevant skill and open its body.** `.mpcore/skills/INVENTORY.md` says which skill
   applies. Read the canonical body at `.mpcore/skills/<name>/SKILL.md`.
5. **Implement only the authorized scope.** One capability at a time.
6. **Run the tests and report the real output**, including failures. A specification is not evidence.
7. **Preserve unrelated changes** and leave every human approval boundary where it is.

## Non-negotiable

- The host is a bearer-only resource server. Identity comes from the validated token through
  `ICurrentActorAccessor` — never from a body, query, route value or arbitrary header.
- Login, signup, OTP and password handling are never implemented here.
- Do not weaken authorization, delete an assertion or relax a default to make something pass.
- Do not add a transport or a message broker the manifest does not record; that is a scope change
  only the owner can make.
- No credential, realm URL or connection string belongs in this repository.

Detailed guidance is loaded on demand from the skill files. Do not paste them all into a session.

## Repository-specific T1 module convention

US-EN-1 T1 uses four projects per module: `Domain`, `Application`, `Infrastructure`, and `Api`
under `src/Modules/<Name>`. This accepted convention is recorded in
`docs/architecture/ADR-001-four-project-modules.md` and implemented by `templates/Module`.
For this repository, follow that convention when creating a module. The upstream MP Core
`mpcore-implement-ddd-module` skill describes a one-project default and remains unchanged.
The executable host references a module's Api project and explicitly calls its service and
endpoint registration methods.
