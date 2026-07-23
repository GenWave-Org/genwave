#!/usr/bin/env bash
# launch.sh — tear the Docker stack down and bring it back up.
#
# Building is build.sh's job; this script just (re)launches. Compose will still build any
# missing image for a service that has a build: context, so a first launch works too.
#
# Single-station stack. Everything is published on localhost — no proxy, no FQDNs.
#
# Presets (STORY-201 / SPEC F78.10):
#   ./launch.sh              dev flow (default, unchanged): teardown, db-first up, wait for
#                             db healthy, ./migrate.sh --keep-going, full up, status.
#   ./launch.sh --pinned     demo/appliance flow: pull -> migrate.sh -> up -d against
#                             compose.yaml + compose.demo.yaml. NEVER builds — it's meant
#                             for a box that only ever runs published GHCR images. See
#                             DEPLOYMENT.md's "Applying migrations".
#   ./launch.sh --with a,b   merge a,b into COMPOSE_PROFILES (env var, else .env's value)
#                             for this launch's compose invocations.
#   ./launch.sh --dry-run    print the exact command plan (one per line, "plan> "-prefixed)
#                             plus the effective profile set ("plan-profiles> "), then exit
#                             0. Touches nothing — no docker/compose call is made at all.
#
# Presets compose, e.g. the sanctioned demo-box launch:
#   ./launch.sh --pinned --with logging,tunnel
#
# Env overrides:
#   BUILD=1 ./launch.sh      force a rebuild on the way up — dev flow only. BUILD=1 with
#                             --pinned is a hard error (--pinned never builds).
#   SKIP_PREFLIGHT=1 ./launch.sh  bypass machine preflight checks (gh-#19 escape hatch).
set -euo pipefail
cd "$(dirname "$0")"

. tools/preflight.sh

usage() {
  awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "$0"
}

PINNED=0
DRY_RUN=0
WITH=""

while [ $# -gt 0 ]; do
  case "$1" in
    --pinned)
      PINNED=1
      shift
      ;;
    --with)
      [ $# -ge 2 ] || { echo "launch.sh: --with needs a value" >&2; usage >&2; exit 2; }
      WITH="$2"
      shift 2
      ;;
    --with=*)
      WITH="${1#*=}"
      shift
      ;;
    --dry-run)
      DRY_RUN=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "launch.sh: unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

# --build never applies to --pinned (it only ever runs pulled images) — reject the
# combination up front, before any docker/compose call is made.
if [ "$PINNED" = "1" ] && [ "${BUILD:-0}" = "1" ]; then
  echo "launch.sh: BUILD=1 is incompatible with --pinned (--pinned never builds)." >&2
  exit 2
fi

# --- compose file selection: plain dev stack, or +compose.demo.yaml under --pinned -----
COMPOSE_ARGS=()
MIGRATE_ARGS=()
if [ "$PINNED" = "1" ]; then
  COMPOSE_ARGS=(-f compose.yaml -f compose.demo.yaml)
  MIGRATE_ARGS=(-f compose.yaml -f compose.demo.yaml)
fi

compose() {
  docker compose "${COMPOSE_ARGS[@]}" "$@"
}

# Human-readable rendering of the compose invocation, for both the dry-run plan and error
# messages — avoids a dangling double space when COMPOSE_ARGS is empty (dev flow).
compose_display() {
  if [ "${#COMPOSE_ARGS[@]}" -eq 0 ]; then
    echo "docker compose"
  else
    echo "docker compose ${COMPOSE_ARGS[*]}"
  fi
}

# --- profile merge (--with): existing COMPOSE_PROFILES (env, else .env) + the given list -
base_profiles="${COMPOSE_PROFILES:-}"
if [ -z "$base_profiles" ] && [ -f .env ]; then
  # `|| true` (gh-#19): an .env without COMPOSE_PROFILES is a valid config, not a pipefail
  # that silently aborts the whole launch under set -e.
  base_profiles="$(grep -E '^COMPOSE_PROFILES=' .env | tail -n1 | cut -d= -f2- || true)"
fi

EFFECTIVE_PROFILES="$base_profiles"
if [ -n "$WITH" ]; then
  if [ -n "$EFFECTIVE_PROFILES" ]; then
    EFFECTIVE_PROFILES="$EFFECTIVE_PROFILES,$WITH"
  else
    EFFECTIVE_PROFILES="$WITH"
  fi
fi
[ -n "$EFFECTIVE_PROFILES" ] && export COMPOSE_PROFILES="$EFFECTIVE_PROFILES"

UP_ARGS=(-d)
[ "${BUILD:-0}" = "1" ] && UP_ARGS+=(--build)

plan_line() { printf 'plan> %s\n' "$*"; }
plan_profiles() { printf 'plan-profiles> %s\n' "$EFFECTIVE_PROFILES"; }

