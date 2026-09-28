#!/usr/bin/env bash
# migrate.sh — apply the idempotent db/*-migration.sh scripts to a RUNNING db service.
#
# Extracted from launch.sh's migration loop (which now delegates here — see below) so a
# box that only ever runs pulled GHCR images under compose.yaml + compose.demo.yaml (the
# demo/appliance topology; see DEPLOYMENT.md) has a sanctioned way to pick up new
# migrations WITHOUT launch.sh's dev-stack assumptions (source build, teardown, full
# relaunch). This script only ever talks to an already-running db service — it never
# brings anything up or down.
#
# This is deliberately "bash scripts as baseline" — a real migration runner (DbUp, grate,
# ...) is tracked as future work in gh-#12; until that lands, db/NN-*-migration.sh +
# this runner are the whole story.
#
# Usage:
#   ./migrate.sh [-f FILE]... [--dry-run] [--keep-going] [--help]
#
#   Compose project / file selection — the same mechanisms `docker compose` itself
#   understands, nothing migrate.sh invents:
#     -f, --file FILE     Passed straight through to `docker compose` (repeatable).
#     COMPOSE_FILE (env)  Honored automatically — we invoke plain `docker compose` when
#                         no -f is given, so compose's own env-var resolution applies.
#     (neither)           Plain `docker compose` — project auto-detection from the
#                         compose.yaml in this directory. This is the dev-stack case;
#                         it's what launch.sh's delegated call below relies on.
#
#   Demo/appliance box (compose.yaml + compose.demo.yaml, per DEPLOYMENT.md):
#     ./migrate.sh -f compose.yaml -f compose.demo.yaml
#
#   --dry-run     List which db/*-migration.sh scripts would run, sorted, and exit 0.
#                 Pure local glob — touches no docker/compose state, so it works even
#                 against a stack that isn't up yet.
#   --keep-going  Run every migration even after one fails (see "Failure handling" below).
#                 Without this flag (the default), the run stops at the first failing
#                 migration.
#
# Failure handling:
#   The default (no --keep-going) is fail-fast: the first failing migration stops the run,
#   prints its own captured output to stderr, and migrate.sh exits non-zero. Silently
#   limping past a schema migration failure is worse than stopping loudly.
#
#   gh-#770/STORY-436: this is also how launch.sh calls this script now — both the pinned
#   flow and the dev flow run it fail-fast, and a failing migration stops the launch itself
#   (see launch.sh's own comment above its call), leaving the db up for inspection instead
#   of carrying on regardless.
#
#   --keep-going restores the old always-continue behaviour (every migration runs even
#   after an earlier one fails, and failures are reported but never stop the run) — it
#   remains available for anyone invoking migrate.sh standalone who wants that.
#
# Schema journal (SPEC F211.3, STORY-484, PLAN T591):
#   Before the loop, a preamble creates station.schema_migration if it isn't there yet (an
#   upgrading pre-F211 box) — mirrored in db/06 too, so a fresh box has it without ever
#   running this preamble. After each migration this script itself applies successfully, it
#   upserts one row: script = the file's base name, applied_at = now(), app_version =
#   $GW_VERSION if set, else null. The migrations themselves are never touched — the journal
#   lives here, not in db/*-migration.sh. GW_VERSION and the script name cross into the db
#   container only via `-e` env / psql `-v` bind variables, never string-interpolated into
#   SQL text. A failed journal write is treated exactly like a failed migration (see
#   "Failure handling" above) — it is never silently skipped.
#
# Exit: 0 — every migration ran (or --dry-run listed what would have)
#       1 — a migration failed (fail-fast: the first one; --keep-going: any of them),
#           or the db service isn't running / reachable
#       2 — usage error (bad argument)
set -euo pipefail
cd "$(dirname "$0")"

DRY_RUN=0
KEEP_GOING=0
COMPOSE_ARGS=()

usage() {
  awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "$0"
}

while [ $# -gt 0 ]; do
  case "$1" in
    -f|--file)
      [ $# -ge 2 ] || { echo "migrate.sh: $1 needs a path" >&2; exit 2; }
      COMPOSE_ARGS+=(-f "$2")
      shift 2
      ;;
    -f=*|--file=*)
      COMPOSE_ARGS+=(-f "${1#*=}")
      shift
      ;;
    --dry-run)
      DRY_RUN=1
      shift
      ;;
    --keep-going)
      KEEP_GOING=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "migrate.sh: unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

compose() {
  docker compose "${COMPOSE_ARGS[@]}" "$@"
}

# Human-readable rendering of the compose invocation, for error messages — avoids a
# dangling double space when COMPOSE_ARGS is empty (the plain-`docker compose` case).
compose_display() {
  if [ "${#COMPOSE_ARGS[@]}" -eq 0 ]; then
    echo "docker compose"
  else
    echo "docker compose ${COMPOSE_ARGS[*]}"
  fi
}

