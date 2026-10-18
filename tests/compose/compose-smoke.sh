#!/usr/bin/env bash
# SLS-126 (US-EN-1 T3): the local environment, tested through the commands a developer runs.
#
#   bash tests/compose/compose-smoke.sh
#
# Builds and starts compose.yaml as its own Compose project (shipping-smoke, never the developer's "shipping"),
# checks the services, networks, routing, readiness order, migrations and data persistence, then removes that
# project's containers, network, volume and images. It never touches another project's containers or volumes.
# The database password is generated for the run, and a developer's .env is ignored: compose gets an empty env file.
# Needs bash, curl and a Docker engine with Compose v2. Exit code 0 only if every test passed.

set -uo pipefail
export MSYS_NO_PATHCONV=1 # Git Bash on Windows would otherwise rewrite container paths such as /app.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT" || exit 2

PROJECT="${SMOKE_PROJECT:-shipping-smoke}"
if [[ "$PROJECT" != shipping-smoke* ]]; then
  echo "Refusing to run: the project name must start with shipping-smoke (cleanup deletes its volume)." >&2
  exit 2
fi
export SHIPPING_HTTP_PORT="${SMOKE_HTTP_PORT:-18000}"
export POSTGRES_PASSWORD="smoke-$(od -An -tx1 -N16 /dev/urandom | tr -d ' \n')"
MARKER="sls126-$(od -An -tx1 -N8 /dev/urandom | tr -d ' \n')" # a row written to prove the volume persists
ENTRY="http://127.0.0.1:${SHIPPING_HTTP_PORT}"
EXPECTED_SERVICES="api migrate postgres traefik"

# An empty env file, so a developer's .env (SHIPPING_HOST, POSTGRES_USER, ...) cannot change what is tested.
EMPTY_ENV="$(mktemp)"
ENV_FILE="$EMPTY_ENV"
command -v cygpath >/dev/null && ENV_FILE="$(cygpath -w "$EMPTY_ENV")"
compose() { docker compose -p "$PROJECT" --env-file "$ENV_FILE" "$@"; }

passed=0
failed=0
failures=()
pass() { passed=$((passed + 1)); echo "PASS  $1"; }
fail() { failed=$((failed + 1)); failures+=("$1"); echo "FAIL  $1: $2"; }

# Runs one test function; the function prints nothing on success and its failure reason otherwise.
run() {
  local name="$1" reason
  if reason="$("$name" 2>&1)"; then pass "$name"; else fail "$name" "${reason:-failed}"; fi
}

