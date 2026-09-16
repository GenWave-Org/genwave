#!/usr/bin/env bash
# stack_gate.sh — full-stack release gate for a tagged GenWave build (SPEC F178, STORY-444).
#
# Proves a published `home-<tag>` image set actually boots and serves before that tag reaches a
# box: each requested leg runs a real docker compose stack pinned to the tag, inside its OWN
# scratch copy of the checkout — never the caller's working tree, never the caller's `.env`.
#
#   --fresh          a clean install from nothing (setup.sh --yes, bare launch.sh — the
#                     wizard's own .env picks the pinned piper-only topology — health/on-air
#                     waits)
#   --upgrade        an existing station upgraded onto the tag (T491+)
#   --capture        captures stream audio during the leg for a loudness/crossfade check
#                     (T491+; requires --fresh)
#   --chaos          fault injection during the leg (T491+; requires --capture)
#   --from <vX.Y.Z>  the tag the upgrade leg starts from (validated, stored; unused until T491)
#   --report <dir>   write gate-report.md + gate-report.json there (default: .)
#
# This file started as PLAN T486's skeleton: arg parsing, the prerequisite probe, isolation, the
# scratch + compose.gate.yaml generator, the PROJECTS/trap teardown, and the report writer. PLAN
# T487 fills in the fresh leg's own orchestration (media synth, setup.sh --yes, launch.sh,
# health, on-air — see run_fresh_leg). `--upgrade`/`--capture`/`--chaos` are still report-only
# "skipped" rows until T491+.
#
# Usage: tools/gate/stack_gate.sh --tag <vX.Y.Z> [--fresh] [--upgrade] [--capture] [--chaos]
#                                  [--from <vX.Y.Z>] [--report <dir>]
# Exit codes: 0 = every requested leg passed (or none were requested); 1 = a leg failed (see the
#             report); 2 = usage error or a missing prerequisite (docker, ffmpeg, jq, curl; gh
#             only with --upgrade) — reached before any leg runs, so no report is written.
#
# Isolation (F178.1): never reads the caller's .env; strips GW_*/COMPOSE_*/the six .env secret
# names from its own exported environment before any docker call, so a developer's shell leaking
# a real ADMIN_PASSWORD or a stray GW_* override can never reach the stack under test.
#
# Knobs (env, all optional — the fresh leg's own; --upgrade/--capture/--chaos add more at
# T491+): GATE_API_BASE (default http://localhost:8080) the api base URL /health is probed
# against; GATE_STREAM_URL (default http://localhost:8000/stream) threaded into the real
# setup.sh's own on-air poll target; GATE_HEALTH_SECS (default 180) and GATE_ONAIR_SECS (default
# 300) the wall-clock budgets for the health and on-air waits below; GATE_POLL_SECS (default 5,
# must be a positive integer) how often each wait re-probes.

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

# The fresh leg's own compose file set (base + piper-only + the tag overlay) — hoisted once so
# cleanup() and every gate-side compose call (up's implicit COMPOSE_FILE, exec, logs, down) share
# the exact same list rather than repeating the literal -f flags at each call site (T486 review).
COMPOSE_FILES=(-f compose.yaml -f compose.piper-only.yaml -f compose.gate.yaml)
# Same file set, colon-joined for the COMPOSE_FILE env var — the form setup.sh's own internal
# launch and this leg's bare `./launch.sh` call both read for a "bare compose" invocation with no
# explicit -f (see run_fresh_leg). Kept as a second literal, not derived from COMPOSE_FILES,
# because the array carries the `-f` separators the joined form must not have; update both
# together if the file set ever changes.
COMPOSE_FILE_LIST="compose.yaml:compose.piper-only.yaml:compose.gate.yaml"

# Measurements the fresh leg records on its way to a pass (or as far as it got) — empty until
# run_fresh_leg sets them, read by write_report below.
FRESH_HEALTH_SECS=""
FRESH_ONAIR_SECS=""

