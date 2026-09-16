#!/usr/bin/env bash
# stack_gate.sh — full-stack release gate for a tagged GenWave build (SPEC F178, STORY-444).
#
# Proves a published `home-<tag>` image set actually boots and serves before that tag reaches a
# box: each requested leg runs a real docker compose stack pinned to the tag, inside its OWN
# scratch copy of the checkout — never the caller's working tree, never the caller's `.env`.
#
#   --fresh          a clean install from nothing (setup.sh --yes, launch.sh --pinned, health/
#                     on-air waits — the leg itself lands in T487)
#   --upgrade        an existing station upgraded onto the tag (T491+)
#   --capture        captures stream audio during the leg for a loudness/crossfade check
#                     (T491+; requires --fresh)
#   --chaos          fault injection during the leg (T491+; requires --capture)
#   --from <vX.Y.Z>  the tag the upgrade leg starts from (validated, stored; unused until T491)
#   --report <dir>   write gate-report.md + gate-report.json there (default: .)
#
# This file (PLAN T486) is the skeleton: arg parsing, the prerequisite probe, isolation, the
# scratch + compose.gate.yaml generator, the PROJECTS/trap teardown, and the report writer. The
# fresh leg's actual orchestration (setup/launch/health/on-air) lands in T487 — until then
# `--fresh` always records `ran/failed` so the skeleton is provable end-to-end without a live
# stack. `--upgrade`/`--capture`/`--chaos` are report-only "skipped" rows until T491+.
#
# Usage: tools/gate/stack_gate.sh --tag <vX.Y.Z> [--fresh] [--upgrade] [--capture] [--chaos]
#                                  [--from <vX.Y.Z>] [--report <dir>]
# Exit codes: 0 = every requested leg passed (or none were requested); 1 = a leg failed (see the
#             report); 2 = usage error or a missing prerequisite (docker, ffmpeg, jq; gh only
#             with --upgrade) — reached before any leg runs, so no report is written.
#
# Isolation (F178.1): never reads the caller's .env; strips GW_*/COMPOSE_*/the six .env secret
# names from its own exported environment before any docker call, so a developer's shell leaking
# a real ADMIN_PASSWORD or a stray GW_* override can never reach the stack under test.

set -euo pipefail

prog="$(basename "$0")"
root="$(cd "$(dirname "$0")/../.." && pwd)"

usage_error() {
  echo "$prog: $1" >&2
  exit 2
}

require_prereq() {
  command -v "$1" >/dev/null 2>&1 || { echo "$prog: missing prerequisite: $1" >&2; exit 2; }
}

# ---------------------------------------------------------------------------------------------
# Teardown: every scratch this run creates, and every compose project a leg brings up, is torn
# down on EXIT — success or failure alike. Populated by the legs below; empty (a no-op loop) when
# no leg runs, e.g. a bare `--tag` with no leg flags.
# ---------------------------------------------------------------------------------------------
PROJECTS=()      # "scratch_dir|project_name" — down -v runs from inside scratch_dir
SCRATCH_DIRS=()  # every mktemp -d this run made, regardless of which leg it belongs to

# shellcheck disable=SC2317 # false positive: only called indirectly via `trap cleanup EXIT`,
# which shellcheck's reachability analysis doesn't follow (documented SC2317 caveat).
cleanup() {
  local entry scratch project
  for entry in "${PROJECTS[@]}"; do
    scratch="${entry%%|*}"
    project="${entry#*|}"
    ( cd "$scratch" && docker compose -p "$project" \
        -f compose.yaml -f compose.piper-only.yaml -f compose.gate.yaml down -v ) || true
  done
  for scratch in "${SCRATCH_DIRS[@]}"; do
    rm -rf "$scratch"
  done
}
trap cleanup EXIT

# ---------------------------------------------------------------------------------------------
# Arg parsing — usage errors exit 2 with a one-line reason on stderr.
# ---------------------------------------------------------------------------------------------
TAG=""
FROM_TAG=""
REPORT_DIR="."
DO_FRESH=0
DO_UPGRADE=0
DO_CAPTURE=0
DO_CHAOS=0
declare -A seen=()

