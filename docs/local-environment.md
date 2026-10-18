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
4. `api` is reported healthy once Kestrel accepts connections on 8080. This is process readiness only, not
   application health.

## Entry point

`http://localhost:8000` (the router matches `Host(localhost)`, configurable with `SHIPPING_HOST`). The host
maps no endpoint yet, so every path returns the API's own `404`. That is what routing looks like until
`GET /health` (SLS-15 T4) or a module endpoint exists. A request for any other host gets Traefik's own `404`.

Traefik sees only containers of this Compose project that set `traefik.enable=true`. Its dashboard and API are
not enabled. It does not trust `X-Forwarded-*` headers from clients, and it reads the Docker socket through a
read-only bind.

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
routing, readiness order, migrations and data persistence, then removes only that project's containers,
network, volume and images. It ignores your `.env`. The project name must start with `shipping-smoke`. It has
been run on Linux. It is written for Git Bash and macOS too, but neither has been run end to end yet. CI runs
it in the `compose-smoke` job. Evidence:
[US-EN-1 T3](testing/US-EN-1-T3-red-green.md).

## Known limitations and pending SLS-15 work

- **No health endpoint yet.** `GET /health` and the parent criterion "the health endpoint eventually returns
  HTTP 200" belong to SLS-15 T4. Until then, the API's readiness check is a TCP probe.
- **Full new-developer README:** SLS-15 T5. **Team walkthrough:** SLS-15 T6.
- **Docker socket:** the read-only bind keeps Traefik from changing the socket file, but it does not restrict
  the Docker API calls Traefik makes. Traefik's Docker provider only reads. If that is not acceptable, a
  socket proxy that allows only read calls is the next step. That decision is not made here.
- **No TLS** on the local entry point.