# shellcheck disable=SC2317 # false positive: only called indirectly via `trap cleanup EXIT`,
# which shellcheck's reachability analysis doesn't follow (documented SC2317 caveat).
cleanup() {
  local entry scratch project
  for entry in "${PROJECTS[@]}"; do
    scratch="${entry%%|*}"
    project="${entry#*|}"
    # A project can be registered before setup.sh ever runs (see run_fresh_leg) so that a REAL
    # setup.sh's own internal launch lands under this same project name — but that means a leg
    # that fails before setup.sh ever writes $scratch/.env never brought anything up either.
    # Skip those quietly rather than run a `down -v` against a project nothing exists under
    # (T486 review: teardown noise).
    [ -f "$scratch/.env" ] || continue
    ( cd "$scratch" && docker compose -p "$project" "${COMPOSE_FILES[@]}" down -v ) || true
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
if [ -n "${GATE_POLL_SECS:-}" ] && ! [[ "$GATE_POLL_SECS" =~ ^[1-9][0-9]*$ ]]; then
  usage_error "GATE_POLL_SECS must be a positive integer: $GATE_POLL_SECS"
fi

# ---------------------------------------------------------------------------------------------
# Prerequisite probe — binaries on THIS host; docker compose's v2-ness is checked per leg, inside
# that leg's own scratch (see run_fresh_leg), since verifying it means an actual docker call and
# no docker call may run against the caller's checkout.
# ---------------------------------------------------------------------------------------------
require_prereq docker
require_prereq ffmpeg
require_prereq jq
require_prereq curl
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

# make_gate_media <dir> — SPEC F178.3's run-time half (T489 later moves this into its own
# tools/gate/make_media.sh): two 45-second tracks at -12 and -30 integrated LUFS, tone mixed with
# shaped noise (so loudnorm has real dynamics to normalize, not a bare sine) rather than pure
# tone, tagged genre=music so setup.sh's own Q2 .flac/.mp3 count sees a populated library.
make_gate_media() {
  local dir="$1" i lufs
  mkdir -p "$dir"
  for i in 1 2; do
    case "$i" in 1) lufs=-12 ;; *) lufs=-30 ;; esac
    ffmpeg -nostats -hide_banner -loglevel error -y -f lavfi -i \
      "sine=frequency=440:duration=45[tone];anoisesrc=color=pink:duration=45[noise];[tone][noise]amix=inputs=2:duration=shortest,loudnorm=I=${lufs}:TP=-1:LRA=7" \
      -metadata genre=music -ar 44100 -ac 2 "$dir/gate-track-${i}.flac"
  done
}

# gate_setup_answers <media_dir> — the interview's stdin, in question order (setup.sh F132.2).
# Q1 (images mode) is asked ONLY when a .NET 10 SDK is on THIS PATH right now — the same one-
# liner setup.sh:310's check_dotnet10_sdk uses, mirrored here (not shared: setup.sh is a file
# this task may not edit) so the gate answers the exact menu setup.sh actually shows. Then Q2 the
# dir make_gate_media just populated, Q3 [2] piper-only, Q4 [y] admin UI — the wizard defaults
# named in SPEC F178.2.
gate_setup_answers() {
  local media_dir="$1"
  if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
    printf '1\n'
  fi
  printf '%s\n2\ny\n' "$media_dir"
}

# stage_pinned_overlay_for_launch <scratch> — launch.sh names its OWN pins file
# (compose.pinned.yaml), never compose.gate.yaml, so the tag overlay's content is duplicated
# onto it inside the scratch — the scratch is disposable, launch.sh is not the file to teach a
# second overlay name to for one gate run. `rm -f` first: every top-level scratch entry rsync
# just produced is a SYMLINK back into the real checkout (the hermetic test harness's own repo
# copy is itself a symlink farm, and rsync -a preserves symlinks as symlinks) until this line
# replaces it — writing through that symlink unlinked (`cp` onto an existing symlink follows it)
# would edit the caller's actual working tree, exactly what this whole script exists to never do.
stage_pinned_overlay_for_launch() {
  local scratch="$1"
  rm -f "$scratch/compose.pinned.yaml"
  cp "$scratch/compose.gate.yaml" "$scratch/compose.pinned.yaml"
}