if [ "$PINNED" = "1" ]; then
  # --- pinned/demo flow: pull -> migrate.sh -> up -d, never builds -----------------------
  if [ "$DRY_RUN" = "1" ]; then
    plan_line "$(compose_display) pull"
    plan_line "./migrate.sh ${MIGRATE_ARGS[*]}"
    plan_line "$(compose_display) up -d"
    plan_line "$(compose_display) ps"
    plan_profiles
    exit 0
  fi

  # gh-#19 preflight — after --dry-run (which must touch nothing and needs no Docker),
  # before the first real docker call.
  preflight_docker
  preflight_env_secrets

  echo "==> pulling published images"
  if ! compose pull; then
    preflight_fail "Image pull failed — the running stack was NOT touched." \
      "Check network/GHCR reachability, then re-run: ./launch.sh --pinned" \
      "The previous images are still local; the stack keeps running as-is."
  fi

  echo "==> applying schema migrations against the running db"
  if ! ./migrate.sh "${MIGRATE_ARGS[@]}"; then
    preflight_fail "Schema migration failed — the stack was NOT restarted onto the new images." \
      "Inspect the db: $(compose_display) logs db" \
      "Migrations are idempotent — fix the cause and re-run: ./launch.sh --pinned"
  fi

  echo "==> bringing the stack up"
  # A failed partial up on an appliance is deliberately NOT rolled back with `down`:
  # whatever is still broadcasting keeps broadcasting (never-silent outranks tidiness).
  # Report precisely and say how to proceed instead.
  if ! compose up -d; then
    compose ps || true
    preflight_fail "Bringing the stack up failed part-way (status above)." \
      "Inspect the failing service: $(compose_display) logs <service>" \
      "Re-run when fixed: ./launch.sh --pinned (up is idempotent — it converges the rest)."
  fi

  echo "==> stack status"
  compose ps
  exit 0
fi

# --- dev flow (default): teardown, db-first up, health wait, migrate, full up ------------
if [ "$DRY_RUN" = "1" ]; then
  plan_line "$(compose_display) down --remove-orphans"
  plan_line "$(compose_display) up ${UP_ARGS[*]} db"
  plan_line "$(compose_display) ps -q db"
  plan_line "docker inspect <db container> --format {{.State.Health.Status}} (poll until healthy, up to 30x2s)"
  plan_line "./migrate.sh --keep-going"
  plan_line "$(compose_display) up ${UP_ARGS[*]}"
  plan_line "$(compose_display) ps"
  plan_profiles
  exit 0
fi

# gh-#19 preflight — after --dry-run (which must touch nothing and needs no Docker),
# before teardown ever starts: a machine that can't finish the launch never loses the
# stack it already had.
preflight_docker
preflight_env_secrets

# gh-#19 never-half-a-stack: once teardown has begun, any failure funnels here — take the
# partial stack down again so the user is left at a clean, known zero (the one state a
# re-run of ./launch.sh always starts from), never with half the services wedged.
fail_and_rollback() {
  local problem="$1"
  shift
  echo "==> launch failed — rolling the partial stack back down (never-half-a-stack, gh-#19)"
  compose down --remove-orphans || true
  preflight_fail "$problem" "$@"
}

echo "==> tearing down stack"
compose down --remove-orphans

echo "==> bringing the database up first"
if ! compose up "${UP_ARGS[@]}" db; then
  fail_and_rollback "The database service failed to start." \
    "Inspect it: $(compose_display) logs db" \
    "The stack is fully down — fix the cause and re-run: ./launch.sh"
fi

# The persistent pgdata volume only runs db/01-library.sh on a FRESH volume; an existing
# volume never picks up schema added since. The db/*-migration.sh scripts are idempotent
# in-place upgrades (ADD COLUMN IF NOT EXISTS), so applying them on every launch is safe and
# keeps the schema converged BEFORE the api (which queries the new columns) starts — otherwise
# the api crash-loops on a missing column and the stream falls back to the safe loop.
db_cid="$(compose ps -q db)"
db_healthy=0
for _ in $(seq 1 30); do
  if [ "$(docker inspect "$db_cid" --format '{{.State.Health.Status}}' 2>/dev/null)" = "healthy" ]; then db_healthy=1; break; fi
  sleep 2
done
# gh-#19: falling through silently used to let migrate fail (or the api crash-loop on a
# missing column) with no advice — an unhealthy db after 60s is now a hard, explained stop.
if [ "$db_healthy" != "1" ]; then
  fail_and_rollback "The database did not become healthy within 60s." \
    "Inspect it: $(compose_display) logs db" \
    "A corrupt pgdata volume or bad POSTGRES_PASSWORD are the usual causes." \
    "The stack is fully down — fix the cause and re-run: ./launch.sh"
fi
# The migration loop itself now lives in ./migrate.sh (also usable standalone against a
# running stack that isn't being launched — see its header). --keep-going preserves this
# script's historical behaviour exactly: a failing migration is reported but never stops
# the launch, so `|| true` keeps that true here too.
./migrate.sh --keep-going || true

echo "==> bringing the rest of the stack up"
if ! compose up "${UP_ARGS[@]}"; then
  fail_and_rollback "Bringing the full stack up failed part-way." \
    "Inspect the failing service: $(compose_display) logs <service>" \
    "The stack is fully down — fix the cause and re-run: ./launch.sh"
fi

echo "==> stack status"
compose ps

echo
echo "==> access points (all on localhost — no proxy)"
printf '    %-12s %s\n' "Admin UI" "http://localhost:3000/"
printf '    %-12s %s\n' "API"      "http://localhost:8080/  (health: /health)"
printf '    %-12s %s\n' "Stream"   "http://localhost:8000/stream"
printf '    %-12s %s\n' "Icecast"  "http://localhost:8000/  (status page)"
