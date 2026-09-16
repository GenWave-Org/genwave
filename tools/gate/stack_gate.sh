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
#   --upgrade        an existing station on the previous release, upgraded onto the tag: a
#                     `git worktree` of the previous release stands up its own pinned stack,
#                     waits on-air, then `compose stop api`, swaps in the CURRENT tag's db/ +
#                     migrate.sh, runs the migration, checks the role boundary (station_svc/
#                     library_svc each locked out of the other's schema, SPEC F178.7), brings api
#                     back on the CURRENT tag, and waits health/on-air again (SPEC F178.6/F178.7;
#                     see run_upgrade_leg)
#   --capture        records CAPTURE_SECS of the fresh leg's own live stream and measures it: no
#                     silent gaps, loudness within TOL_LU of the station's own configured target,
#                     speech aired through the booth log (requires --fresh)
#   --chaos          fault injection around the capture leg's own recording (SPEC F178.8; requires
#                     --capture): api-down (T496) — `compose stop api`, sleep GATE_OUTAGE_SECS,
#                     `compose start api`, then poll for a non-safe track_id within
#                     GATE_RECOVERY_SECS; zero silence events during the outage, else the gate
#                     leg fails "api-down silence" (not the capture leg's own "silence" — see
#                     run_capture_leg's post-measure attribution). Engine-reconnect (T497) —
#                     AFTER the capture leg finishes, only when api-down passed — `compose restart
#                     engine`, wait for the container healthy, then poll for a non-safe track_id
#                     within GATE_RECONNECT_SECS, else "engine-reconnect on-air"; then a FRESH
#                     60-second capture measured for silence only, else "engine-reconnect silence"
#                     (see run_engine_reconnect_scenario).
#   --from <vX.Y.Z>  the previous release the upgrade leg starts from — overrides the newest
#                     other `v*` GitHub release otherwise resolved via `gh release list`
#   --report <dir>   write gate-report.md + gate-report.json there (default: .)
#
# This file started as PLAN T486's skeleton: arg parsing, the prerequisite probe, isolation, the
# scratch + compose.gate.yaml generator, the PROJECTS/trap teardown, and the report writer. PLAN
# T487 filled in the fresh leg's own orchestration (media synth, setup.sh --yes, launch.sh,
# health, on-air — see run_fresh_leg). PLAN T491 filled in the capture leg (run_capture_leg):
# admin login, the station's own loudness target, the recording, measure_audio.sh, and the
# booth-log speech check. PLAN T493 filled in the upgrade leg (run_upgrade_leg): previous-release
# resolution, the worktree, and the stop/migrate/restart sequence. PLAN T494 added the role-
# boundary check (SPEC F178.7) between the migration and the api restart. PLAN T496 added the
# api-down chaos scenario (SPEC F178.8(a)) inside run_capture_leg's own recording (see
# run_api_down_scenario). PLAN T497 added the engine-reconnect chaos scenario (SPEC F178.8(b)),
# run right after run_capture_leg finishes rather than inside it (see run_engine_reconnect_scenario).
#
# Usage: tools/gate/stack_gate.sh --tag <vX.Y.Z> [--fresh] [--upgrade] [--capture] [--chaos]
#                                  [--from <vX.Y.Z>] [--report <dir>]
# Exit codes: 0 = every requested leg passed (or none were requested); 1 = a leg failed (see the
#             report); 2 = usage error or a missing prerequisite (docker, ffmpeg, jq, curl; gh
#             only with --upgrade AND no --from, since --from needs no release lookup at all) —
#             reached before any leg runs, so no report is written.
#
# Isolation (F178.1): never reads the caller's .env; strips GW_*/COMPOSE_*/the six .env secret
# names from its own exported environment before any docker call, so a developer's shell leaking
# a real ADMIN_PASSWORD or a stray GW_* override can never reach the stack under test. The
# capture leg's own admin login (below) reads the scratch's ADMIN_PASSWORD straight off the
# scratch's `.env` on disk for the same reason — never `source`d, never exported.
#
# Knobs (env, all optional — the fresh, upgrade and chaos legs share the same ones):
# GATE_API_BASE (default http://localhost:8080) the api base URL /health is probed against, and
# — with --capture — /api/auth/login + /api/settings too; GATE_STREAM_URL (default
# http://localhost:8000/stream) threaded into the real setup.sh's own on-air poll target and, with
# --capture, the URL ffmpeg records from; GATE_HEALTH_SECS (default 180) and GATE_ONAIR_SECS
# (default 300) the wall-clock budgets for the health and on-air waits below (the upgrade leg
# runs both waits twice — before and after the cutover — against the same budgets); GATE_POLL_SECS
# (default 5, must be a positive integer) how often each wait re-probes; CAPTURE_SECS (default
# 360, must be a positive integer) how long --capture records; GATE_OUTAGE_SECS (default 90, must
# be a positive integer) how long --chaos's api-down scenario holds `api` stopped; GATE_RECOVERY_SECS
# (default 120, must be a positive integer) the wall-clock budget the same scenario allows for a
# non-safe track_id to reappear after `compose start api`; GATE_RECONNECT_SECS (default 60, must
# be a positive integer) the wall-clock budget --chaos's engine-reconnect scenario allows for a
# non-safe track_id to reappear after `compose restart engine` (health wait included); TOL_LU,
# SILENCE_FLOOR, SILENCE_SECS — measure_audio.sh's own tolerance knobs, passed through untouched
# (see tools/gate/measure_audio.sh).

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
WORKTREES=()     # every `git worktree add` target this run made (the upgrade leg's previous
                 # release checkout) — `git worktree remove --force` on EXIT, same as the other
                 # two lists; removed before SCRATCH_DIRS is rm -rf'd (a worktree the parent repo
                 # doesn't know about anymore is exactly the debris `git worktree remove` exists
                 # to avoid on a real box's checkout).

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

# Upgrade leg measurements (T493) — UPGRADE_PREVIOUS as soon as resolve_previous_tag succeeds;
# UPGRADE_HEALTH_SECS/UPGRADE_ONAIR_SECS as soon as EITHER wait clears (the leg waits twice, once
# against the previous release, once again after the cutover — the later measurement overwrites
# the earlier one, since a passing leg's own numbers are the ones that describe the tag actually
# being gated). UPGRADE_MIGRATE_TAIL is the current migrate.sh's own tail output, set only when
# it exits non-zero, for the report's diagnostic line — never part of the JSON measurements.
UPGRADE_PREVIOUS=""
UPGRADE_HEALTH_SECS=""
UPGRADE_ONAIR_SECS=""
UPGRADE_MIGRATE_TAIL=""

# UPGRADE_BOUNDARY (T494, SPEC F178.7) — "ok" once BOTH cross-schema probes below come back
# "permission denied", "failed" the moment either one doesn't; empty when the role-boundary check
# is never reached at all (a leg that failed earlier, e.g. at migrate). Rendered into the report
# as its own "role boundary | <value>" line/JSON field, twin-style with the other UPGRADE_* facts.
# UPGRADE_BOUNDARY_TAIL is the first non-matching probe's own output, set only on a failed check,
# for the report's diagnostic line — never part of the JSON measurements (mirrors
# UPGRADE_MIGRATE_TAIL above).
UPGRADE_BOUNDARY=""
UPGRADE_BOUNDARY_TAIL=""

# The fresh leg's own scratch + compose project name, set by run_fresh_leg as soon as each exists
# — run_capture_leg (T491) reuses this SAME stack rather than standing up a second one, since
# SPEC F178.5 measures the fresh leg's own live stream.
FRESH_SCRATCH=""
FRESH_PROJECT=""