# attach_fresh_compose_logs <scratch> <project> — F178.4: any miss past the point the stack was
# asked to come up gets the compose logs into the report dir, so a failed leg is diagnosable
# without re-running it. REPORT_DIR may not exist yet (write_report, below, is normally what
# creates it) — mkdir -p here too, since a miss can land long before that.
attach_fresh_compose_logs() {
  local scratch="$1" project="$2"
  mkdir -p "$REPORT_DIR"
  ( cd "$scratch" && docker compose -p "$project" "${COMPOSE_FILES[@]}" logs --no-color ) \
    > "$REPORT_DIR/compose-fresh.log" 2>&1 || true
}

# fresh_onair_frame <scratch> <project> — the on-air read (tools/onair_gate.sh:34-48): telnet the
# engine's control socket over /dev/tcp for output.icecast.metadata, then select frame "--- 1 ---"
# (the CURRENT on-air track) when frame markers are present, or take the whole reply when they
# are not (the gate's own docker stub prints one bare line, no frame markers at all).
fresh_onair_frame() {
  local scratch="$1" project="$2" raw
  raw="$(cd "$scratch" && docker compose -p "$project" "${COMPOSE_FILES[@]}" \
    exec -T engine bash -s <<'ONAIR_CMD' | tr -d '\r'
exec 3<>/dev/tcp/127.0.0.1/1234 || { echo "CONNECT_FAILED"; exit 1; }
printf '%s\n' "output.icecast.metadata" >&3
while IFS= read -r line <&3; do case "$line" in END*) break ;; *) printf '%s\n' "$line" ;; esac; done
printf 'exit\n' >&3
ONAIR_CMD
)"
  if grep -qE '^--- [0-9]+ ---$' <<<"$raw"; then
    awk '/^--- [0-9]+ ---$/ { cur = ($2 == "1"); next } cur { print }' <<<"$raw"
  else
    printf '%s' "$raw"
  fi
}