container() { compose ps -a -q "$1" 2>/dev/null | head -n1; }
inspect() { local id; id="$(container "$1")"; [[ -n "$id" ]] || { echo "no $1 container"; return 1; }; docker inspect -f "$2" "$id"; }
psql_value() { compose exec -T postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAqc "$1"' _ "$1"; }
# RFC 3339 UTC timestamps from the engine, with the fraction padded to 9 digits: they then sort as strings
# (no GNU date needed, so this also runs on macOS).
sortable_time() {
  local whole="${1%Z}" fraction=""
  if [[ "$whole" == *.* ]]; then fraction="${whole#*.}"; whole="${whole%%.*}"; fi
  fraction="${fraction}000000000"
  echo "$whole.${fraction:0:9}"
}

# Waits until a request through Traefik returns something other than a gateway error: no fixed sleep.
http_through_traefik() { # host path -> prints status line and headers
  local host="$1" path="$2" headers status
  for _ in $(seq 1 60); do
    headers="$(curl -s -o /dev/null -D - -m 5 -H "Host: $host" "$ENTRY$path" | tr -d '\r')"
    status="$(head -n1 <<<"$headers" | awk '{print $2}')"
    if [[ -n "$status" && "$status" != 502 && "$status" != 503 && "$status" != 504 ]]; then
      echo "$headers"
      return 0
    fi
    sleep 1
  done
  echo "no answer from $ENTRY$path (Host: $host): ${status:-no response}"
  return 1
}

# One GET /health through Traefik, no retry: prints the status code, the Server header and the body, one per line.
get_health() {
  local response
  response="$(curl -s -m 5 -i -H "Host: localhost" "$ENTRY/health" | tr -d '\r')"
  head -n1 <<<"$response" | awk '{print $2}'
  grep -i '^server:' <<<"$response" | head -n1 | cut -d' ' -f2-
  sed '1,/^$/d' <<<"$response"
}

# Waits until GET /health through Traefik is answered with the given status by the API itself (Server: Kestrel),
# not by Traefik. SLOS-127 (US-EN-1 T4).
health_becomes() { # status attempts -> prints the answer
  local expected="$1" answer=""
  for _ in $(seq 1 "$2"); do
    answer="$(get_health)"
    if [[ "$(sed -n 1p <<<"$answer")" == "$expected" && "$(sed -n 2p <<<"$answer")" == [Kk]estrel ]]; then
      echo "$answer"
      return 0
    fi
    sleep 1
  done
  echo "GET /health through Traefik was not answered $expected by the API in $2 attempts; last: $(tr '\n' ' ' <<<"$answer")"
  return 1
}

cleanup() {
  # Only this run's project: its containers, its network, its volume and the images it built.
  compose down -v --remove-orphans --rmi local >/dev/null 2>&1 || true
}
trap 'cleanup; rm -f "$EMPTY_ENV"' EXIT

# --- Configuration ----------------------------------------------------------------------------------------

test_compose_configuration_is_valid() {
  compose config --quiet
}

test_compose_refuses_to_start_without_a_database_password() {
  local empty out
  empty="$(mktemp)"
  # An empty env file in place of a developer's .env, which may set the password.
  local env_file="$empty"
  command -v cygpath >/dev/null && env_file="$(cygpath -w "$empty")"
  out="$(env -u POSTGRES_PASSWORD docker compose -p "$PROJECT" --env-file "$env_file" config --quiet 2>&1)"
  local code=$?
  rm -f "$empty"
  [[ $code -ne 0 ]] || { echo "config succeeded with no POSTGRES_PASSWORD"; return 1; }
  grep -q POSTGRES_PASSWORD <<<"$out" || { echo "error does not name POSTGRES_PASSWORD: $out"; return 1; }
}

test_compose_defines_exactly_the_api_database_proxy_and_migration_services() {
  local services
  services="$(compose config --services 2>&1 | sort | tr '\n' ' ' | sed 's/ $//')"
  [[ "$services" == "$EXPECTED_SERVICES" ]] || { echo "services are '$services', expected '$EXPECTED_SERVICES'"; return 1; }
}

test_compose_images_build() {
  compose build --quiet 2>&1 | tail -n 20
  return "${PIPESTATUS[0]}"
}

test_api_image_runs_the_shipping_host_and_refuses_to_start_without_a_connection_string() {
  local image out
  image="$(compose config --images 2>/dev/null | grep -- '-api$' | head -n1)"
  [[ -n "$image" ]] || { echo "no api image in the configuration"; return 1; }
  out="$(docker run --rm --network none "$image" 2>&1)"
  [[ $? -ne 0 ]] || { echo "the api started without a connection string"; return 1; }
  grep -q "ConnectionStrings:PostgreSql is required" <<<"$out" || { echo "not the Shipping host's startup guard: $(tail -n 3 <<<"$out")"; return 1; }
}

test_api_image_is_a_runtime_image_running_as_non_root_without_sdk_or_migration_tooling() {
  local image entrypoint user sdks
  image="$(compose config --images 2>/dev/null | grep -- '-api$' | head -n1)"
  [[ -n "$image" ]] || { echo "no api image in the configuration"; return 1; }
  entrypoint="$(docker image inspect -f '{{json .Config.Entrypoint}}' "$image")"
  [[ "$entrypoint" == '["dotnet","Shipping.Platform.Api.dll"]' ]] || { echo "entrypoint is $entrypoint"; return 1; }
  user="$(docker run --rm --network none --entrypoint id "$image" -u)" || { echo "could not run id in the api image"; return 1; }
  [[ "$user" =~ ^[0-9]+$ && "$user" != 0 ]] || { echo "the api runs as user '$user'"; return 1; }
  sdks="$(docker run --rm --network none --entrypoint dotnet "$image" --list-sdks)" || { echo "could not run dotnet in the api image"; return 1; }
  [[ -z "$sdks" ]] || { echo "the api image contains an SDK: $sdks"; return 1; }
  if docker run --rm --network none --entrypoint sh "$image" -c 'ls /migrations/*efbundle* || ls /app/*efbundle*' >/dev/null 2>&1; then
    echo "the api image contains a migration bundle"; return 1
  fi
}

# --- Startup and readiness --------------------------------------------------------------------------------

test_compose_up_starts_and_waits_for_every_service() {
  compose up -d --wait --wait-timeout 240 2>&1 | tail -n 20
  local code="${PIPESTATUS[0]}"
  [[ $code -eq 0 ]] || { compose ps -a; compose logs --no-color --tail 40; }
  return "$code"
}

test_postgres_is_healthy_by_pg_isready() {
  local health test
  health="$(inspect postgres '{{.State.Health.Status}}')" || { echo "$health"; return 1; }
  [[ "$health" == healthy ]] || { echo "postgres health is $health"; return 1; }
  test="$(inspect postgres '{{json .Config.Healthcheck.Test}}')"
  grep -q pg_isready <<<"$test" || { echo "postgres health check is $test"; return 1; }
}

test_migrations_finish_successfully_before_the_api_starts() {
  local code finished started
  code="$(inspect migrate '{{.State.Status}} {{.State.ExitCode}}')" || { echo "$code"; return 1; }
  [[ "$code" == "exited 0" ]] || { echo "migrate is '$code'"; return 1; }
  finished="$(inspect migrate '{{.State.FinishedAt}}')"
  started="$(inspect api '{{.State.StartedAt}}')" || { echo "$started"; return 1; }
  [[ ! "$(sortable_time "$finished")" > "$(sortable_time "$started")" ]] || { echo "api started at $started, before migrate finished at $finished"; return 1; }
}

test_api_is_ready_and_running() {
  local state
  state="$(inspect api '{{.State.Status}} {{.State.Health.Status}} {{.RestartCount}}')" || { echo "$state"; return 1; }
  [[ "$state" == "running healthy 0" ]] || { echo "api is '$state'"; return 1; }
}

test_api_readiness_is_its_health_endpoint() {
  local test
  test="$(inspect api '{{json .Config.Healthcheck.Test}}')" || { echo "$test"; return 1; }
  grep -q 'GET /health ' <<<"$test" || { echo "api health check is $test"; return 1; }
}

# --- Networking -------------------------------------------------------------------------------------------

test_api_reaches_postgres_by_its_service_name() {
  local env
  env="$(inspect api '{{range .Config.Env}}{{println .}}{{end}}' | grep '^ConnectionStrings__PostgreSql=')"
  grep -q 'Host=postgres;' <<<"$env" || { echo "api connection string does not name the postgres service: ${env%%Password=*}"; return 1; }
  grep -qi 'localhost\|127\.0\.0\.1' <<<"$env" && { echo "api connection string names localhost"; return 1; }
  compose exec -T api bash -c 'exec 3<>/dev/tcp/postgres/5432' || { echo "api cannot open postgres:5432"; return 1; }
}

test_networks_separate_the_proxy_from_the_database() {
  local api postgres traefik internal
  api="$(inspect api '{{range $n, $_ := .NetworkSettings.Networks}}{{$n}} {{end}}')"
  postgres="$(inspect postgres '{{range $n, $_ := .NetworkSettings.Networks}}{{$n}} {{end}}')"
  traefik="$(inspect traefik '{{range $n, $_ := .NetworkSettings.Networks}}{{$n}} {{end}}')"
  [[ "$api" == "${PROJECT}_backend ${PROJECT}_edge " ]] || { echo "api networks: $api"; return 1; }
  [[ "$postgres" == "${PROJECT}_backend " ]] || { echo "postgres networks: $postgres"; return 1; }
  [[ "$traefik" == "${PROJECT}_edge " ]] || { echo "traefik networks: $traefik"; return 1; }
  internal="$(docker network inspect -f '{{.Internal}}' "${PROJECT}_backend")"
  [[ "$internal" == true ]] || { echo "backend network is not internal"; return 1; }
  if compose exec -T traefik nc -z -w 2 postgres 5432 >/dev/null 2>&1; then echo "traefik can reach postgres"; return 1; fi
}

test_only_the_proxy_entry_point_is_published_on_the_host() {
  # Every host binding of every container in the project, from the engine: an EXPOSEd port is not a binding.
  local bindings service
  bindings="$(for service in api postgres traefik; do
    inspect "$service" "{{range \$port, \$bound := .NetworkSettings.Ports}}{{range \$bound}}$service:{{\$port}}->{{.HostIp}}:{{.HostPort}} {{end}}{{end}}"
  done | tr -s ' \n' ' ' | sed 's/^ //; s/ $//')"
  [[ "$bindings" == "traefik:80/tcp->127.0.0.1:${SHIPPING_HTTP_PORT}" ]] \
    || { echo "host bindings are '$bindings', expected only traefik:80/tcp->127.0.0.1:${SHIPPING_HTTP_PORT}"; return 1; }
}

