# Local environment (Docker Compose)

SLS-126 (US-EN-1 T3). `compose.yaml` runs the Shipping host, its PostgreSQL database and Traefik on one machine.
It is a development environment, not a production deployment.

## Prerequisites

- Docker Engine with Compose v2 (`docker compose version`).
- Port 8000 free on the loopback interface, or another port set in `SHIPPING_HTTP_PORT`.
- No .NET SDK is needed to run the environment. The images build the host.

## Start and stop

```bash
cp .env.example .env          # set POSTGRES_PASSWORD; .env is git-ignored
docker compose up --build     # or: docker compose up --build -d --wait
docker compose ps
docker compose logs -f api
docker compose down           # stops and removes the containers; the database volume stays
```

`docker compose down -v` also deletes the database volume and every row in it. Use it only when you mean to.

## Services

| Service | Image | Role | Networks | Host port |
|---|---|---|---|---|
| `postgres` | `postgres:17.6-alpine` | The one Shipping database. Healthy when `pg_isready` answers over TCP. | `backend` | none |
| `migrate` | `Dockerfile`, target `migrator` | Applies each module's EF Core migration bundle, then exits. | `backend` | none |
| `api` | `Dockerfile`, target `api` | The Shipping host (`Shipping.Platform.Api`), on port 8080, non-root. | `backend`, `edge` | none |
| `traefik` | `traefik:v3.7.15` | Reverse proxy, the only published port. | `edge` | `127.0.0.1:${SHIPPING_HTTP_PORT:-8000}` |

The Compose project is `shipping`, so containers are named `shipping-<service>-1` and the volume is
`shipping_postgres-data`. `backend` is an internal network: it has no route out, and Traefik is not on it.

## Startup order

1. `postgres` starts and becomes healthy.
2. `migrate` runs once `postgres` is healthy. It runs one bundle per module, each through that module's own
   `DbContext`, schema and `<module>.__EFMigrationsHistory` (ADR-002). A bundle applies only the migrations
   missing from its history, so every `up` is safe to repeat. The first failure stops `migrate` with a non-zero
   exit code.
3. `api` starts only after `migrate` exits with 0. If a migration fails, the API does not start, and
   `docker compose logs migrate` shows why.