run_fresh_leg() {
  local scratch project version_output major
  scratch="$(mktemp -d)"
  SCRATCH_DIRS+=("$scratch")

  rsync -a --exclude .env --exclude .git --exclude node_modules --exclude bin --exclude obj \
    "$root/" "$scratch/"

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
  stage_pinned_overlay_for_launch "$scratch"

  # SPEC F178.3 (run-time half) — the wizard's Q2 needs files on disk before it asks for them.
  local media_dir="$scratch/media"
  make_gate_media "$media_dir"

  # Registered BEFORE setup.sh ever runs, not after: setup.sh's own wizard launches the stack
  # itself (invoke_launch -> a bare ./launch.sh) as part of --yes, and that child launch.sh reads
  # COMPOSE_PROJECT_NAME from the environment it inherits from setup.sh. Without this project name
  # in setup.sh's own env, compose falls back to compose.yaml's top-level `name: genwave` — on a
  # real box that's the live dev station's project (setup.sh's internal launch would recreate ITS
  # containers), and this leg's own compose calls further down would then collide on the SAME
  # published ports (8080/8000/5432) as a second, differently-named stack (T487 review F1).
  # cleanup()'s down -v is guarded on $scratch/.env existing, so registering this early is safe
  # even when setup.sh fails before ever writing one (T486 teardown-noise finding).
  project="$(gate_project_name fresh)"
  PROJECTS+=("$scratch|$project")

  # SPEC F178.2 — setup.sh --yes, answers on stdin, .env landing inside the scratch.
  # COMPOSE_PROJECT_NAME/COMPOSE_FILE isolate setup.sh's OWN internal launch (above) under this
  # leg's project rather than the top-level `name: genwave`; GW_STREAM_URL matters only to the
  # REAL setup.sh's own on-air poll (the stub ignores it) — harmless either way. GW_ENV_FILE is
  # NOT itself read by setup.sh's wizard; the media path is answered on stdin
  # (gate_setup_answers), so no MEDIA_DIR env is passed here. GW_ONAIR_TIMEOUT_SECONDS is
  # deliberately left unset — setup.sh's own default (900s) covers a first-run pull of five
  # images; this leg measures its OWN "within GATE_ONAIR_SECS of up" wait below, against the
  # already-up stack (SPEC F178.4), so passing GATE_ONAIR_SECS through here would start that clock
  # before the pull even begins.
  if ! (cd "$scratch" && gate_setup_answers "$media_dir" | \
        GW_ENV_FILE="$scratch/.env" \
        COMPOSE_PROJECT_NAME="$project" COMPOSE_FILE="$COMPOSE_FILE_LIST" \
        GW_STREAM_URL="${GATE_STREAM_URL:-http://localhost:8000/stream}" \
        ./setup.sh --yes); then
    LEG_STATUS[fresh]="failed"; LEG_DETAIL[fresh]="setup"
    attach_fresh_compose_logs "$scratch" "$project"
    return
  fi

  # Bare ./launch.sh — no --pinned, which also stacks compose.demo.yaml (Caddy, PUBLIC_HOST),
  # wrong for a fresh-install gate; the wizard's own .env (GW_PRESET=home-piper-only) already
  # picks the pinned piper-only topology, and stage_pinned_overlay_for_launch (above) already put
  # the tag's images in the file launch.sh actually reads. Same COMPOSE_PROJECT_NAME as the
  # setup.sh call above, so a real setup.sh's own internal launch and this call converge on the
  # SAME stack rather than colliding on ports — this second call is then an idempotent
  # re-converge (stub world: setup.sh never touched docker at all, so this is the only `up`).
  # The isolation strip earlier in this script dropped any caller-supplied COMPOSE_*; these are
  # the gate's own, set fresh here.
  #
  # T488 real-box finding: SKIP_PREFLIGHT=1 forced on THIS call only (mirrors setup.sh's own
  # invoke_launch/1640-1650 comment) — setup.sh --yes just above already ran launch.sh's machine
  # preflight over this exact scratch's env moments earlier via its own internal invoke_launch,
  # so a second preflight here is redundant AND actively wrong: it would see the leg's own api
  # container (just brought up by that first launch) already bound to :8080 and report the port
  # "already in use by an unidentified process" — the leg failing against itself. Never exported
  # process-wide; scoped to this one subprocess only.
  if ! (cd "$scratch" && \
        SKIP_PREFLIGHT=1 \
        COMPOSE_FILE="$COMPOSE_FILE_LIST" \
        COMPOSE_PROJECT_NAME="$project" \
        ./launch.sh); then
    LEG_STATUS[fresh]="failed"; LEG_DETAIL[fresh]="launch"
    attach_fresh_compose_logs "$scratch" "$project"
    return
  fi

  # SPEC F178.4 — /health must go green within GATE_HEALTH_SECS (default 180). Strip a trailing
  # slash: GATE_API_BASE (the fake station's own base URL, in tests) carries one, and appending
  # "/health" straight onto it would double the slash and 404 against the real path. Both budgets
  # below are measured off bash's own $SECONDS (wall clock since the shell started) rather than a
  # sleep counter, so curl's own probe time (health) and the `docker compose exec` round-trip
  # (on-air) both count against the budget instead of running for free between sleeps; `--max-time
  # 5` keeps a single hung probe from eating the whole budget by itself.
  local api_base="${GATE_API_BASE:-http://localhost:8080}"
  api_base="${api_base%/}"
  local health_budget="${GATE_HEALTH_SECS:-180}" poll_secs="${GATE_POLL_SECS:-5}"
  local health_start=$SECONDS health_ok=0 elapsed=0
  while :; do
    if curl -fsS --max-time 5 -o /dev/null "$api_base/health"; then health_ok=1; break; fi
    elapsed=$((SECONDS - health_start))
    [ "$elapsed" -ge "$health_budget" ] && break
    sleep "$poll_secs"
  done
  elapsed=$((SECONDS - health_start))
  if [ "$health_ok" != "1" ]; then
    LEG_STATUS[fresh]="failed"; LEG_DETAIL[fresh]="health"
    attach_fresh_compose_logs "$scratch" "$project"
    return
  fi
  FRESH_HEALTH_SECS="$elapsed"

  # SPEC F178.4 — output.icecast.metadata frame 1 must carry a track_id within GATE_ONAIR_SECS
  # (default 300) of up.
  local onair_budget="${GATE_ONAIR_SECS:-300}"
  local onair_start=$SECONDS onair_ok=0 frame
  while :; do
    frame="$(fresh_onair_frame "$scratch" "$project")"
    if grep -q 'track_id="' <<<"$frame"; then onair_ok=1; break; fi
    elapsed=$((SECONDS - onair_start))
    [ "$elapsed" -ge "$onair_budget" ] && break
    sleep "$poll_secs"
  done
  elapsed=$((SECONDS - onair_start))
  if [ "$onair_ok" != "1" ]; then
    LEG_STATUS[fresh]="failed"; LEG_DETAIL[fresh]="on-air"
    attach_fresh_compose_logs "$scratch" "$project"
    return
  fi
  FRESH_ONAIR_SECS="$elapsed"

  LEG_STATUS[fresh]="passed"
  LEG_DETAIL[fresh]=""
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
  local status="$1" detail="$2" measurements="${3:-}" first_failure="" skipped_by=""
  [ -n "$measurements" ] || measurements="{}"
  case "$status" in
    failed)  first_failure="$detail" ;;
    skipped) skipped_by="$detail" ;;
  esac
  jq -n --arg status "$status" --arg first_failure "$first_failure" --arg skipped_by "$skipped_by" \
    --argjson measurements "$measurements" '
    {
      status: $status,
      first_failure: (if $first_failure == "" then null else $first_failure end),
      skipped_by: (if $skipped_by == "" then null else $skipped_by end),
      measurements: $measurements
    }'
}