# Capture leg measurements (T491) — empty until run_capture_leg sets them, one field at a time,
# as each step completes; a step never reached stays empty (rendered as `null` in the JSON
# report, never a string) so a failing leg still reports every number it actually measured
# (SPEC F178.5: "every number into the report").
CAPTURE_SECS_VALUE=""
CAPTURE_TARGET_LUFS=""
CAPTURE_SILENCE_EVENTS=""
CAPTURE_INTEGRATED_LUFS=""
CAPTURE_BOOTH_LOG=""
CAPTURE_FFMPEG_TAIL=""

# Chaos leg measurements (T496/T497, SPEC F178.8) — the api-down scenario runs INSIDE
# run_capture_leg itself (see run_api_down_scenario); the engine-reconnect scenario runs right
# AFTER run_capture_leg returns, only when api-down passed (see run_engine_reconnect_scenario).
# Neither is a leg of its own, so these are set by the scenario functions rather than by a
# run_chaos_leg. CHAOS_API_DOWN_RAN flips to 1 the moment `compose stop api` is attempted; it, not
# DO_CHAOS, is what the post-capture code below reads to tell "the scenario really ran" apart from
# "capture never got that far" (e.g. login/target failed first). ENGINE_RESTART_RAN is the same
# flag for `compose restart engine`. CHAOS_OUTAGE_SECS is the GATE_OUTAGE_SECS value once the
# outage actually happened; CHAOS_RECOVERY_SECS is the elapsed seconds from `start api` to the
# first non-safe frame, empty if recovery never happened within GATE_RECOVERY_SECS — a single
# source of truth per measurement, no separate "_OK" flag (an empty seconds string already means
# "never reached"). ENGINE_ONAIR_SECS is the same shape for `restart engine`: elapsed seconds
# (health wait included) to the first non-safe frame, empty if it never happened within
# GATE_RECONNECT_SECS. ENGINE_SILENCE_EVENTS is the post-restart capture's own silence_events=
# reading (SPEC F178.5(a) only — loudness is deliberately not judged here), empty if that capture
# was never reached. ENGINE_CAPTURE_FFMPEG_TAIL is that capture's own ffmpeg tail, set only when
# the recording itself fails, for the report's diagnostic line — never part of the JSON
# measurements (mirrors CAPTURE_FFMPEG_TAIL).
CHAOS_API_DOWN_RAN=0
CHAOS_OUTAGE_SECS=""
CHAOS_RECOVERY_SECS=""
ENGINE_RESTART_RAN=0
ENGINE_ONAIR_SECS=""
ENGINE_SILENCE_EVENTS=""
ENGINE_CAPTURE_FFMPEG_TAIL=""