# --- Traefik ----------------------------------------------------------------------------------------------

test_traefik_routes_requests_for_the_api_host_to_the_api() {
  local headers
  # An unmapped path must come back as the API's own 404, served by Kestrel, not as a gateway error or
  # Traefik's "no router" 404.
  headers="$(http_through_traefik localhost /sls126-unmapped-route)" || { echo "$headers"; return 1; }
  [[ "$(head -n1 <<<"$headers" | awk '{print $2}')" == 404 ]] || { echo "status: $(head -n1 <<<"$headers")"; return 1; }
  grep -qi '^server: kestrel' <<<"$headers" || { echo "the 404 did not come from the API: $headers"; return 1; }
}

test_traefik_does_not_route_other_hosts() {
  local headers
  headers="$(http_through_traefik unknown.invalid /)" || { echo "$headers"; return 1; }
  [[ "$(head -n1 <<<"$headers" | awk '{print $2}')" == 404 ]] || { echo "status: $(head -n1 <<<"$headers")"; return 1; }
  grep -qi '^server: kestrel' <<<"$headers" && { echo "a request for another host reached the API"; return 1; }
  return 0
}

test_traefik_has_no_dashboard_or_api_and_a_read_only_docker_socket() {
  local args socket
  if compose exec -T traefik wget -q -T 2 -O /dev/null http://127.0.0.1:8080/api/rawdata >/dev/null 2>&1; then
    echo "the Traefik API answers on :8080"; return 1
  fi
  args="$(inspect traefik '{{join .Args " "}}')"
  grep -q -- '--providers.docker.exposedByDefault=false' <<<"$args" || { echo "containers are exposed by default: $args"; return 1; }
  grep -q -- "--providers.docker.constraints=Label(\`com.docker.compose.project\`,\`${PROJECT}\`)" <<<"$args" \
    || { echo "traefik is not limited to this project's containers: $args"; return 1; }
  grep -q -- '--api' <<<"$args" && { echo "traefik api enabled: $args"; return 1; }
  socket="$(inspect traefik '{{range .Mounts}}{{if eq .Destination "/var/run/docker.sock"}}{{.RW}}{{end}}{{end}}')"
  [[ "$socket" == false ]] || { echo "docker socket mount RW=$socket"; return 1; }
}