# fresh_measurements_json — {} until the fresh leg has measured at least one of health_secs/
# onair_secs (FRESH_HEALTH_SECS/FRESH_ONAIR_SECS, set by run_fresh_leg as it clears each wait —
# a leg that fails at on-air still reports the health_secs it already measured).
fresh_measurements_json() {
  jq -n --arg h "$FRESH_HEALTH_SECS" --arg o "$FRESH_ONAIR_SECS" '
    (if $h == "" then {} else {health_secs: ($h | tonumber)} end) +
    (if $o == "" then {} else {onair_secs: ($o | tonumber)} end)'
}

# fresh_measurements_md — the same facts as fresh_measurements_json, rendered as report lines;
# "none" when neither was ever measured.
fresh_measurements_md() {
  if [ -z "$FRESH_HEALTH_SECS" ] && [ -z "$FRESH_ONAIR_SECS" ]; then
    printf 'none\n'
    return
  fi
  [ -n "$FRESH_HEALTH_SECS" ] && printf 'fresh health_secs: %s\n' "$FRESH_HEALTH_SECS"
  [ -n "$FRESH_ONAIR_SECS" ] && printf 'fresh onair_secs: %s\n' "$FRESH_ONAIR_SECS"
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
    printf '\n## Measurements\n\n%s\n\n' "$(fresh_measurements_md)"
    printf 'Needs manual evidence: the LLL ear — %s facts are manual\n' "$manual_facts"
  } > "$out_dir/gate-report.md"

  local first_failure fresh_json upgrade_json capture_json chaos_json
  first_failure="$(first_failure_across_legs)"
  fresh_json="$(leg_json "${LEG_STATUS[fresh]}" "${LEG_DETAIL[fresh]}" "$(fresh_measurements_json)")"
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