list_migrations() {
  for migration in db/*-migration.sh; do
    [ -f "$migration" ] || continue
    printf '%s\n' "$migration"
  done
}

if [ "$DRY_RUN" = "1" ]; then
  echo "==> --dry-run: Migrations that would run (sorted; db/01-*.sh excluded — first-boot only)"
  mapfile -t to_run < <(list_migrations)
  if [ "${#to_run[@]}" -eq 0 ]; then
    echo "    (none found)"
  else
    printf '    %s\n' "${to_run[@]}"
  fi
  exit 0
fi

# --- the db service must already be running — this script never starts/stops anything ---
err_file="$(mktemp)"
trap 'rm -f "$err_file"' EXIT

if ! db_cid="$(compose ps -q db 2>"$err_file")"; then
  echo "migrate.sh: '$(compose_display) ps -q db' failed:" >&2
  sed 's/^/  /' "$err_file" >&2
  echo "migrate.sh: check the compose file/project selection (-f, or COMPOSE_FILE env)." >&2
  exit 1
fi

if [ -z "$db_cid" ]; then
  echo "migrate.sh: db service is not running under this compose project." >&2
  echo "  start it first, e.g.: $(compose_display) up -d db" >&2
  exit 1
fi

if [ "$(docker inspect "$db_cid" --format '{{.State.Running}}' 2>/dev/null)" != "true" ]; then
  echo "migrate.sh: db container ($db_cid) exists but is not running." >&2
  exit 1
fi

# --- schema journal preamble (SPEC F211.3, STORY-484, PLAN T591) -----------------------
# station.schema_migration already exists on every fresh box (db/06's own fresh-init mirror,
# gh-#618); this only matters on a box upgrading from before F211, where migrate.sh has
# never created it yet. No dynamic data here, so no bind variables are needed. Relies on db/06
# having already created the station_svc role and the station schema itself (the SET ROLE below
# would fail otherwise) — true on every public box: db/06 runs as a Postgres init script on every
# fresh install, and this script only ever runs against an already-initialized (db/01+db/06, at
# minimum) box in the first place.
ensure_schema_journal() {
  compose exec -T db bash -s <<-'BASH'
	set -euo pipefail
	: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"
	psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
		SET ROLE station_svc;
		SET search_path = station;
		CREATE TABLE IF NOT EXISTS station.schema_migration (
		  script      text        NOT NULL PRIMARY KEY CHECK (script <> ''),
		  applied_at  timestamptz NOT NULL,
		  app_version text
		);
		SQL
	BASH
}

if ! ensure_schema_journal; then
  echo "migrate.sh: failed to prepare station.schema_migration — check 'docker compose logs db'" >&2
  exit 1
fi

# One upsert per successfully-applied migration. GW_VERSION and the script's own base name
# cross into the container only via `-e` (compose exec) and psql `-v` bind variables — never
# string-interpolated into the SQL text itself, since GW_VERSION is an operator-controlled
# shell env var. `nullif(:'app_version', '')` turns an unset GW_VERSION into SQL NULL.
journal_migration() {
  local migration="$1" script
  script="${migration##*/}"
  if [ -z "$script" ]; then
    echo "migrate.sh: could not derive a script name from '$migration' — refusing to journal with an empty key" >&2
    return 1
  fi
  if ! compose exec -T -e GW_SCRIPT="$script" -e GW_VERSION="${GW_VERSION:-}" db bash -s <<-'BASH'
	set -euo pipefail
	: "${POSTGRES_USER:?POSTGRES_USER must be set}" "${POSTGRES_DB:?POSTGRES_DB must be set}"
	psql -v ON_ERROR_STOP=1 -v script="$GW_SCRIPT" -v app_version="$GW_VERSION" \
	  --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-'SQL'
		SET ROLE station_svc;
		SET search_path = station;
		INSERT INTO station.schema_migration (script, applied_at, app_version)
		VALUES (:'script', now(), NULLIF(:'app_version', ''))
		ON CONFLICT (script) DO UPDATE
		  SET applied_at = excluded.applied_at,
		      app_version = excluded.app_version;
		SQL
	BASH
  then
    echo "migrate.sh: failed to journal $script — check 'docker compose logs db'" >&2
    return 1
  fi
}

# --- the loop itself — extracted from launch.sh verbatim in shape/output ---------------
echo "==> Applying in-place schema migrations (idempotent)"

run_migration() {
  local migration="$1" output
  printf '    %s ... ' "$migration"
  if output="$(compose exec -T db bash -s < "$migration" 2>&1)"; then
    echo "ok"
    return 0
  fi
  echo "FAILED — check 'docker compose logs db'"
  # --keep-going preserves launch.sh's historical silence here (output was always
  # discarded); fail-fast mode surfaces the migration's own stderr/stdout, which is
  # usually the actual psql error and far more useful than pointing at the server log.
  if [ "$KEEP_GOING" != "1" ] && [ -n "$output" ]; then
    printf '%s\n' "$output" | sed 's/^/      /' >&2
  fi
  return 1
}

any_failed=0
for migration in db/*-migration.sh; do
  [ -f "$migration" ] || continue
  if run_migration "$migration" && journal_migration "$migration"; then
    :
  else
    any_failed=1
    if [ "$KEEP_GOING" != "1" ]; then
      echo "migrate.sh: stopping — a migration failed and --keep-going was not passed." >&2
      exit 1
    fi
  fi
done

if [ "$any_failed" = "1" ]; then
  exit 1
fi

# This closing line is suppressed only under --keep-going. gh-#770/STORY-436: launch.sh
# (both flows) now calls migrate.sh WITHOUT that flag, so "==> Schema migrations up to
# date" prints there too, just before launch.sh's next "==> Bringing …" banner —
# that's fine, not something to guard against. --keep-going (standalone use only, now that
# launch.sh no longer passes it) is the one case that still wants this line skipped.
if [ "$KEEP_GOING" != "1" ]; then
  echo "==> Schema migrations up to date"
fi