require_once() {
  [ -z "${seen[$1]:-}" ] || usage_error "$1 given more than once"
  seen[$1]=1
}

while [ $# -gt 0 ]; do
  case "$1" in
    --tag)
      require_once --tag
      [ $# -ge 2 ] || usage_error "--tag needs a value"
      TAG="$2"; shift 2 ;;
    --from)
      require_once --from
      [ $# -ge 2 ] || usage_error "--from needs a value"
      FROM_TAG="$2"; shift 2 ;;
    --report)
      require_once --report
      [ $# -ge 2 ] || usage_error "--report needs a value"
      REPORT_DIR="$2"; shift 2 ;;
    --fresh)    require_once --fresh;    DO_FRESH=1;    shift ;;
    --upgrade)  require_once --upgrade;  DO_UPGRADE=1;  shift ;;
    --capture)  require_once --capture;  DO_CAPTURE=1;  shift ;;
    --chaos)    require_once --chaos;    DO_CHAOS=1;    shift ;;
    *) usage_error "unknown flag: $1" ;;
  esac
done

[ -n "$TAG" ] || usage_error "--tag is required"
[[ "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || usage_error "--tag must look like vX.Y.Z: $TAG"
if [ -n "$FROM_TAG" ] && [[ ! "$FROM_TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  usage_error "--from must look like vX.Y.Z: $FROM_TAG"
fi
[ "$DO_CAPTURE" = 0 ] || [ "$DO_FRESH" = 1 ]   || usage_error "--capture requires --fresh"
[ "$DO_CHAOS" = 0 ]   || [ "$DO_CAPTURE" = 1 ] || usage_error "--chaos requires --capture"

# ---------------------------------------------------------------------------------------------
# Prerequisite probe — binaries on THIS host; docker compose's v2-ness is checked per leg, inside
# that leg's own scratch (see run_fresh_leg), since verifying it means an actual docker call and
# no docker call may run against the caller's checkout.
# ---------------------------------------------------------------------------------------------
require_prereq docker
require_prereq ffmpeg
require_prereq jq
[ "$DO_UPGRADE" = 1 ] && require_prereq gh

# ---------------------------------------------------------------------------------------------
# Isolation (F178.1) — strip GW_*/COMPOSE_*/the six .env secret names from OUR OWN exported
# environment before any docker call. The test harness already sanitizes the child's env for
# specs; this makes the same guarantee true for a human running the gate from their own shell.
# `compgen -A export` enumerates every currently-exported name (a bash builtin — no `env`/`printenv`
# parsing needed).
# ---------------------------------------------------------------------------------------------
strip_isolation_vars() {
  local name
  for name in $(compgen -A export); do
    case "$name" in
      GW_*|COMPOSE_*|POSTGRES_PASSWORD|LIBRARY_DB_PASSWORD|STATION_DB_PASSWORD| \
      ICECAST_SOURCE_PASSWORD|ICECAST_ADMIN_PASSWORD|ADMIN_PASSWORD)
        unset "$name" ;;
    esac
  done
}
strip_isolation_vars

# ---------------------------------------------------------------------------------------------
# Leg state — status is one of passed/failed/skipped; detail is the first failing assertion
# (failed) or the skip reason (skipped), empty for passed.
# ---------------------------------------------------------------------------------------------
declare -A LEG_STATUS=([fresh]="" [upgrade]="" [capture]="" [chaos]="")
declare -A LEG_DETAIL=([fresh]="" [upgrade]="" [capture]="" [chaos]="")

# Exactly five `image:` lines, one per repo-built service, nothing else under them — matches
# compose.pinned.yaml's naming (api's image is bare `genwave`; the other four are `genwave-<svc>`).
write_compose_gate_overlay() {
  local scratch="$1" tag="$2"
  cat > "$scratch/compose.gate.yaml" <<EOF
services:
  api:
    image: ghcr.io/genwave-org/genwave:home-${tag}
  engine:
    image: ghcr.io/genwave-org/genwave-engine:home-${tag}
  icecast:
    image: ghcr.io/genwave-org/genwave-icecast:home-${tag}
  admin_ui:
    image: ghcr.io/genwave-org/genwave-admin-ui:home-${tag}
  piper:
    image: ghcr.io/genwave-org/genwave-piper:home-${tag}
EOF
}

# ScriptProcess's sandboxed test PATH has no `openssl`, so the 8 hex chars come from tr + head
# (both are always on PATH) rather than `openssl rand -hex 4`.
gate_project_name() {
  printf 'gw-gate-%s-%s' "$1" "$(tr -dc 'a-f0-9' < /dev/urandom | head -c 8)"
}

run_fresh_leg() {
  local scratch project version_output major
  scratch="$(mktemp -d)"
  SCRATCH_DIRS+=("$scratch")

  rsync -a --exclude .env --exclude .git "$root/" "$scratch/"

  # The leg's first docker call, from inside the scratch — never from the caller's checkout.
  # "docker compose v2" (SPEC F178.1) means the `docker compose` PLUGIN, as opposed to the legacy
  # `docker-compose` v1 binary — any plugin major >= 2 qualifies, so this parses the major out of
  # `docker compose version` rather than pinning the literal string "v2" (a real box's plugin is
  # newer than the fixture's stubbed "v2.29.0" and must still pass).
  if ! version_output="$(cd "$scratch" && docker compose version)"; then
    LEG_STATUS[fresh]="failed"; LEG_DETAIL[fresh]="docker compose version failed"
    return
  fi
  if [[ "$version_output" =~ [vV]?([0-9]+)\.[0-9]+\.[0-9]+ ]]; then
    major="${BASH_REMATCH[1]}"
  else
    LEG_STATUS[fresh]="failed"
    LEG_DETAIL[fresh]="could not parse docker compose version: $version_output"
    return
  fi
  if [ "$major" -lt 2 ]; then
    LEG_STATUS[fresh]="failed"
    LEG_DETAIL[fresh]="docker compose plugin v2+ required, got: $version_output"
    return
  fi

  write_compose_gate_overlay "$scratch" "$TAG"

  project="$(gate_project_name fresh)"
  PROJECTS+=("$scratch|$project")

  # T487 replaces this: the fresh leg proper — setup.sh --yes, launch.sh --pinned against
  # compose.yaml + compose.piper-only.yaml + compose.gate.yaml under $project, health/on-air
  # waits, compose logs attached on a miss. For now the leg always fails so the skeleton
  # (scratch, overlay, project naming, teardown, report) is provable without a live stack.
  LEG_STATUS[fresh]="failed"
  LEG_DETAIL[fresh]="fresh leg not implemented (T487)"
}

if [ "$DO_FRESH" = 1 ]; then
  run_fresh_leg
else
  LEG_STATUS[fresh]="skipped"; LEG_DETAIL[fresh]="--fresh not given"
fi

if [ "$DO_UPGRADE" = 1 ]; then
  LEG_STATUS[upgrade]="skipped"; LEG_DETAIL[upgrade]="not implemented until T491"
else
  LEG_STATUS[upgrade]="skipped"; LEG_DETAIL[upgrade]="--upgrade not given"
fi

if [ "$DO_CAPTURE" = 1 ]; then
  LEG_STATUS[capture]="skipped"; LEG_DETAIL[capture]="not implemented until T491"
else
  LEG_STATUS[capture]="skipped"; LEG_DETAIL[capture]="--capture not given"
fi

if [ "$DO_CHAOS" = 1 ]; then
  LEG_STATUS[chaos]="skipped"; LEG_DETAIL[chaos]="not implemented until T491"
else
  LEG_STATUS[chaos]="skipped"; LEG_DETAIL[chaos]="--chaos not given"
fi

# ---------------------------------------------------------------------------------------------
# Report — gate-report.md (a small table + measurements + the fixed manual-evidence line) and
# its machine twin gate-report.json. Written on every path that reaches here, before exiting.
# ---------------------------------------------------------------------------------------------

# `"manual: ` occurrences across tests/**/*.cs — the fixed line's N. The inner `|| true` keeps a
# zero-match day (every fact automated) from tripping `set -e`/pipefail: grep exits 1 when it
# finds nothing, and pipefail promotes that over wc's own (successful) exit 0.
count_manual_facts() {
  (grep -rho '"manual: ' "$root/tests" --include='*.cs' || true) | wc -l
}

leg_row_md() {
  local leg="$1" status="$2" detail="$3"
  case "$status" in
    passed)  printf '| %s | ran/passed | - |\n' "$leg" ;;
    failed)  printf '| %s | ran/failed | %s |\n' "$leg" "$detail" ;;
    skipped) printf '| %s | skipped (%s) | - |\n' "$leg" "$detail" ;;
  esac
}