# A background ffmpeg still recording on an abort (Ctrl-C, set -e) is not killed here on purpose:
# it is bounded by `-t CAPTURE_SECS` and its source dies with the stack's `down -v` below.
# shellcheck disable=SC2317 # false positive: only called indirectly via `trap cleanup EXIT`,
# which shellcheck's reachability analysis doesn't follow (documented SC2317 caveat).
cleanup() {
  local entry scratch project worktree
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
  # Before the scratch rm -rf below, which would otherwise yank the worktree's directory out from
  # under git and leave a stale entry in the ORIGINAL checkout's `.git/worktrees/` on a real box.
  # Run from $root (the original checkout — see resolve_previous_tag/run_upgrade_leg), the same
  # place `git worktree add` ran from, since `git worktree remove` needs a real repo to find the
  # worktree's admin data in. `--force` — the leg mutates db/ and the compose overlay inside the
  # worktree (SPEC F178.6's cutover), so it is never git-clean by the time this runs.
  for worktree in "${WORKTREES[@]}"; do
    ( cd "$root" && git worktree remove --force "$worktree" ) || true
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
if [ -n "${CAPTURE_SECS:-}" ] && ! [[ "$CAPTURE_SECS" =~ ^[1-9][0-9]*$ ]]; then
  usage_error "CAPTURE_SECS must be a positive integer: $CAPTURE_SECS"
fi
if [ -n "${GATE_OUTAGE_SECS:-}" ] && ! [[ "$GATE_OUTAGE_SECS" =~ ^[1-9][0-9]*$ ]]; then
  usage_error "GATE_OUTAGE_SECS must be a positive integer: $GATE_OUTAGE_SECS"
fi
if [ -n "${GATE_RECOVERY_SECS:-}" ] && ! [[ "$GATE_RECOVERY_SECS" =~ ^[1-9][0-9]*$ ]]; then
  usage_error "GATE_RECOVERY_SECS must be a positive integer: $GATE_RECOVERY_SECS"
fi
if [ -n "${GATE_RECONNECT_SECS:-}" ] && ! [[ "$GATE_RECONNECT_SECS" =~ ^[1-9][0-9]*$ ]]; then
  usage_error "GATE_RECONNECT_SECS must be a positive integer: $GATE_RECONNECT_SECS"
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
# gh only resolves the previous release when the caller didn't name one outright — --from skips
# the lookup entirely (see resolve_previous_tag), so a box with no `gh` at all can still run an
# upgrade leg pinned with --from.
[ "$DO_UPGRADE" = 1 ] && [ -z "$FROM_TAG" ] && require_prereq gh

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

# gate_api_base — GATE_API_BASE (default http://localhost:8080) with any trailing slash trimmed;
# every caller appends its own leading slash, so a caller-supplied trailing slash (the test
# harness's fake station URL carries one) never doubles up. Shared by the fresh leg's /health
# probe and the capture leg's /api/auth/login + /api/settings calls.
gate_api_base() {
  local base="${GATE_API_BASE:-http://localhost:8080}"
  printf '%s' "${base%/}"
}

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

# attach_fresh_compose_logs <scratch> <project> [logname] — F178.4: any miss past the point the
# stack was asked to come up gets the compose logs into the report dir, so a failed leg is
# diagnosable without re-running it. <logname> defaults to compose-fresh.log; the upgrade leg
# (T493) passes compose-upgrade.log so the two legs' dumps never overwrite each other. REPORT_DIR
# may not exist yet (write_report, below, is normally what creates it) — mkdir -p here too, since
# a miss can land long before that.
attach_fresh_compose_logs() {
  local scratch="$1" project="$2" logname="${3:-compose-fresh.log}"
  mkdir -p "$REPORT_DIR"
  ( cd "$scratch" && docker compose -p "$project" "${COMPOSE_FILES[@]}" logs --no-color ) \
    > "$REPORT_DIR/$logname" 2>&1 || true
}

# check_compose_v2 <scratch> — SPEC F178.1's "docker compose v2" means the plugin, as opposed to
# the legacy `docker-compose` v1 binary: any plugin major >= 2 qualifies, so this parses the major
# out of `docker compose version` rather than pinning the literal string "v2" (a real box's plugin
# is newer than the fixture's stubbed "v2.29.0" and must still pass). Prints nothing on success;
# on failure prints the message a caller should use as its own LEG_DETAIL and returns non-zero.
# Runs from inside <scratch> — a leg's first docker call, never from the caller's checkout.
check_compose_v2() {
  local scratch="$1" version_output major
  if ! version_output="$(cd "$scratch" && docker compose version)"; then
    printf 'docker compose version failed'
    return 1
  fi
  if [[ "$version_output" =~ [vV]?([0-9]+)\.[0-9]+\.[0-9]+ ]]; then
    major="${BASH_REMATCH[1]}"
  else
    printf 'could not parse docker compose version: %s' "$version_output"
    return 1
  fi
  if [ "$major" -lt 2 ]; then
    printf 'docker compose plugin v2+ required, got: %s' "$version_output"
    return 1
  fi
  return 0
}

# wait_for_health — SPEC F178.4: /health must go green within GATE_HEALTH_SECS (default 180).
# gate_api_base strips a trailing slash: GATE_API_BASE (the fake station's own base URL, in
# tests) carries one, and appending "/health" straight onto it would double the slash and 404
# against the real path. The budget is measured off bash's own $SECONDS (wall clock since the
# shell started) rather than a sleep counter, so curl's own probe time counts against the budget
# too; `--max-time 5` keeps a single hung probe from eating the whole budget by itself. Prints
# "<0|1> <elapsed>" on its own stdout line — a caller reads both with `read -r ok elapsed < <(…)`.
wait_for_health() {
  local api_base; api_base="$(gate_api_base)"
  local budget="${GATE_HEALTH_SECS:-180}" poll_secs="${GATE_POLL_SECS:-5}"
  local start=$SECONDS ok=0 elapsed=0
  while :; do
    if curl -fsS --max-time 5 -o /dev/null "$api_base/health"; then ok=1; break; fi
    elapsed=$((SECONDS - start))
    [ "$elapsed" -ge "$budget" ] && break
    sleep "$poll_secs"
  done
  elapsed=$((SECONDS - start))
  printf '%s %s\n' "$ok" "$elapsed"
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

# wait_for_onair <scratch> <project> [budget_secs] — SPEC F178.4: output.icecast.metadata frame 1
# must carry a track_id within GATE_ONAIR_SECS (default 300) of up. Same budget shape and output
# contract as wait_for_health. [budget_secs] overrides GATE_ONAIR_SECS for a caller polling
# against a DIFFERENT budget against the exact same frame — run_api_down_scenario (T496) reuses
# this poll loop verbatim against GATE_RECOVERY_SECS rather than duplicating it.
wait_for_onair() {
  local scratch="$1" project="$2"
  local budget="${3:-${GATE_ONAIR_SECS:-300}}" poll_secs="${GATE_POLL_SECS:-5}"
  local start=$SECONDS ok=0 elapsed=0 frame
  while :; do
    frame="$(fresh_onair_frame "$scratch" "$project")"
    if grep -q 'track_id="' <<<"$frame"; then ok=1; break; fi
    elapsed=$((SECONDS - start))
    [ "$elapsed" -ge "$budget" ] && break
    sleep "$poll_secs"
  done
  elapsed=$((SECONDS - start))
  printf '%s %s\n' "$ok" "$elapsed"
}

run_fresh_leg() {
  local scratch project
  scratch="$(mktemp -d)"
  SCRATCH_DIRS+=("$scratch")
  FRESH_SCRATCH="$scratch"

  rsync -a --exclude .env --exclude .git --exclude node_modules --exclude bin --exclude obj \
    "$root/" "$scratch/"

  # The leg's first docker call, from inside the scratch — never from the caller's checkout.
  local compose_detail
  if ! compose_detail="$(check_compose_v2 "$scratch")"; then
    LEG_STATUS[fresh]="failed"; LEG_DETAIL[fresh]="$compose_detail"
    return
  fi

  write_compose_gate_overlay "$scratch" "$TAG"
  stage_pinned_overlay_for_launch "$scratch"

  # SPEC F178.3 (run-time half) — the wizard's Q2 needs files on disk before it asks for them;
  # tools/gate/make_media.sh (T489) renders the tones and copies the committed CC0 clips in.
  local media_dir="$scratch/media"
  "$root/tools/gate/make_media.sh" "$media_dir"

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
  FRESH_PROJECT="$project"
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

  # SPEC F178.4 — health then on-air, both within their own wall-clock budgets (see
  # wait_for_health/wait_for_onair).
  local health_ok health_elapsed
  read -r health_ok health_elapsed < <(wait_for_health)
  if [ "$health_ok" != "1" ]; then
    LEG_STATUS[fresh]="failed"; LEG_DETAIL[fresh]="health"
    attach_fresh_compose_logs "$scratch" "$project"
    return
  fi
  FRESH_HEALTH_SECS="$health_elapsed"

  local onair_ok onair_elapsed
  read -r onair_ok onair_elapsed < <(wait_for_onair "$scratch" "$project")
  if [ "$onair_ok" != "1" ]; then
    LEG_STATUS[fresh]="failed"; LEG_DETAIL[fresh]="on-air"
    attach_fresh_compose_logs "$scratch" "$project"
    return
  fi
  FRESH_ONAIR_SECS="$onair_elapsed"

  LEG_STATUS[fresh]="passed"
  LEG_DETAIL[fresh]=""
}

if [ "$DO_FRESH" = 1 ]; then
  run_fresh_leg
else
  LEG_STATUS[fresh]="skipped"; LEG_DETAIL[fresh]="--fresh not given"
fi

# resolve_previous_tag — SPEC F178.6: --from, when given, always wins outright (no `gh` call at
# all — see the prerequisite probe above). Otherwise the newest OTHER published `v*` release:
# --exclude-drafts --exclude-pre-releases narrows the release LIST itself to "published" ones
# (SPEC F178.6); filtering is done in jq here so the stub and the real CLI take the same path —
# `gh release list` itself already orders newest first, so the first candidate jq finds after
# excluding --tag IS the newest other one. Prints the resolved tag on success; prints nothing and
# returns non-zero when `gh` failed or no other `v*` release exists to fall back to.
resolve_previous_tag() {
  if [ -n "$FROM_TAG" ]; then
    printf '%s' "$FROM_TAG"
    return 0
  fi
  local raw candidate
  if ! raw="$(gh release list --limit 50 --exclude-drafts --exclude-pre-releases --json tagName)"; then
    return 1
  fi
  candidate="$(printf '%s' "$raw" | jq -r --arg tag "$TAG" '
      [ .[].tagName | select(test("^v[0-9]+\\.[0-9]+\\.[0-9]+$")) | select(. != $tag) ]
      | .[0] // empty
    ')"
  [ -n "$candidate" ] || return 1
  printf '%s' "$candidate"
}

# fail_upgrade_leg <detail> — every miss inside run_upgrade_leg PAST the point $worktree/$project
# exist marks the leg failed with the same two lines: LEG_STATUS/LEG_DETAIL[upgrade], then the
# SAME attach_fresh_compose_logs "$worktree" "$project" "compose-upgrade.log" call. $worktree/
# $project are read off run_upgrade_leg's own `local`s via bash's dynamic scoping — this helper is
# only ever called from inside that function's stack frame, never on its own. The caller still
# does its own `return` right after calling this — a `return` in here would only exit
# fail_upgrade_leg itself, not run_upgrade_leg. The three misses that happen BEFORE $project
# exists (resolve_previous_tag/worktree add/check_compose_v2) don't call this — there is no
# compose project yet for attach_fresh_compose_logs to dump.
fail_upgrade_leg() {
  LEG_STATUS[upgrade]="failed"; LEG_DETAIL[upgrade]="$1"
  attach_fresh_compose_logs "$worktree" "$project" "compose-upgrade.log"
}

# upgrade_boundary_query <worktree> <project> <role> <query> — SPEC F178.7 / STORY-446 (T494): one
# cross-schema probe of the role boundary, run as <role> via the same compose invocation shape
# capture_booth_log_count (above) uses against the db service — the db is up throughout the leg,
# api still stopped at this point (see run_upgrade_leg's own call site, between migrate and the
# api restart). The real db container's socket connection is `trust`, so no password is needed for
# either role.
#
# A real psql exits 1 on the very outcome we want (permission denied) and the stub always exits 0;
# the text is the only signal both share, so the status is ignored. Prints that text verbatim.
upgrade_boundary_query() {
  local worktree="$1" project="$2" role="$3" query="$4" out
  out="$(cd "$worktree" && docker compose -p "$project" "${COMPOSE_FILES[@]}" \
      exec -T db psql -U "$role" -d genwave -tA -c "$query" < /dev/null 2>&1)" || true
  printf '%s' "$out"
}

# run_upgrade_leg — SPEC F178.6/F178.7 / STORY-446 (T493/T494): a `git worktree` of the previous
# release stands up its OWN pinned stack (setup.sh --yes + the gate's own `up -d`, exactly the
# fresh leg's own shape — see below), waits health/on-air, then `compose stop api`, swaps the
# CURRENT tag's db/ + migrate.sh into the worktree, runs that migration, checks the role boundary
# (station_svc locked out of library.media, library_svc locked out of station.settings —
# upgrade_boundary_query above), brings api back up on the CURRENT tag, and waits health/on-air
# again.
run_upgrade_leg() {
  local scratch worktree project previous
  scratch="$(mktemp -d)"
  SCRATCH_DIRS+=("$scratch")
  worktree="$scratch/prev"

  if ! previous="$(resolve_previous_tag)"; then
    LEG_STATUS[upgrade]="failed"; LEG_DETAIL[upgrade]="previous"
    return
  fi
  UPGRADE_PREVIOUS="$previous"

  # `git worktree add` runs from $root — the ORIGINAL checkout this script was invoked from, NOT
  # a rsync scratch copy (those drop .git on purpose; see run_fresh_leg's own rsync, --exclude
  # .git). A worktree needs a real repo behind it to check the previous tag's tree out of.
  if ! (cd "$root" && git worktree add "$worktree" "$previous"); then
    LEG_STATUS[upgrade]="failed"; LEG_DETAIL[upgrade]="worktree"
    return
  fi
  # Registered immediately — a failure on any later line still gets the worktree torn down on
  # EXIT (cleanup, above), the same guarantee PROJECTS/SCRATCH_DIRS give the other two lists.
  WORKTREES+=("$worktree")

  # The leg's first docker call, from this run's own scratch — never the caller's checkout
  # (mirrors run_fresh_leg's own check_compose_v2 call; see its comment above).
  local compose_detail
  if ! compose_detail="$(check_compose_v2 "$scratch")"; then
    LEG_STATUS[upgrade]="failed"; LEG_DETAIL[upgrade]="$compose_detail"
    return
  fi

  # The previous release's own pinned overlay — stage_pinned_overlay_for_launch matters here for
  # the SAME reason it matters to the fresh leg: setup.sh --yes below launches the stack itself
  # (GW_PRESET=home-piper-only in the .env it writes makes its bare ./launch.sh read
  # compose.pinned.yaml, not compose.gate.yaml or COMPOSE_FILE).
  write_compose_gate_overlay "$worktree" "$previous"
  stage_pinned_overlay_for_launch "$worktree"

  local media_dir="$worktree/media"
  "$root/tools/gate/make_media.sh" "$media_dir"

  # Registered BEFORE setup.sh ever runs, not after — see run_fresh_leg's own comment on this
  # same pattern; the reasoning is identical, just against the worktree instead of a rsync scratch.
  project="$(gate_project_name upgrade)"
  PROJECTS+=("$worktree|$project")

  if ! (cd "$worktree" && gate_setup_answers "$media_dir" | \
        GW_ENV_FILE="$worktree/.env" \
        COMPOSE_PROJECT_NAME="$project" COMPOSE_FILE="$COMPOSE_FILE_LIST" \
        GW_STREAM_URL="${GATE_STREAM_URL:-http://localhost:8000/stream}" \
        ./setup.sh --yes); then
    fail_upgrade_leg "setup"
    return
  fi

  # The gate's own `up -d` — a raw compose call, NOT the previous worktree's own ./launch.sh: that
  # script's --pinned would also stack compose.demo.yaml (wrong here, same reason the fresh leg
  # never passes --pinned either), and a bare ./launch.sh from an OLDER release can't be trusted
  # to behave like the current one. Real setup.sh --yes already launched the stack itself and
  # waited on-air as part of --yes (same as the fresh leg); this call is that same idempotent
  # re-converge.
  if ! (cd "$worktree" && docker compose -p "$project" "${COMPOSE_FILES[@]}" up -d); then
    fail_upgrade_leg "up"
    return
  fi

  local health_ok health_elapsed onair_ok onair_elapsed
  read -r health_ok health_elapsed < <(wait_for_health)
  if [ "$health_ok" != "1" ]; then
    fail_upgrade_leg "health"
    return
  fi
  UPGRADE_HEALTH_SECS="$health_elapsed"

  read -r onair_ok onair_elapsed < <(wait_for_onair "$worktree" "$project")
  if [ "$onair_ok" != "1" ]; then
    fail_upgrade_leg "on-air"
    return
  fi
  UPGRADE_ONAIR_SECS="$onair_elapsed"

  # --- The cutover (SPEC F178.6) --------------------------------------------------------------
  if ! (cd "$worktree" && docker compose -p "$project" "${COMPOSE_FILES[@]}" stop api); then
    fail_upgrade_leg "stop"
    return
  fi

  # db/ + migrate.sh + the overlay all swap to the CURRENT tag's — $root is this script's own
  # invocation root (the tag under test), never the previous release's. The case guard keeps the
  # rm -rf pinned under this run's OWN mktemp scratch even if $worktree were ever miscomputed —
  # $worktree is always $scratch/prev, built two lines above, never caller input.
  local target_db="$worktree/db"
  case "$target_db" in
    "$scratch"/*) : ;;
    *)
      LEG_STATUS[upgrade]="failed"; LEG_DETAIL[upgrade]="db"
      return ;;
  esac
  rm -rf "$target_db"
  cp -a "$root/db" "$target_db"
  cp "$root/migrate.sh" "$worktree/migrate.sh"
  write_compose_gate_overlay "$worktree" "$TAG"

  # migrate.sh's own exit status, captured exactly (never via `if ! v=$(…)`, which loses it) — a
  # non-zero status is the fact this leg exists to catch, not just a boolean pass/fail. cwd/.env/
  # COMPOSE_* mirror what migrate.sh itself documents it needs (it cd's to its own dirname, then
  # `docker compose` picks up .env from there and COMPOSE_FILE for the file set); it never starts
  # or stops the stack itself, only the db service already running under it.
  local s=0 out
  out="$(cd "$worktree" && COMPOSE_FILE="$COMPOSE_FILE_LIST" COMPOSE_PROJECT_NAME="$project" \
        ./migrate.sh 2>&1)" || s=$?
  if [ "$s" -ne 0 ]; then
    UPGRADE_MIGRATE_TAIL="$(printf '%s\n' "$out" | tail -5)"
    fail_upgrade_leg "migrate"
    return
  fi

  # --- Role boundary (SPEC F178.7) ------------------------------------------------------------
  # After migration, while api is still stopped and the db is up: station_svc must be unable to
  # read library.media, and library_svc must be unable to read station.settings. Anything other
  # than "permission denied" on either side fails the leg; a row count means the migration granted
  # a role more than its own schema.
  local station_boundary library_boundary
  station_boundary="$(upgrade_boundary_query "$worktree" "$project" station_svc \
      "select count(*) from library.media")"
  library_boundary="$(upgrade_boundary_query "$worktree" "$project" library_svc \
      "select count(*) from station.settings")"
  if [[ "$station_boundary" == *"permission denied"* ]] && [[ "$library_boundary" == *"permission denied"* ]]; then
    UPGRADE_BOUNDARY="ok"
  else
    UPGRADE_BOUNDARY="failed"
    if [[ "$station_boundary" != *"permission denied"* ]]; then
      UPGRADE_BOUNDARY_TAIL="$(printf '%s\n' "$station_boundary" | tail -3)"
    else
      UPGRADE_BOUNDARY_TAIL="$(printf '%s\n' "$library_boundary" | tail -3)"
    fi
    fail_upgrade_leg "role boundary"
    return
  fi

  if ! (cd "$worktree" && docker compose -p "$project" "${COMPOSE_FILES[@]}" up -d api); then
    fail_upgrade_leg "restart"
    return
  fi

  read -r health_ok health_elapsed < <(wait_for_health)
  if [ "$health_ok" != "1" ]; then
    fail_upgrade_leg "health"
    return
  fi
  UPGRADE_HEALTH_SECS="$health_elapsed"

  read -r onair_ok onair_elapsed < <(wait_for_onair "$worktree" "$project")
  if [ "$onair_ok" != "1" ]; then
    fail_upgrade_leg "on-air"
    return
  fi
  UPGRADE_ONAIR_SECS="$onair_elapsed"

  LEG_STATUS[upgrade]="passed"
  LEG_DETAIL[upgrade]=""
}

if [ "$DO_UPGRADE" = 1 ]; then
  run_upgrade_leg
else
  LEG_STATUS[upgrade]="skipped"; LEG_DETAIL[upgrade]="--upgrade not given"
fi

# capture_booth_log_count <scratch> <project> — the same compose invocation shape (project, -f
# list, cwd) fresh_onair_frame (above) uses for its own `exec -T engine` metadata poll, read
# against the db service instead: how many station.booth_log rows carry a non-null segment_kind
# (speech aired through piper) in the last 7 minutes (SPEC F178.5's own fixed window — NOT derived
# from CAPTURE_SECS, which is a knob above defaulting to 360s and is not clamped: the SQL window
# stays 7 minutes whatever CAPTURE_SECS is, so a value above 420 undercounts speech aired early).
# Prints the trimmed count on success; a failed exec prints nothing and returns 1, which the
# caller treats as an unreadable count.
capture_booth_log_count() {
  local scratch="$1" project="$2" raw
  if ! raw="$(cd "$scratch" && docker compose -p "$project" "${COMPOSE_FILES[@]}" \
      exec -T db psql -U genwave -d genwave -tA -c \
      "select count(*) from station.booth_log where segment_kind is not null and occurred_at > now() - interval '7 minutes'" \
      < /dev/null)"; then
    return 1
  fi
  printf '%s' "$raw" | tr -d '[:space:]'
}

# run_api_down_scenario <scratch> <project> — SPEC F178.8(a) / STORY-447 (T496): fault injection
# that runs WHILE run_capture_leg's own recording is in flight (its caller backgrounds ffmpeg
# first — see run_capture_leg), so the outage lands inside the SAME capture the F178.5
# measurements are taken from, never a second recording. `compose stop api` (the same invocation
# shape run_upgrade_leg's own cutover uses: cd into the scratch, -p project, COMPOSE_FILES, then
# the verb — so both the docker stub's `*" stop api"*` case and a caller's own `EndsWith("stop
# api")` check match), sleep GATE_OUTAGE_SECS (default 90), `compose start api`, then reuse
# wait_for_onair's own poll loop — budgeted against GATE_RECOVERY_SECS (default 120) instead of
# GATE_ONAIR_SECS — until a frame carries a non-safe track_id.
#
# Sets the CHAOS_* globals only; never touches LEG_STATUS/LEG_DETAIL directly. run_capture_leg
# decides chaos's pass/fail once its own recording's silence count is known too (silence during
# the outage is the outage's own verdict and wins over a slow recovery — see run_capture_leg's
# post-measure attribution below). A `stop api`/`start api` that itself fails returns early with
# no recovery measured, so the leg reports "api-down recovery" — the spec's only recovery-side
# name — with the compose error on stderr above it.
run_api_down_scenario() {
  local scratch="$1" project="$2"
  local outage="${GATE_OUTAGE_SECS:-90}" recovery_budget="${GATE_RECOVERY_SECS:-120}"

  CHAOS_API_DOWN_RAN=1
  if ! (cd "$scratch" && docker compose -p "$project" "${COMPOSE_FILES[@]}" stop api); then
    return
  fi
  CHAOS_OUTAGE_SECS="$outage"
  sleep "$outage"
  if ! (cd "$scratch" && docker compose -p "$project" "${COMPOSE_FILES[@]}" start api); then
    return
  fi

  local recovery_ok recovery_elapsed
  read -r recovery_ok recovery_elapsed < <(wait_for_onair "$scratch" "$project" "$recovery_budget")
  if [ "$recovery_ok" = "1" ]; then
    CHAOS_RECOVERY_SECS="$recovery_elapsed"
  fi
}

# run_capture_leg — SPEC F178.5 / STORY-445 (T491): records CAPTURE_SECS of the fresh leg's own
# live stream and measures it — against the SAME stack run_fresh_leg just brought up
# (FRESH_SCRATCH/FRESH_PROJECT), never a second one. Every CAPTURE_* value below is set as soon
# as that step completes, even on a later failure, so the report always shows as much as was
# actually measured.
run_capture_leg() {
  local scratch="$FRESH_SCRATCH" project="$FRESH_PROJECT"
  local capture_secs="${CAPTURE_SECS:-360}"
  CAPTURE_SECS_VALUE="$capture_secs"

  local api_base; api_base="$(gate_api_base)"

  # Login (F178.5): the admin password THIS leg's own setup.sh --yes generated, read off the
  # scratch's `.env` with grep + cut (setup.sh writes the value unquoted) — never `source`d
  # (F178.1 isolation).
  local pw
  if ! pw="$(grep '^ADMIN_PASSWORD=' "$scratch/.env" | cut -d= -f2-)"; then
    pw=""
  fi
  if [ -z "$pw" ]; then
    LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="login"
    return
  fi

  # The password travels to curl over stdin (`-d @-`), never as an argv — an argv is readable by
  # any other process on the box via /proc/*/cmdline for as long as the process runs.
  local login_code
  if ! login_code="$(printf '%s' "$pw" | jq -Rc '{password:.}' | \
      curl -sS --max-time 10 -c "$scratch/cookies" \
      -H 'Content-Type: application/json' \
      -d @- \
      -o /dev/null -w '%{http_code}' "$api_base/api/auth/login")"; then
    LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="login"
    return
  fi
  if [ "$login_code" != "204" ]; then
    LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="login"
    return
  fi

  # Target (F178.5): the station's OWN configured loudness target, read back through the same
  # admin API a human would use, cookie-authenticated from the login above — never a gate default.
  local target
  if ! target="$(curl -sS --max-time 10 -b "$scratch/cookies" "$api_base/api/settings" \
      | jq -r '.[] | select(.key=="Loudness:TargetLufs") | .value')"; then
    LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="target"
    return
  fi
  if ! [[ "$target" =~ ^-?[0-9]+(\.[0-9]+)?$ ]]; then
    LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="target"
    return
  fi
  CAPTURE_TARGET_LUFS="$target"

  # Record (F178.5): CAPTURE_SECS of the live stream to a local WAV. -reconnect 1 rides out a
  # source hang-up the same way it would ride out a real Icecast reconnect. Always backgrounded
  # and `wait`ed on, --chaos or not, so the SAME recording is what the api-down scenario below
  # (SPEC F178.8(a): "with capture running") runs against — a foreground/background split would
  # be the one difference between the two code paths worth avoiding.
  local stream_url="${GATE_STREAM_URL:-http://localhost:8000/stream}"
  local ffmpeg_log_file="$scratch/ffmpeg-capture.log" ffmpeg_pid
  ffmpeg -nostats -hide_banner -loglevel error -y -reconnect 1 \
      -i "$stream_url" -t "$capture_secs" -ar 48000 -ac 1 "$scratch/capture.wav" \
      > "$ffmpeg_log_file" 2>&1 &
  ffmpeg_pid=$!

  if [ "$DO_CHAOS" = 1 ]; then
    run_api_down_scenario "$scratch" "$project"
  fi

  local ffmpeg_status=0
  wait "$ffmpeg_pid" || ffmpeg_status=$?
  local ffmpeg_log; ffmpeg_log="$(cat "$ffmpeg_log_file" 2>/dev/null || true)"

  # The api-down verdict is resolved here — BEFORE the recording-failure check below returns —
  # so a chaos scenario that ran is always given a verdict even when the recording itself later
  # fails. Silence, measured further down once the recording succeeds, can still override a
  # "passed" here to "api-down silence" (silence is the outage's own verdict and wins over a slow
  # recovery — see the measure case below).
  if [ "$CHAOS_API_DOWN_RAN" = 1 ]; then
    if [ -n "$CHAOS_RECOVERY_SECS" ]; then
      LEG_STATUS[chaos]="passed"; LEG_DETAIL[chaos]=""
    else
      LEG_STATUS[chaos]="failed"; LEG_DETAIL[chaos]="api-down recovery"
    fi
  fi

  if [ "$ffmpeg_status" -ne 0 ] || [ ! -s "$scratch/capture.wav" ]; then
    LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="capture"
    CAPTURE_FFMPEG_TAIL="$(printf '%s\n' "$ffmpeg_log" | tail -5)"
    return
  fi

  # Measure (F178.5): silencedetect + ebur128, both always on measure_audio.sh's own stdout line
  # — parsed here regardless of its exit code, so a failing measurement still lands in the report.
  local measure_out measure_status=0
  measure_out="$("$root/tools/gate/measure_audio.sh" "$scratch/capture.wav" \
      --target "$target")" || measure_status=$?
  CAPTURE_SILENCE_EVENTS="$(printf '%s\n' "$measure_out" | sed -n 's/.*silence_events=\([0-9]*\).*/\1/p')"
  CAPTURE_INTEGRATED_LUFS="$(printf '%s\n' "$measure_out" | sed -n 's/.*integrated_lufs=\(-\{0,1\}[0-9.]*\).*/\1/p')"
  case "$measure_status" in
    0) : ;;
    1)
      if [ -n "$CAPTURE_SILENCE_EVENTS" ] && [ "$CAPTURE_SILENCE_EVENTS" -gt 0 ]; then
        # Silence decided first, before loudness — and, with --chaos, attributed to the chaos
        # leg's "api-down silence" rather than the capture leg's own "silence" (SPEC F178.8(a):
        # "zero silence events across the outage"), overriding the recovery-only verdict set
        # above. The capture leg itself stays whatever it already was (untouched here) so its own
        # booth_log/measure verdicts below are still reached. measure_audio.sh's exit 1 covers
        # silence OR loudness, so under --chaos a silent capture skips the loudness verdict: the
        # capture leg can read passed with out-of-tolerance loudness while chaos carries the red
        # (the integrated value still lands in the report, and the gate still exits 1).
        if [ "$CHAOS_API_DOWN_RAN" = 1 ]; then
          LEG_STATUS[chaos]="failed"; LEG_DETAIL[chaos]="api-down silence"
        else
          LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="silence"
        fi
      else
        LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="loudness"
      fi
      ;;
    *)
      LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="measure"
      ;;
  esac

  # Speech aired (F178.5): run AFTER the measure so the query window covers the capture, but
  # recorded even when the measure above already failed — every number into the report.
  local booth_count
  if ! booth_count="$(capture_booth_log_count "$scratch" "$project")"; then
    booth_count=""
  fi
  CAPTURE_BOOTH_LOG="$booth_count"
  if [ "${LEG_STATUS[capture]}" != "failed" ]; then
    if ! [[ "$booth_count" =~ ^[0-9]+$ ]] || [ "$booth_count" -lt 1 ]; then
      LEG_STATUS[capture]="failed"; LEG_DETAIL[capture]="booth_log"
    fi
  fi

  if [ "${LEG_STATUS[capture]}" != "failed" ]; then
    LEG_STATUS[capture]="passed"; LEG_DETAIL[capture]=""
  fi
}

