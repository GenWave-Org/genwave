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
#   ./launch.sh --pinned     demo/appliance flow: pull -> db up (--no-recreate) + health
#                             wait -> migrate.sh -> up -d against compose.yaml +
#                             compose.demo.yaml. NEVER builds — it's meant for a box that
#                             only ever runs published GHCR images. Works on a fresh box
#                             (gh-#305) AND as the upgrade path: --no-recreate starts an
#                             absent/stopped db but never touches a running one, so an
#                             upgrade still restarts nothing before migrations pass. See
#                             DEPLOYMENT.md's "Applying migrations".
#   ./launch.sh --with a,b   merge a,b into COMPOSE_PROFILES (env var, else .env's value)
#                             for this launch's compose invocations.
#   ./launch.sh --piper-only low-memory/no-kokoro topology (gh-#242): merge
#                             compose.piper-only.yaml — always LAST, so its kokoro
#                             removal + depends_on reset win — into whichever file set
#                             the flow uses (dev, or --pinned's demo pair). kokoro never
#                             runs; every TTS render routes to the piper sidecar.
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
PIPER_ONLY=0
DRY_RUN=0
WITH=""

while [ $# -gt 0 ]; do
  case "$1" in
    --pinned)
      PINNED=1
      shift
      ;;
    --piper-only)
      PIPER_ONLY=1
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
# --piper-only (gh-#242): the no-kokoro overlay merges LAST so its kokoro removal +
# depends_on reset win whatever came before. `-f` disables compose's auto-discovery, so
# the dev flow's implicit compose.yaml has to be named explicitly here.
if [ "$PIPER_ONLY" = "1" ]; then
  if [ "${#COMPOSE_ARGS[@]}" -eq 0 ]; then
    COMPOSE_ARGS=(-f compose.yaml)
    MIGRATE_ARGS=(-f compose.yaml)
  fi
  COMPOSE_ARGS+=(-f compose.piper-only.yaml)
  MIGRATE_ARGS+=(-f compose.piper-only.yaml)
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

# --- gh-#309: make bare `docker compose` in this directory agree with this launch --------
# The file stack this launch ran against, in COMPOSE_FILE's own ':'-separated form (the
# default COMPOSE_PATH_SEPARATOR on Linux). The dev flow names compose.yaml explicitly
# rather than leaving it empty: a previous --pinned run's value must be REPLACED, not
# inherited, or a plain ./launch.sh would leave the box pointing at the demo pair.
compose_file_value() {
  if [ "${#COMPOSE_ARGS[@]}" -eq 0 ]; then
    echo "compose.yaml"
  else
    printf '%s\n' "${COMPOSE_ARGS[@]}" | grep -v '^-f$' | paste -sd:
  fi
}