# --- Health (SLOS-127, US-EN-1 T4) ------------------------------------------------------------------------

test_health_returns_200_healthy_from_the_api_through_traefik() {
  local answer
  answer="$(health_becomes 200 60)" || { echo "$answer"; return 1; }
  [[ "$(sed -n '3,$p' <<<"$answer")" == Healthy ]] || { echo "body: $(sed -n '3,$p' <<<"$answer")"; return 1; }
}

test_health_returns_503_while_postgres_is_stopped_and_200_once_it_is_back() {
  local answer down later="" next
  compose stop postgres >/dev/null 2>&1 || { echo "could not stop postgres"; return 1; }
  answer="$(health_becomes 503 30)"
  down=$?
  # Every request while the database is gone, not only the first, must be answered by the API within curl's 5 s.
  for _ in 1 2 3; do
    next="$(get_health)"
    [[ "$(sed -n 1p <<<"$next")" == 503 && "$(sed -n 2p <<<"$next")" == [Kk]estrel ]] || later="$(tr '\n' ' ' <<<"$next")"
  done
  compose start postgres >/dev/null 2>&1 || { echo "could not start postgres again"; return 1; }
  [[ $down -eq 0 ]] || { echo "while postgres was stopped: $answer"; return 1; }
  [[ "$(sed -n '3,$p' <<<"$answer")" == Unhealthy ]] || { echo "body while postgres was stopped: $(sed -n '3,$p' <<<"$answer")"; return 1; }
  [[ -z "$later" ]] || { echo "a later request while postgres was stopped was answered '$later' (status code, server, body)"; return 1; }
  answer="$(health_becomes 200 90)" || { echo "after postgres started again: $answer"; return 1; }
}