# run_engine_reconnect_scenario <scratch> <project> — SPEC F178.8(b) / STORY-447 (T497): runs
# AFTER run_capture_leg has fully returned (its own recording, measure and booth-log steps all
# done), never inside that recording — the api-down window's own capture must already have been
# measured clean before the engine is touched at all, since a real reconnect briefly interrupts
# the encode, and the test harness's fake stream flips to a different file the instant `restart
# engine` is logged. Only called by the caller below when LEG_STATUS[chaos] is already "passed"
# (api-down clean); resolves LEG_STATUS[chaos]/LEG_DETAIL[chaos] itself, unlike
# run_api_down_scenario, since there is no later caller left to do it for this one.
#
# `compose restart engine` restarts ONLY the engine container (the same invocation shape
# run_api_down_scenario uses, so the docker stub's `*" restart engine"*` case and a caller's own
# `EndsWith("restart engine")` check both match) — api/icecast/db keep running throughout. The
# engine reconnects to icecast on its own and, until it is fed again, plays the safe fallback it
# pulls from the api (engine/genwave.liq); the non-safe track_id this scenario polls for is PUSHED
# by the api's PlayoutFeederService, which opens a fresh TcpClient to the restarted control socket
# on its next tick (src/GenWave.Host/Playout/PlayoutFeederService.cs). A reconnect that never
# turns non-safe is therefore a feeder question first, not an engine one. A `compose restart
# engine` that itself fails returns early with no frame measured, so it is reported as
# "engine-reconnect on-air" — the spec's only on-air-side name — with the compose error on stderr.
# wait_for_health anchors the gap clock to "the container reporting healthy" (SPEC F178.8(b)); the
# api never actually goes unhealthy across an engine restart, so this returns at once on a real
# stack — its own result isn't itself a pass/fail gate here (not named in AC5-AC7). ENGINE_ONAIR_SECS
# is the total elapsed time from the restart command — health wait included — to the first
# non-safe frame; GATE_RECONNECT_SECS (default 60) bounds only the on-air poll itself, reusing
# wait_for_onair's own loop rather than a second one.
run_engine_reconnect_scenario() {
  local scratch="$1" project="$2"
  local budget="${GATE_RECONNECT_SECS:-60}"
  local gap_start=$SECONDS

  ENGINE_RESTART_RAN=1
  if ! (cd "$scratch" && docker compose -p "$project" "${COMPOSE_FILES[@]}" restart engine); then
    LEG_STATUS[chaos]="failed"; LEG_DETAIL[chaos]="engine-reconnect on-air"
    return
  fi

  wait_for_health >/dev/null

  local onair_ok onair_elapsed
  read -r onair_ok onair_elapsed < <(wait_for_onair "$scratch" "$project" "$budget")
  if [ "$onair_ok" != "1" ]; then
    LEG_STATUS[chaos]="failed"; LEG_DETAIL[chaos]="engine-reconnect on-air"
    return
  fi
  ENGINE_ONAIR_SECS=$((SECONDS - gap_start))

  # Post-restart capture (SPEC F178.8(b)): a FRESH 60-second recording — the spec fixes this
  # scenario's own capture length, independent of CAPTURE_SECS (the api-down window's length) —
  # same ffmpeg flags as run_capture_leg's own recording, foreground (nothing else needs to run
  # concurrently with it, unlike the api-down scenario inside the backgrounded capture leg).
  local reconnect_capture_secs=60
  local stream_url="${GATE_STREAM_URL:-http://localhost:8000/stream}"
  local ffmpeg_log_file="$scratch/ffmpeg-reconnect.log" ffmpeg_status=0
  ffmpeg -nostats -hide_banner -loglevel error -y -reconnect 1 \
      -i "$stream_url" -t "$reconnect_capture_secs" -ar 48000 -ac 1 "$scratch/capture-reconnect.wav" \
      > "$ffmpeg_log_file" 2>&1 || ffmpeg_status=$?
  if [ "$ffmpeg_status" -ne 0 ] || [ ! -s "$scratch/capture-reconnect.wav" ]; then
    LEG_STATUS[chaos]="failed"; LEG_DETAIL[chaos]="engine-reconnect capture"
    ENGINE_CAPTURE_FFMPEG_TAIL="$(tail -5 "$ffmpeg_log_file" 2>/dev/null || true)"
    return
  fi

  # Silence only (SPEC F178.5(a), via F178.8(b): "passes F178.5(a)") — loudness is F178.5(b) and
  # is deliberately NOT judged here, so measure_audio.sh's own exit code (which covers silence OR
  # loudness together) is never read below, only its silence_events= field off stdout, present
  # regardless of exit 0/1. CAPTURE_TARGET_LUFS is already set by the time this scenario can even
  # run (it is read before run_capture_leg's own recording starts, and this only runs once that
  # leg's chaos verdict is "passed") — --target is required by measure_audio.sh but its number
  # plays no part in this scenario's own verdict.
  local measure_out measure_status=0
  measure_out="$("$root/tools/gate/measure_audio.sh" "$scratch/capture-reconnect.wav" \
      --target "$CAPTURE_TARGET_LUFS")" || measure_status=$?
  if [ "$measure_status" -ge 2 ]; then
    LEG_STATUS[chaos]="failed"; LEG_DETAIL[chaos]="engine-reconnect measure"
    return
  fi
  # Same leniency as the capture leg: a measure exit 0/1 with no silence_events= on stdout (not
  # something measure_audio.sh does today) reads as no silence, with the null visible in the report.
  ENGINE_SILENCE_EVENTS="$(printf '%s\n' "$measure_out" | sed -n 's/.*silence_events=\([0-9]*\).*/\1/p')"
  if [ -n "$ENGINE_SILENCE_EVENTS" ] && [ "$ENGINE_SILENCE_EVENTS" -gt 0 ]; then
    LEG_STATUS[chaos]="failed"; LEG_DETAIL[chaos]="engine-reconnect silence"
  fi
}

