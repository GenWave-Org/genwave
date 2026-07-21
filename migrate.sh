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
#   --keep-going  Run every migration even after one fails, matching launch.sh's
#                 historical behaviour (see "Failure handling" below). Without this
#                 flag (the default), the run stops at the first failing migration.
#
# Failure handling:
#   Before launch.sh extracted this loop, a failing migration printed "FAILED" and the
#   script carried on to the next one — nothing ever stopped, and launch.sh's overall
#   exit code was unaffected. That is preserved exactly via --keep-going, which is how
#   launch.sh invokes this script (byte-identical dev-flow output/behaviour).
#
#   Run standalone (the demo-box case this script exists for), the default is now
#   fail-fast: the first failing migration stops the run and migrate.sh exits non-zero.
#   A conscious improvement over the old always-continue behaviour — silently limping
#   past a schema migration failure on a box nobody is watching interactively is worse
#   than stopping loudly. --keep-going is there for anyone who wants the old behaviour.
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
  echo "==> --dry-run: migrations that would run (sorted; db/01-*.sh excluded — first-boot only)"
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

# --- the loop itself — extracted from launch.sh verbatim in shape/output ---------------
echo "==> applying in-place schema migrations (idempotent)"

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
  if ! run_migration "$migration"; then
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

# Keep launch.sh's dev-flow output byte-identical: with --keep-going (how launch.sh calls
# this script) there was never a closing line here — the next output launch.sh itself
# prints is "==> bringing the rest of the stack up". Only announce completion standalone.
if [ "$KEEP_GOING" != "1" ]; then
  echo "==> schema migrations up to date"
fi