# --- Persistence and migrations ---------------------------------------------------------------------------

test_each_module_is_migrated_through_its_own_schema_and_history() {
  local dir module schema expected actual
  [[ -n "$(ls -d src/Modules/*/Shipping.Modules.*.Infrastructure 2>/dev/null)" ]] || { echo "no modules found"; return 1; }
  for dir in src/Modules/*/Shipping.Modules.*.Infrastructure; do
    module="$(basename "$dir" | sed -E 's/^Shipping\.Modules\.(.+)\.Infrastructure$/\1/')"
    schema="$(tr '[:upper:]' '[:lower:]' <<<"$module")"
    expected="$(ls "$dir/Persistence/Migrations" | grep -E '^[0-9]{14}_.+\.cs$' | grep -v '\.Designer\.cs$' | sed 's/\.cs$//' | sort | tr '\n' ' ')"
    actual="$(psql_value "SELECT \"MigrationId\" FROM \"$schema\".\"__EFMigrationsHistory\" ORDER BY 1" | tr -d '\r' | tr '\n' ' ')"
    [[ -n "$expected" && "$actual" == "$expected" ]] || { echo "$module history in $schema is '$actual', expected '$expected'"; return 1; }
  done
  [[ "$(psql_value "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory'" | tr -d '\r')" == 0 ]] \
    || { echo "a migration history exists in public"; return 1; }
}

test_migrations_run_again_change_nothing() {
  local before after
  before="$(psql_value "SELECT string_agg(table_schema || '.' || table_name, ',' ORDER BY 1) FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'")"
  compose run --rm --no-deps migrate >/dev/null 2>&1 || { echo "a second migration run failed"; return 1; }
  after="$(psql_value "SELECT string_agg(table_schema || '.' || table_name, ',' ORDER BY 1) FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'")"
  [[ "$before" == "$after" ]] || { echo "histories changed from '$before' to '$after'"; return 1; }
  test_each_module_is_migrated_through_its_own_schema_and_history
}

test_database_data_survives_container_recreation_and_compose_down() {
  local first second
  psql_value "CREATE TABLE IF NOT EXISTS public.smoke_marker (value text); INSERT INTO public.smoke_marker VALUES ('$MARKER')" >/dev/null \
    || { echo "could not write the marker"; return 1; }
  first="$(container postgres)"
  compose up -d --wait --force-recreate --no-deps postgres >/dev/null 2>&1 || { echo "postgres did not come back after recreation"; return 1; }
  second="$(container postgres)"
  [[ -n "$second" && "$first" != "$second" ]] || { echo "postgres container was not recreated"; return 1; }
  [[ "$(psql_value "SELECT value FROM public.smoke_marker" | tr -d '\r')" == "$MARKER" ]] || { echo "marker lost after recreation"; return 1; }

  compose down >/dev/null 2>&1 || { echo "compose down failed"; return 1; }
  docker volume inspect "${PROJECT}_postgres-data" >/dev/null 2>&1 || { echo "compose down removed the volume"; return 1; }
  compose up -d --wait --wait-timeout 240 >/dev/null 2>&1 || { echo "the environment did not come back after compose down"; return 1; }
  [[ "$(psql_value "SELECT value FROM public.smoke_marker" | tr -d '\r')" == "$MARKER" ]] || { echo "marker lost after compose down and up"; return 1; }
}

test_api_stays_running_after_the_checks() {
  test_api_is_ready_and_running
}

test_a_failed_migration_prevents_the_api_from_starting() {
  local state code logs
  compose down >/dev/null 2>&1
  # The volume keeps the original password, so the migration's login fails: the API must not start on it.
  POSTGRES_PASSWORD="wrong-$MARKER" compose up -d --wait --wait-timeout 120 >/dev/null 2>&1 && { echo "compose up succeeded"; return 1; }
  code="$(inspect migrate '{{.State.ExitCode}}')"
  [[ "$code" != 0 ]] || { echo "migrate exited 0"; return 1; }
  state="$(inspect api '{{.State.Status}}' 2>/dev/null)" || state=absent
  [[ "$state" == created || "$state" == absent ]] || { echo "api is '$state'"; return 1; }
  # Captured first: piped into grep -q, pipefail turns the SIGPIPE compose logs gets when grep stops at the first
  # match into a failure.
  logs="$(compose logs --no-color migrate 2>&1)" || { echo "could not read the migrate logs: $logs"; return 1; }
  grep -qi 'password authentication failed' <<<"$logs" || { echo "the failure is not visible in the migrate logs"; return 1; }
}

# --- Run --------------------------------------------------------------------------------------------------

echo "Compose project: $PROJECT, entry point: $ENTRY"
cleanup
for t in \
  test_compose_configuration_is_valid \
  test_compose_refuses_to_start_without_a_database_password \
  test_compose_defines_exactly_the_api_database_proxy_and_migration_services \
  test_compose_images_build \
  test_api_image_runs_the_shipping_host_and_refuses_to_start_without_a_connection_string \
  test_api_image_is_a_runtime_image_running_as_non_root_without_sdk_or_migration_tooling \
  test_compose_up_starts_and_waits_for_every_service \
  test_postgres_is_healthy_by_pg_isready \
  test_migrations_finish_successfully_before_the_api_starts \
  test_api_is_ready_and_running \
  test_api_readiness_is_its_health_endpoint \
  test_api_reaches_postgres_by_its_service_name \
  test_networks_separate_the_proxy_from_the_database \
  test_only_the_proxy_entry_point_is_published_on_the_host \
  test_traefik_routes_requests_for_the_api_host_to_the_api \
  test_traefik_does_not_route_other_hosts \
  test_traefik_has_no_dashboard_or_api_and_a_read_only_docker_socket \
  test_health_returns_200_healthy_from_the_api_through_traefik \
  test_health_returns_503_while_postgres_is_stopped_and_200_once_it_is_back \
  test_each_module_is_migrated_through_its_own_schema_and_history \
  test_migrations_run_again_change_nothing \
  test_api_stays_running_after_the_checks \
  test_database_data_survives_container_recreation_and_compose_down \
  test_a_failed_migration_prevents_the_api_from_starting; do
  run "$t"
done

echo
echo "Passed: $passed, Failed: $failed, Total: $((passed + failed))"
if (( failed > 0 )); then
  printf '  failed: %s\n' "${failures[@]}"
  echo "Logs of the last state:"
  compose ps -a 2>&1
  compose logs --no-color --tail 30 2>&1
  exit 1
fi