if [ "$DO_CAPTURE" = 1 ]; then
  if [ "${LEG_STATUS[fresh]}" = "passed" ]; then
    run_capture_leg
  else
    LEG_STATUS[capture]="skipped"; LEG_DETAIL[capture]="fresh leg did not pass"
  fi
else
  LEG_STATUS[capture]="skipped"; LEG_DETAIL[capture]="--capture not given"
fi

# The api-down scenario (T496) runs INSIDE run_capture_leg itself (SPEC F178.8(a): "with capture
# running") and resolves LEG_STATUS[chaos]/LEG_DETAIL[chaos] there before run_capture_leg
# returns. `--chaos` without `--capture` already exits 2 at the usage check above, so one gap left
# here is "capture never got far enough to record at all" (e.g. its own login/target call failed
# before the recording — or before this leg, --fresh itself never passed) — chaos never ran in
# that case, and LEG_STATUS[chaos] is still empty. The other gap: the engine-reconnect scenario
# (T497) is a SEPARATE call, made here rather than from inside run_capture_leg, and only attempted
# once api-down has already resolved "passed" — an api-down failure already stands as chaos's
# first failure and the engine scenario simply never runs (its own measurements stay null, no
# extra report line).
if [ "$DO_CHAOS" = 1 ]; then
  if [ "${LEG_STATUS[chaos]}" = "passed" ]; then
    run_engine_reconnect_scenario "$FRESH_SCRATCH" "$FRESH_PROJECT"
  elif [ -z "${LEG_STATUS[chaos]}" ]; then
    LEG_STATUS[chaos]="skipped"; LEG_DETAIL[chaos]="capture leg did not record"
  fi
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