# `./launch.sh --pinned` runs against compose.yaml + compose.demo.yaml, but a bare
# `docker compose down` here loads only compose.yaml — so every service that exists ONLY
# in an overlay (caddy, ollama, ollama-init) is invisible to it, survives the teardown and
# is left running (gh-#309's repro exactly). Recording the stack in .env — COMPOSE_FILE is
# read from the project directory automatically — makes bare down/ps/logs target what was
# actually launched, with no flags to remember.
#
# Written only AFTER a successful up: a stack that never came up is not this box's state.
# launch.sh's own calls always pass explicit -f, which outranks this variable, so the value
# can never feed back into this script.
#
# Profile-gated services (admin/tunnel/logging) are a SEPARATE axis and deliberately not
# handled here: COMPOSE_PROFILES is documented as per-launch under --with, and persisting
# it would silently change that flag's meaning. A box wanting a standing set puts it in
# .env itself — which this script already reads as the base for --with. Until then a bare
# `down` still leaves profile-gated containers behind; `--remove-orphans` clears them.
persist_compose_file() {
  local value tmp
  value="$(compose_file_value)"

  [ -f .env ] || touch .env
  if grep -qE '^COMPOSE_FILE=' .env; then
    # In place, preserving every other line and their order.
    tmp="$(mktemp)"
    awk -v v="COMPOSE_FILE=$value" '/^COMPOSE_FILE=/ {print v; next} {print}' .env > "$tmp"
    cat "$tmp" > .env   # cat, not mv: keeps the operator's own file mode and ownership
    rm -f "$tmp"
  else
    # A .env whose last line has no newline would otherwise get this appended to it.
    if [ -s .env ] && [ "$(tail -c1 .env | wc -l)" -eq 0 ]; then
      printf '\n' >> .env
    fi
    printf 'COMPOSE_FILE=%s\n' "$value" >> .env
  fi

  echo "==> recorded COMPOSE_FILE=$value in .env — bare 'docker compose down' now matches this launch (gh-#309)"
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

# Poll the db container's healthcheck until healthy (up to 30x2s = 60s). Returns non-zero
# on a missing container or timeout; the caller decides what failure means (dev flow rolls
# the stack back down, pinned flow reports and leaves everything as-is).
wait_db_healthy() {
  local db_cid
  db_cid="$(compose ps -q db)"
  [ -n "$db_cid" ] || return 1
  for _ in $(seq 1 30); do
    if [ "$(docker inspect "$db_cid" --format '{{.State.Health.Status}}' 2>/dev/null)" = "healthy" ]; then
      return 0
    fi
    sleep 2
  done
  return 1
}

if [ "$PINNED" = "1" ]; then
  # --- pinned/demo flow: pull -> db up -> migrate.sh -> up -d, never builds --------------
  # Re-run hint carrying the flags this launch was actually given — the bare "--pinned"
  # hint used to drop --piper-only/--with, steering the user into a different topology
  # (gh-#305: kokoro included, on the 4GB box that opted out of it).
  RELAUNCH="./launch.sh --pinned"
  [ "$PIPER_ONLY" = "1" ] && RELAUNCH="$RELAUNCH --piper-only"
  [ -n "$WITH" ] && RELAUNCH="$RELAUNCH --with $WITH"

  if [ "$DRY_RUN" = "1" ]; then
    plan_line "$(compose_display) pull"
    plan_line "$(compose_display) up -d --no-recreate db"
    plan_line "$(compose_display) ps -q db"
    plan_line "docker inspect <db container> --format {{.State.Health.Status}} (poll until healthy, up to 30x2s)"
    plan_line "./migrate.sh ${MIGRATE_ARGS[*]}"
    plan_line "$(compose_display) up -d --remove-orphans"
    plan_line "record COMPOSE_FILE=$(compose_file_value) in .env (gh-#309)"
    plan_line "docker image prune -af --filter until=168h (success-path hygiene, gh-#441)"
    plan_line "docker builder prune -af"
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
      "Check network/GHCR reachability, then re-run: $RELAUNCH" \
      "The previous images are still local; the stack keeps running as-is."
  fi

  # gh-#305: migrate.sh only ever talks to an already-running db, but on a fresh box
  # (first appliance boot — nothing has ever started) there isn't one, and the launch
  # deadlocked here forever. --no-recreate starts an absent/stopped db and leaves a
  # running one completely untouched — an upgrade still restarts nothing onto the new
  # images before migrations pass; a first boot finally gets a db to migrate.
  echo "==> ensuring the database is up (a running db is never recreated here)"
  if ! compose up -d --no-recreate db; then
    preflight_fail "The database service failed to start — nothing else was touched." \
      "Inspect it: $(compose_display) logs db" \
      "Fix the cause and re-run: $RELAUNCH"
  fi
  if ! wait_db_healthy; then
    preflight_fail "The database did not become healthy within 60s — the stack was NOT restarted onto the new images." \
      "Inspect it: $(compose_display) logs db" \
      "A corrupt pgdata volume or bad POSTGRES_PASSWORD are the usual causes." \
      "Fix the cause and re-run: $RELAUNCH"
  fi

  echo "==> applying schema migrations against the running db"
  if ! ./migrate.sh "${MIGRATE_ARGS[@]}"; then
    preflight_fail "Schema migration failed — the stack was NOT restarted onto the new images." \
      "Inspect the db: $(compose_display) logs db" \
      "Migrations are idempotent — fix the cause and re-run: $RELAUNCH"
  fi

  echo "==> bringing the stack up"
  # A failed partial up on an appliance is deliberately NOT rolled back with `down`:
  # whatever is still broadcasting keeps broadcasting (never-silent outranks tidiness).
  # Report precisely and say how to proceed instead.
  #
  # --remove-orphans (T148 review finding F6, SPEC F99.3): tears down any container whose
  # SERVICE no longer exists in this launch's file stack at all — e.g. a box upgrading across a
  # release that deletes/renames a service. Verified (docker-linux-ops, live daemon,
  # 2026-08-14): Compose's own definition of "orphan" is narrower than "not currently selected
  # by profile" — a container for a service that's still DEFINED but merely profile-gated OFF
  # (piper's `profiles: ["fallback"]`) is untouched by this flag, confirmed against a scratch
  # compose file with a profile-gated service turned on then off across two `up -d
  # --remove-orphans` runs. So this flag alone does NOT stop a previously-opted-in `piper`
  # sidecar when an operator later removes `--with fallback` — see DEPLOYMENT.md's failover
  # section for the one-time manual step that actually does. Still worth adding: safe (never
  # removes an active service — verified against this box's own compose.yaml +
  # compose.demo.yaml render), and it IS the mechanism for the case it does cover.
  if ! compose up -d --remove-orphans; then
    # `ps -a`, not `ps`: plain `ps` lists RUNNING containers only — so it omitted exactly the
    # one service this message tells the operator to go inspect. A container the daemon
    # refused to START (stale network id after an unclean host power cut, a port already
    # bound, a bad mount) never reaches Up, so the "status above" read all-green under a
    # "failed part-way" verdict with no failing service anywhere in it.
    compose ps -a || true
    preflight_fail "Bringing the stack up failed part-way (status above — look for a service NOT in an Up state)." \
      "Inspect the failing service: $(compose_display) logs <service>" \
      "A container that never started has no logs — read the daemon's reason instead: docker inspect <container> --format '{{.State.Error}}'" \
      "Re-run when fixed: $RELAUNCH (up is idempotent — it converges the rest)."
  fi

  persist_compose_file

  # gh-#441: superseded release images accumulate ~1.5 GB per release and nothing else ever
  # prunes them — 46 GB of dead tags filled the demo box's disk mid-deploy (2026-08-09), and an
  # SD-card Pi hits that wall far sooner. Success path only: every failure above bails via
  # preflight_fail before reaching here, so a failed upgrade never touches the previous images
  # (they are what is still running). Best-effort — hygiene never fails a launch that already
  # succeeded. `until=168h` keys on image CREATED time, so everything in use plus roughly the
  # last week of releases survives for instant rollback; older tags go. The builder cache is
  # pure waste on a --pinned box, which never builds (BUILD=1 + --pinned errors at parse time).
  echo "==> pruning superseded images (kept: in-use + last 7 days)"
  docker image prune -af --filter "until=168h" | tail -1 || true
  docker builder prune -af >/dev/null 2>&1 || true

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
  # Same no-dangling-space discipline as compose_display: MIGRATE_ARGS is empty on the
  # plain dev flow, populated under --piper-only.
  if [ "${#MIGRATE_ARGS[@]}" -eq 0 ]; then
    plan_line "./migrate.sh --keep-going"
  else
    plan_line "./migrate.sh --keep-going ${MIGRATE_ARGS[*]}"
  fi
  plan_line "$(compose_display) up ${UP_ARGS[*]}"
  plan_line "record COMPOSE_FILE=$(compose_file_value) in .env (gh-#309)"
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
# gh-#19: falling through silently used to let migrate fail (or the api crash-loop on a
# missing column) with no advice — an unhealthy db after 60s is now a hard, explained stop.
if ! wait_db_healthy; then
  fail_and_rollback "The database did not become healthy within 60s." \
    "Inspect it: $(compose_display) logs db" \
    "A corrupt pgdata volume or bad POSTGRES_PASSWORD are the usual causes." \
    "The stack is fully down — fix the cause and re-run: ./launch.sh"
fi
# The migration loop itself now lives in ./migrate.sh (also usable standalone against a
# running stack that isn't being launched — see its header). --keep-going preserves this
# script's historical behaviour exactly: a failing migration is reported but never stops
# the launch, so `|| true` keeps that true here too. MIGRATE_ARGS is empty on the plain
# dev flow (compose project auto-detection, byte-identical behaviour) and carries the
# overlay file selection under --piper-only.
./migrate.sh --keep-going "${MIGRATE_ARGS[@]}" || true

echo "==> bringing the rest of the stack up"
if ! compose up "${UP_ARGS[@]}"; then
  fail_and_rollback "Bringing the full stack up failed part-way." \
    "Inspect the failing service: $(compose_display) logs <service>" \
    "The stack is fully down — fix the cause and re-run: ./launch.sh"
fi

persist_compose_file

echo "==> stack status"
compose ps

echo
echo "==> access points (all on localhost — no proxy)"
printf '    %-12s %s\n' "Admin UI" "http://localhost:3000/"
printf '    %-12s %s\n' "API"      "http://localhost:8080/  (health: /health)"
printf '    %-12s %s\n' "Stream"   "http://localhost:8000/stream"
printf '    %-12s %s\n' "Icecast"  "http://localhost:8000/  (status page)"