4. `api` is reported healthy once `GET /health` answers `200` inside the container (see
   [Health endpoint](#health-endpoint)).

## Entry point

`http://localhost:8000` (the router matches `Host(localhost)`, configurable with `SHIPPING_HOST`). The host
maps `GET /health` and no business endpoint yet, so any other path returns the API's own `404`. A request for
any other host gets Traefik's own `404`.

Traefik sees only containers of this Compose project that set `traefik.enable=true`. Its dashboard and API are
not enabled. It does not trust `X-Forwarded-*` headers from clients, and it reads the Docker socket through a
read-only bind.

## Health endpoint

`GET /health` (SLOS-127, US-EN-1 T4) reports **readiness**: whether the host can serve its traffic, which
needs its database. Each module registers a check that opens a connection through its own `DbContext` and
closes it again. Nothing is queried, created or migrated, so the answer does not depend on whether migrations
ran. The host registers no check of its own.

| Answer | Meaning |
|---|---|
| `200` `Healthy` | Kestrel serves requests and every module's context reaches PostgreSQL. |
| `503` `Unhealthy` | At least one module's context cannot reach PostgreSQL within 2 s. |
| `405` | Any method other than `GET`. |

The body is the status word only, as `text/plain` with `Cache-Control: no-store, no-cache`. It never contains a
check name, host, connection string or exception. The endpoint needs no token. It is marked `AllowAnonymous`, so it
stays open once authentication is added (none is configured yet). There is no separate liveness endpoint:
nothing in this environment restarts the API on a failed probe.

The `api` container's Docker health check is `GET /health`. Traefik routes only to a container Docker reports
`healthy`, so what a request through Traefik gets depends on how long the database has been gone:

| State | Through Traefik (`curl -i -H 'Host: localhost' http://localhost:8000/health`) |
|---|---|
| Stack up | `200` `Healthy`, `Server: Kestrel` |
| PostgreSQL stopped, API still `healthy` in Docker (about the first 2 minutes) | `503` `Unhealthy`, `Server: Kestrel` |
| PostgreSQL stopped long enough for 20 failed probes: API `unhealthy` | `404 page not found`, no `Server` header: Traefik's own answer, the API is out of rotation |
| PostgreSQL back | `200` again once the next probe passes |
| API stopped, or not yet `healthy` after `up` | Traefik's own `404` |

Traefik also answers its own `404` for a few seconds after `docker compose up --wait` returns, until it has
picked up the healthy container. `Server: Kestrel` tells an answer from the API apart from one from Traefik.

The 2 s timeout bounds the answer, not the database work. While PostgreSQL is unreachable, a connection attempt
keeps running in the background until Npgsql gives up (10-12 s recorded, 15 s at most). Each module keeps at
most one such attempt: further requests wait for it rather than start another, so the connection pool the API
shares with business traffic is not filled by health requests. Once PostgreSQL is back, `/health` can stay `503`
for what is left of that one attempt. To ask the API directly, bypassing
Traefik: `docker compose exec api bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080; printf "GET /health HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3; cat <&3'`.

## Configuration

| Variable (`.env`) | Default | Used for |
|---|---|---|
| `POSTGRES_PASSWORD` | none, required | The database password. Compose refuses to start without it. |
| `POSTGRES_USER`, `POSTGRES_DB` | `shipping` | The database role and name. |
| `SHIPPING_HTTP_PORT` | `8000` | The Traefik port on `127.0.0.1`. |
| `SHIPPING_HOST` | `localhost` | The host name Traefik routes to the API. |

The API and `migrate` get `ConnectionStrings__PostgreSql` with `Host=postgres`, the Compose service name. The
API runs with `ASPNETCORE_ENVIRONMENT=Development`. No connection string or password is built into an image.

PostgreSQL applies `POSTGRES_PASSWORD` only when it first creates the volume. If you change it later, `migrate`
fails with `password authentication failed`, and the API does not start. Set the old value back, or delete the
volume deliberately.

For `psql`, use `docker compose exec postgres psql -U shipping -d shipping`. The database port is not
published. To publish it, add it in a git-ignored `compose.override.yml`.

## Tests

`bash tests/compose/compose-smoke.sh` builds and starts the environment as a separate Compose project,
`shipping-smoke`, on port 18000 with a generated password. It checks services, networks, published ports,
routing, readiness order, `GET /health` (including a PostgreSQL stop and restart), migrations and data persistence, then removes only that project's containers,
network, volume and images. It ignores your `.env`. The project name must start with `shipping-smoke`. It has
been run on Linux. It is written for Git Bash and macOS too, but neither has passed end to end yet: in Git Bash on
one Windows machine the Docker CLI received no output from containers, so 7 checks failed
([US-EN-1 T5](testing/US-EN-1-T5-red-green.md#green-and-regression)). CI runs
it in the `compose-smoke` job. Evidence:
[US-EN-1 T3](testing/US-EN-1-T3-red-green.md), [US-EN-1 T4](testing/US-EN-1-T4-red-green.md).

## Known limitations and pending SLS-15 work

- **New-developer quickstart:** the [README](../README.md#quickstart) (US-EN-1 T5). **Team walkthrough:** the
  [facilitator guide](onboarding/team-walkthrough.md) and [demo runbook](onboarding/demo-runbook.md) (US-EN-1 T6);
  each session is recorded in the [readiness checklist](onboarding/team-readiness-checklist.md).
- **Docker socket:** the read-only bind keeps Traefik from changing the socket file, but it does not restrict
  the Docker API calls Traefik makes. Traefik's Docker provider only reads. If that is not acceptable, a
  socket proxy that allows only read calls is the next step. That decision is not made here.
- **No TLS** on the local entry point.