# capture_measurements_json — every CAPTURE_* value as a jq NUMBER, or `null` for a step the
# capture leg never reached (CAPTURE_SECS_VALUE empty means the leg never ran at all, so every
# field is null in that case too).
capture_measurements_json() {
  jq -n \
    --arg secs "$CAPTURE_SECS_VALUE" \
    --arg target "$CAPTURE_TARGET_LUFS" \
    --arg silence "$CAPTURE_SILENCE_EVENTS" \
    --arg integrated "$CAPTURE_INTEGRATED_LUFS" \
    --arg booth "$CAPTURE_BOOTH_LOG" \
    '{
      capture_secs: (if $secs == "" then null else ($secs | tonumber) end),
      target_lufs: (if $target == "" then null else ($target | tonumber) end),
      silence_events: (if $silence == "" then null else ($silence | tonumber) end),
      integrated_lufs: (if $integrated == "" then null else ($integrated | tonumber) end),
      booth_log: (if $booth == "" then null else ($booth | tonumber) end)
    }'
}

# capture_measurements_md — the same facts as capture_measurements_json, rendered as report
# lines; "none" when the capture leg never ran. A ffmpeg failure's stderr tail (CAPTURE_FFMPEG_TAIL)
# is appended as a diagnostic line when the recording step itself failed.
capture_measurements_md() {
  if [ -z "$CAPTURE_SECS_VALUE" ]; then
    printf 'none\n'
    return
  fi
  printf 'capture secs: %s\n' "$CAPTURE_SECS_VALUE"
  [ -n "$CAPTURE_TARGET_LUFS" ] && printf 'target: %s LUFS\n' "$CAPTURE_TARGET_LUFS"
  [ -n "$CAPTURE_SILENCE_EVENTS" ] && printf 'silence events: %s\n' "$CAPTURE_SILENCE_EVENTS"
  [ -n "$CAPTURE_INTEGRATED_LUFS" ] && printf 'integrated: %s LUFS\n' "$CAPTURE_INTEGRATED_LUFS"
  [ -n "$CAPTURE_BOOTH_LOG" ] && printf 'booth_log: %s\n' "$CAPTURE_BOOTH_LOG"
  [ -n "$CAPTURE_FFMPEG_TAIL" ] && printf 'capture error: %s\n' "$CAPTURE_FFMPEG_TAIL"
  return 0
}