leg_json() {
  local status="$1" detail="$2" first_failure="" skipped_by=""
  case "$status" in
    failed)  first_failure="$detail" ;;
    skipped) skipped_by="$detail" ;;
  esac
  jq -n --arg status "$status" --arg first_failure "$first_failure" --arg skipped_by "$skipped_by" '
    {
      status: $status,
      first_failure: (if $first_failure == "" then null else $first_failure end),
      skipped_by: (if $skipped_by == "" then null else $skipped_by end),
      measurements: {}
    }'
}

first_failure_across_legs() {
  local leg
  for leg in fresh upgrade capture chaos; do
    if [ "${LEG_STATUS[$leg]}" = "failed" ]; then
      printf '%s' "${LEG_DETAIL[$leg]}"
      return
    fi
  done
}

write_report() {
  local out_dir="$1" manual_facts leg
  mkdir -p "$out_dir"
  manual_facts="$(count_manual_facts)"

  {
    printf '# Stack gate report — %s\n\n' "$TAG"
    printf '| leg | result | detail |\n'
    printf '|---|---|---|\n'
    for leg in fresh upgrade capture chaos; do
      leg_row_md "$leg" "${LEG_STATUS[$leg]}" "${LEG_DETAIL[$leg]}"
    done
    printf '\n## Measurements\n\nnone\n\n'
    printf 'Needs manual evidence: the LLL ear — %s facts are manual\n' "$manual_facts"
  } > "$out_dir/gate-report.md"

  local first_failure fresh_json upgrade_json capture_json chaos_json
  first_failure="$(first_failure_across_legs)"
  fresh_json="$(leg_json "${LEG_STATUS[fresh]}" "${LEG_DETAIL[fresh]}")"
  upgrade_json="$(leg_json "${LEG_STATUS[upgrade]}" "${LEG_DETAIL[upgrade]}")"
  capture_json="$(leg_json "${LEG_STATUS[capture]}" "${LEG_DETAIL[capture]}")"
  chaos_json="$(leg_json "${LEG_STATUS[chaos]}" "${LEG_DETAIL[chaos]}")"

  jq -n \
    --arg tag "$TAG" \
    --argjson fresh "$fresh_json" \
    --argjson upgrade "$upgrade_json" \
    --argjson capture "$capture_json" \
    --argjson chaos "$chaos_json" \
    --arg first_failure "$first_failure" \
    --argjson manual_facts "$manual_facts" \
    '{
      tag: $tag,
      legs: { fresh: $fresh, upgrade: $upgrade, capture: $capture, chaos: $chaos },
      first_failure: (if $first_failure == "" then null else $first_failure end),
      manual_facts: $manual_facts
    }' > "$out_dir/gate-report.json"
}

write_report "$REPORT_DIR"

for leg in fresh upgrade capture chaos; do
  if [ "${LEG_STATUS[$leg]}" = "failed" ]; then
    exit 1
  fi
done
exit 0