# upgrade_measurements_json — every UPGRADE_* value as a jq NUMBER (or STRING for previous/role
# boundary), or `null` for a value the upgrade leg never reached (UPGRADE_PREVIOUS empty means the
# leg never resolved a previous release at all, so every field is null in that case too).
# health_secs/onair_secs hold whichever wait cleared last — the leg waits on both twice, once
# against the previous release and once again after the cutover, and the second measurement
# describes the tag actually being gated, so it is the one worth keeping. role_boundary (T494,
# SPEC F178.7) is "ok"/"failed"/null — a twin of the "role boundary | <value>" report-md line.
upgrade_measurements_json() {
  jq -n \
    --arg previous "$UPGRADE_PREVIOUS" \
    --arg h "$UPGRADE_HEALTH_SECS" \
    --arg o "$UPGRADE_ONAIR_SECS" \
    --arg boundary "$UPGRADE_BOUNDARY" \
    '{
      previous: (if $previous == "" then null else $previous end),
      health_secs: (if $h == "" then null else ($h | tonumber) end),
      onair_secs: (if $o == "" then null else ($o | tonumber) end),
      role_boundary: (if $boundary == "" then null else $boundary end)
    }'
}

# upgrade_measurements_md — the same facts as upgrade_measurements_json, rendered as report lines;
# "none" when the upgrade leg never resolved a previous release. A failed migrate's stderr tail
# (UPGRADE_MIGRATE_TAIL) is appended as a diagnostic line when that step is what failed the leg.
upgrade_measurements_md() {
  if [ -z "$UPGRADE_PREVIOUS" ]; then
    printf 'none\n'
    return
  fi
  printf 'upgrade previous: %s\n' "$UPGRADE_PREVIOUS"
  [ -n "$UPGRADE_HEALTH_SECS" ] && printf 'upgrade health_secs: %s\n' "$UPGRADE_HEALTH_SECS"
  [ -n "$UPGRADE_ONAIR_SECS" ] && printf 'upgrade onair_secs: %s\n' "$UPGRADE_ONAIR_SECS"
  [ -n "$UPGRADE_BOUNDARY" ] && printf 'role boundary | %s\n' "$UPGRADE_BOUNDARY"
  [ -n "$UPGRADE_BOUNDARY_TAIL" ] && printf 'role boundary error: %s\n' "$UPGRADE_BOUNDARY_TAIL"
  [ -n "$UPGRADE_MIGRATE_TAIL" ] && printf 'migrate error: %s\n' "$UPGRADE_MIGRATE_TAIL"
  return 0
}

# chaos_measurements_json — CHAOS_OUTAGE_SECS/CHAOS_RECOVERY_SECS/ENGINE_ONAIR_SECS/
# ENGINE_SILENCE_EVENTS as jq NUMBERs, or `null` for a value its own scenario never reached
# (CHAOS_API_DOWN_RAN staying 0 means the first pair is null; ENGINE_RESTART_RAN staying 0 means
# the second pair is null — the engine scenario only runs after a passed api-down, so the second
# pair null never implies anything about the first, while the first pair null implies the second).
chaos_measurements_json() {
  jq -n \
    --arg outage "$CHAOS_OUTAGE_SECS" \
    --arg recovery "$CHAOS_RECOVERY_SECS" \
    --arg onair "$ENGINE_ONAIR_SECS" \
    --arg silence "$ENGINE_SILENCE_EVENTS" \
    '{
      api_down_outage_secs: (if $outage == "" then null else ($outage | tonumber) end),
      api_down_recovery_seconds: (if $recovery == "" then null else ($recovery | tonumber) end),
      engine_reconnect_onair_seconds: (if $onair == "" then null else ($onair | tonumber) end),
      engine_reconnect_silence_events: (if $silence == "" then null else ($silence | tonumber) end)
    }'
}

# chaos_measurements_md — the same facts as chaos_measurements_json, rendered as report lines;
# "none" only when NEITHER scenario ever ran (each prints its own facts independently otherwise,
# since the engine-reconnect scenario can run — and fail early, before a silence reading exists —
# without the api-down scenario itself having failed).
chaos_measurements_md() {
  if [ "$CHAOS_API_DOWN_RAN" != 1 ] && [ "$ENGINE_RESTART_RAN" != 1 ]; then
    printf 'none\n'
    return
  fi
  [ -n "$CHAOS_OUTAGE_SECS" ] && printf 'api-down outage: %s s\n' "$CHAOS_OUTAGE_SECS"
  [ -n "$CHAOS_RECOVERY_SECS" ] && printf 'api-down recovery: %s s\n' "$CHAOS_RECOVERY_SECS"
  [ -n "$ENGINE_ONAIR_SECS" ] && printf 'engine-reconnect on-air: %s s\n' "$ENGINE_ONAIR_SECS"
  [ -n "$ENGINE_SILENCE_EVENTS" ] && printf 'engine-reconnect silence events: %s\n' "$ENGINE_SILENCE_EVENTS"
  [ -n "$ENGINE_CAPTURE_FFMPEG_TAIL" ] && printf 'engine-reconnect capture error: %s\n' "$ENGINE_CAPTURE_FFMPEG_TAIL"
  return 0
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
    printf '\n## Measurements\n\n%s\n' "$(fresh_measurements_md)"
    printf '%s\n' "$(upgrade_measurements_md)"
    printf '%s\n' "$(capture_measurements_md)"
    printf '%s\n\n' "$(chaos_measurements_md)"
    printf 'Needs manual evidence: the LLL ear — %s facts are manual\n' "$manual_facts"
  } > "$out_dir/gate-report.md"

  local first_failure fresh_json upgrade_json capture_json chaos_json capture_top_json chaos_top_json
  first_failure="$(first_failure_across_legs)"
  fresh_json="$(leg_json "${LEG_STATUS[fresh]}" "${LEG_DETAIL[fresh]}" "$(fresh_measurements_json)")"
  upgrade_json="$(leg_json "${LEG_STATUS[upgrade]}" "${LEG_DETAIL[upgrade]}" "$(upgrade_measurements_json)")"
  capture_json="$(leg_json "${LEG_STATUS[capture]}" "${LEG_DETAIL[capture]}" "$(capture_measurements_json)")"
  chaos_json="$(leg_json "${LEG_STATUS[chaos]}" "${LEG_DETAIL[chaos]}" "$(chaos_measurements_json)")"
  # Top-level twins of each leg's own numbers — SPEC F178.5's/F178.8's own consumers read
  # `.capture.target_lufs`/`.chaos.api_down_recovery_seconds` directly, not
  # `.legs.capture.measurements.target_lufs`/`.legs.chaos.measurements.api_down_recovery_seconds`.
  capture_top_json="$(capture_measurements_json | jq 'del(.capture_secs)')"
  chaos_top_json="$(chaos_measurements_json)"

  jq -n \
    --arg tag "$TAG" \
    --argjson fresh "$fresh_json" \
    --argjson upgrade "$upgrade_json" \
    --argjson capture "$capture_json" \
    --argjson chaos "$chaos_json" \
    --argjson capture_top "$capture_top_json" \
    --argjson chaos_top "$chaos_top_json" \
    --arg first_failure "$first_failure" \
    --argjson manual_facts "$manual_facts" \
    '{
      tag: $tag,
      legs: { fresh: $fresh, upgrade: $upgrade, capture: $capture, chaos: $chaos },
      capture: $capture_top,
      chaos: $chaos_top,
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
