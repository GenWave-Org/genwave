#!/usr/bin/env bash
# launch.sh — tear the Docker stack down and bring it back up.
#
# Building is build.sh's job; this script just (re)launches. Compose will still build any
# missing image for a service that has a build: context, so a first launch works too.
# After a successful up it reports what it is actually running — built-image ages on the
# dev flow, the pinned tags under --pinned/GW_PRESET=home* (gh-#351: stale images used to
# verify silently).
#
# Single-station stack. Everything is published on localhost — no proxy, no FQDNs.
#
# The pins/topology split (SPEC F136.5): compose.pinned.yaml carries ONLY the published-
# GHCR-image mechanics for the five repo-built services; compose.demo.yaml carries ONLY the
# public-appliance topology (Caddy, port lockdown, ollama) and now REQUIRES stacking on
# compose.pinned.yaml. `--pinned` (below) stacks both; GW_PRESET=home* stacks the pins
# overlay alone — the wizard's LAN station, never the public appliance (that stays
# flag-only).
#
# Presets (STORY-201 / SPEC F78.10, vocabulary v2 SPEC F132.5/F136.5):
#   ./launch.sh              dev flow (default, unchanged): teardown, db-first up, wait for
#                             db healthy, ./migrate.sh --keep-going, full up, status.
#   ./launch.sh --pinned     demo/appliance flow, STAGED (SPEC F136, STORY-343): base +
#                             compose.pinned.yaml + compose.demo.yaml. Pull the core images
#                             (db, icecast, engine, api, +piper when the fallback profile is
#                             active) -> db up (--no-recreate) + health wait -> migrate.sh ->
#                             up -d --no-deps the core -> ON AIR -> an unscoped pull (fast
#                             no-op for the already-pulled core layers) -> an unscoped
#                             up -d --remove-orphans, converging every remaining pin (see the
#                             STAGED comment in the script body for why both --no-deps and
#                             the missing --no-recreate matter here). A failed stage-2
#                             pull/up leaves the already-airing core untouched and does not
#                             abort the launch, but it is NOT a silent success either: a
#                             degradation summary prints last and the script exits 4, not 0 —
#                             see "Exit codes" below. NEVER builds — it's meant for a box that
#                             only ever runs published GHCR images. Works on a fresh box
#                             (gh-#305) AND as the upgrade path: --no-recreate starts an
#                             absent/stopped db but never touches a running one, so an upgrade
#                             still restarts nothing before migrations pass. See
#                             DEPLOYMENT.md's "Applying migrations".
#   ./launch.sh --with a,b   merge a,b into COMPOSE_PROFILES (env var, else .env's value)
#                             for this launch's compose invocations.
#   ./launch.sh --piper-only low-memory/no-kokoro topology (gh-#242): merge
#                             compose.piper-only.yaml — always LAST, so its kokoro
#                             removal + depends_on reset win — into whichever file set
#                             the flow uses (dev; GW_PRESET=home's pinned pair; or
#                             --pinned's pinned+demo trio). kokoro never runs; every TTS
#                             render routes to the piper sidecar. Combined with a pinned
#                             overlay there are no heavyweights to stage behind, so that
#                             flow stays unstaged (one pull, one up).
#   ./launch.sh --dry-run    print the exact command plan (one per line, "plan> "-prefixed)
#                             plus the effective profile set ("plan-profiles> "), then exit
#                             0. Touches nothing — no docker/compose call is made at all.
#
# Presets compose, e.g. the sanctioned demo-box launch:
#   ./launch.sh --pinned --with logging,tunnel
#
# Env overrides:
#   BUILD=1 ./launch.sh      force a rebuild on the way up — dev flow only. BUILD=1 with
#                             --pinned or a home* GW_PRESET is a hard error (neither builds).
#   SKIP_PREFLIGHT=1 ./launch.sh  bypass machine preflight checks (gh-#19 escape hatch).
#   GW_PRESET=<preset> in .env  (SPEC F132.5, vocabulary v2 — closed set, CLOSED 2026-08-18
#                             at the T317 review): one of home, home-piper-only, dev,
#                             dev-piper-only. `home` = base + compose.pinned.yaml (published
#                             images, LAN station — no compose.demo.yaml, no PUBLIC_HOST).
#                             `home-piper-only` adds the piper-only overlay. `dev`/
#                             `dev-piper-only` are the from-source flow, unchanged. The
#                             retired v1 spellings (`pinned`, `pinned-piper-only` — the demo
#                             overlay used to ride along with them) are REJECTED loudly, not
#                             silently remapped: the public appliance is flag-only (--pinned)
#                             now. Honored ONLY when no explicit --pinned/--piper-only flag is
#                             given (an explicit flag always wins); an unrecognized value is a
#                             loud exit (2), never a silent fallback. launch.sh is the ONLY
#                             reader of this key in the whole repo.
#
# Exit codes:
#   0   success — dev flow, or a pinned-overlay flow (--pinned or GW_PRESET=home*) fully
#       converged through stage 2.
#   2   bad invocation (unknown flag, bad/retired GW_PRESET, BUILD=1 with a pinned overlay).
#   3   preflight/launch failure (tools/preflight.sh's preflight_fail); dev flow rolls the
#       partial stack back down, a pinned-overlay flow's stage-1 failures leave the stack
#       exactly as it was.
#   4   a pinned-overlay flow's stage 2 (the post-on-air pull/up) failed — the on-air core is
#       untouched, but heavyweights and/or profile-gated extras may be stale or missing; the
#       printed degradation summary names the catch-up command (the exact set depends on the
#       file set this launch used — see HEAVYWEIGHTS_DESC/EXTRAS_DESC in the script body).
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
# USE_PINNED_OVERLAY: whether compose.pinned.yaml joins the file set at all (SPEC F136.5).
# --pinned always implies it (below); GW_PRESET=home*, resolved further down, sets it alone
# — the wizard's LAN station never wants the demo overlay's Caddy/ollama/public-port
# lockdown. Kept distinct from PINNED (which alone drives whether compose.demo.yaml joins),
# since it, not PINNED, is what gates "never builds, pull-first" below.
USE_PINNED_OVERLAY=0
# Tracks whether the caller passed an explicit topology flag, independently of PINNED/
# PIPER_ONLY's final values — GW_PRESET (below, F132.5) is honored ONLY when neither was
# given; an explicit flag always outranks whatever GW_PRESET says.
TOPOLOGY_FLAG_GIVEN=0

while [ $# -gt 0 ]; do
  case "$1" in
    --pinned)
      PINNED=1
      TOPOLOGY_FLAG_GIVEN=1
      shift
      ;;
    --piper-only)
      PIPER_ONLY=1
      TOPOLOGY_FLAG_GIVEN=1
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

# --- GW_PRESET resolution (SPEC F132.5, vocabulary v2 SPEC F136.5) -----------------------
# launch.sh is the ONLY reader of GW_PRESET in the repo: it resolves flags + preset into
# concrete topology, so nothing else may read the key. Honored ONLY when the caller gave no
# explicit topology flag at all — an explicit --pinned/--piper-only always wins, even a
# lone --piper-only (dev flow) against a GW_PRESET=home in .env. Same effective-value
# convention as COMPOSE_PROFILES below: process env wins, else the env file's last
# assignment (GW_ENV_FILE, defaulting to .env — the preflight.sh test seam, mirrored here so
# the wizard's .env is read the same way by everything that reads it). Delegated to
# preflight.sh's own preflight_env_value (tools/preflight.sh, sourced above) instead of a
# second hand-rolled reader — that used to hardcode `.env` here while this comment already
# claimed GW_ENV_FILE parity, a split-brain a scratch GW_ENV_FILE would silently miss.
#
# `home`/`home-piper-only` set USE_PINNED_OVERLAY only — never PINNED, which alone adds
# compose.demo.yaml (the public appliance stays flag-only, F136.5). The retired v1
# spellings (`pinned`, `pinned-piper-only` — which used to mean base+demo together) fall
# through to the `*)` default below and exit loud: a stale pre-split .env must never be
# silently reinterpreted as either the new `home` shape or the demo shape.
GW_PRESET_VALUES="home, home-piper-only, dev, dev-piper-only"

if [ "$TOPOLOGY_FLAG_GIVEN" = "0" ]; then
  gw_preset_env_file="${GW_ENV_FILE:-.env}"
  gw_preset="$(preflight_env_value GW_PRESET)"

  case "$gw_preset" in
    "")                 ;;                    # unset — the existing bare-flag default stands
    home)               USE_PINNED_OVERLAY=1 ;;
    home-piper-only)    USE_PINNED_OVERLAY=1; PIPER_ONLY=1 ;;
    dev)                ;;                    # the plain dev flow — nothing to set
    dev-piper-only)     PIPER_ONLY=1 ;;
    *)
      echo "launch.sh: unrecognized GW_PRESET value '${gw_preset}' (from ${gw_preset_env_file})." >&2
      echo "  Valid values: ${GW_PRESET_VALUES}." >&2
      echo "  Fix GW_PRESET in ${gw_preset_env_file}, or pass an explicit --pinned/--piper-only flag instead." >&2
      exit 2
      ;;
  esac
fi

# --pinned always brings the pins overlay along (SPEC F136.5: --pinned = base + pinned +
# demo, three files).
[ "$PINNED" = "1" ] && USE_PINNED_OVERLAY=1

# --build never applies to a pinned-overlay flow (it only ever runs pulled images) — reject
# the combination up front, before any docker/compose call is made.
if [ "$USE_PINNED_OVERLAY" = "1" ] && [ "${BUILD:-0}" = "1" ]; then
  echo "launch.sh: BUILD=1 is incompatible with --pinned/a home* GW_PRESET (neither ever builds)." >&2
  exit 2
fi

# --- compose file selection: plain dev stack, +compose.pinned.yaml, +compose.demo.yaml ---
# USE_PINNED_OVERLAY (home* or --pinned) adds the pins overlay; PINNED (only --pinned, or a
# GW_PRESET that resolved to it — currently nothing does, the public appliance is flag-only)
# additionally adds the demo topology overlay on top. Order matters: compose.demo.yaml must
# follow compose.pinned.yaml so DEPLOYMENT.md's documented `-f` order matches what ships.
COMPOSE_ARGS=()
MIGRATE_ARGS=()
if [ "$USE_PINNED_OVERLAY" = "1" ]; then
  COMPOSE_ARGS=(-f compose.yaml -f compose.pinned.yaml)
  MIGRATE_ARGS=(-f compose.yaml -f compose.pinned.yaml)
fi
if [ "$PINNED" = "1" ]; then
  COMPOSE_ARGS+=(-f compose.demo.yaml)
  MIGRATE_ARGS+=(-f compose.demo.yaml)
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

# --- preflight topology inputs (F134.3a amendment): launch.sh alone resolves flags +
# GW_PRESET into concrete topology (above) and hands preflight the result as two explicit
# env inputs — tools/preflight.sh reads no preset key itself. Exported here, once topology
# is fully known, ahead of every preflight_docker/preflight_env_secrets call site below.
GW_PREFLIGHT_TOPOLOGY="full"
[ "$PIPER_ONLY" = "1" ] && GW_PREFLIGHT_TOPOLOGY="piper-only"
export GW_PREFLIGHT_TOPOLOGY

# 1 iff compose.demo.yaml is actually in this launch's file set — PINNED is that exact
# predicate (the only place demo.yaml gets added, above); USE_PINNED_OVERLAY alone
# (GW_PRESET=home*) never sets it (SPEC F134.3a/F136.5: derived from the resolved file set,
# never hardcoded to a preset name).
GW_PREFLIGHT_DEMO="0"
[ "$PINNED" = "1" ] && GW_PREFLIGHT_DEMO="1"
export GW_PREFLIGHT_DEMO

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

# --- profile merge (--with): existing COMPOSE_PROFILES (env, else GW_ENV_FILE/.env) + the
# given list. Same preflight_env_value delegate as GW_PRESET above (F5): this used to
# hardcode `.env` here regardless of GW_ENV_FILE, splitting from every other reader in the
# launch — a scratch-env-file test could set COMPOSE_PROFILES and see it silently ignored.
base_profiles="$(preflight_env_value COMPOSE_PROFILES)"

EFFECTIVE_PROFILES="$base_profiles"
if [ -n "$WITH" ]; then
  if [ -n "$EFFECTIVE_PROFILES" ]; then
    EFFECTIVE_PROFILES="$EFFECTIVE_PROFILES,$WITH"
  else
    EFFECTIVE_PROFILES="$WITH"
  fi
fi
[ -n "$EFFECTIVE_PROFILES" ] && export COMPOSE_PROFILES="$EFFECTIVE_PROFILES"

# --- staged startup (SPEC F136): the pinned flow's core-first, everything-else-after split -
# THIS is the authoritative comment for the staged design — every other mention of it below
# is a one-line pointer back here, not a restatement.
#
# CORE_SERVICES pull+start first (scoped, `up` adds `--no-deps`) and put the station on air;
# everything else — the TTS/LLM heavyweight(s) (kokoro always; ollama/ollama-init too, but
# ONLY when compose.demo.yaml is in the file set — that overlay is ollama's only home in this
# repo, GW_PRESET=home never composes it) AND every other profile-gated service this scoped
# stage never names at all (admin_ui, alloy, cloudflared, dockerproxy; +caddy, demo-file-set
# only) — pulls and converges afterwards via a second, UNSCOPED pull + up, never blocking or
# delaying "on air" (F136.1). See HEAVYWEIGHTS_DESC/EXTRAS_DESC below, near the degradation
# summary, for the per-file-set version of this list used in that message — recomputed from
# the resolved file set rather than hardcoded, so a home-preset degradation report never
# claims a service (ollama, caddy) that topology never composed in the first place.
#
# Two things make the split actually hold, both live-daemon-proven 2026-08-18 (F136 review
# findings F1-F3):
#   - stage 1's `up` carries `--no-deps`: a SCOPED `up` still starts every target's depends_on
#     set regardless of required:false — that flag only relaxes the health GATE a dependency
#     must clear, not membership in the set a scoped `up` brings up. Without it, api's
#     depends_on: kokoro pulls and health-waits kokoro right here, defeating the whole split.
#   - stage 2's `up` carries NO --no-recreate: that flag never recreates a service whose PIN
#     changed, only one that's entirely absent — proven to leave admin_ui/caddy/ollama STALE
#     across an upgrade (the gh-#93 regression class, documented in compose.demo.yaml itself).
#     Stage 2 has to be the plain pre-F136 `up -d --remove-orphans` to actually converge pins.
#
# STAGED is the one predicate both the --dry-run plan and the real invocations key off — true
# iff the pinned flow has heavyweights to stage behind at all. `--piper-only` profile-gates
# kokoro/ollama/ollama-init off unconditionally (compose.piper-only.yaml), so it has nothing
# to stage and stays the exact pre-F136 flow instead: one pull, one up, both unscoped, neither
# carrying --no-deps nor a service list.
STAGED=1
[ "$PIPER_ONLY" = "1" ] && STAGED=0

CORE_SERVICES=(db icecast engine api)
case ",${EFFECTIVE_PROFILES}," in
  # piper opted in as the TTS fallback (`--with fallback`, SPEC F99.3): a small CPU-only
  # sidecar, core-fast rather than a heavyweight — pulls and starts alongside the rest of
  # core instead of waiting on the catch-up stage.
  *,fallback,*) CORE_SERVICES+=(piper) ;;
esac

# STAGE1_TARGETS: CORE_SERVICES when STAGED, else empty — compose's own "every active-profile
# service" default when no service is named, so pull and up share the one array (a prior
# round kept a PULL_TARGETS/UP_TARGETS pair that always held the same value).
STAGE1_TARGETS=()
[ "$STAGED" = "1" ] && STAGE1_TARGETS=("${CORE_SERVICES[@]}")

# UP1_ARGS: the stage-1 `up` flags — --no-deps only when STAGED. Computed once here and
# consumed by both the --dry-run plan line and the real invocation below, so a regression in
# one is a regression in the other (round 2's F1 bug was exactly this pair drifting apart —
# a correct --dry-run string sitting beside a real `compose up` call with --no-deps hardcoded
# unconditionally).
UP1_ARGS=(-d --remove-orphans)
[ "$STAGED" = "1" ] && UP1_ARGS+=(--no-deps)

UP_ARGS=(-d)
[ "${BUILD:-0}" = "1" ] && UP_ARGS+=(--build)

# HEAVYWEIGHTS_DESC / EXTRAS_DESC: the stage-2 degradation summary's naming of what MIGHT be
# stale/missing, recomputed from the resolved file set (PINNED — compose.demo.yaml is or
# isn't in play) rather than hardcoded — ollama/ollama-init and caddy exist ONLY in
# compose.demo.yaml, so a GW_PRESET=home degradation report must never claim either of them
# (SPEC F136.5).
HEAVYWEIGHTS_DESC="kokoro"
EXTRAS_DESC="admin_ui, alloy, cloudflared, dockerproxy"
if [ "$PINNED" = "1" ]; then
  HEAVYWEIGHTS_DESC="kokoro, ollama"
  EXTRAS_DESC="caddy, ${EXTRAS_DESC}"
fi

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

# --- gh-#351: what is this launch actually running? --------------------------------------
# launch.sh never (re)builds (see the header — that's build.sh's job), so a dev box can
# `up` week-old images and quietly verify the OLD code. After a successful up, print the
# facts: which locally-built images came up and how old they are (dev flow), or which
# pinned tags came up (--pinned, where an image date means nothing — the tag is the fact).
# INFORMATIONAL ONLY, ruled on gh-#351: no implicit build, no staleness heuristic, no
# prompt. Every call in here is guarded and both call sites add `|| true` — a readout must
# never fail a launch that already succeeded.

# The services with a build: context, read from the rendered compose config — derived, not
# hardcoded, so the list can't rot when a service is added or changes posture. The render
# is profile-aware, so this is exactly the built set THIS launch ran. Rendered output is
# normalized: service names sit at two-space indent under `services:`, their keys at four —
# which is all the parsing this needs.
built_services() {
  compose config 2>/dev/null | awk '
    /^services:/                 { in_services = 1; next }
    in_services && /^[^ ]/       { in_services = 0 }       # left the services: block
    in_services && /^  [^ ]/     { svc = $1; sub(/:.*$/, "", svc); next }
    in_services && /^    build:/ { print svc }
  ' || true
}

# "built 6 days ago" from a created/now epoch pair. Under two minutes reads "built just
# now" — the first-launch case, where compose itself just built the missing image on the
# way up. Unit boundaries (120s/120m/48h) are chosen so a count of 1 never prints, which is
# what lets every phrase pluralize without a grammar branch.
age_phrase() {
  local created_epoch="$1" now_epoch="$2"
  local delta=$(( now_epoch - created_epoch ))
  if [ "$delta" -lt 120 ]; then
    echo "built just now"
  elif [ "$delta" -lt 7200 ]; then
    echo "built $(( delta / 60 )) minutes ago"
  elif [ "$delta" -lt 172800 ]; then
    echo "built $(( delta / 3600 )) hours ago"
  else
    echo "built $(( delta / 86400 )) days ago"
  fi
}

# Dev-flow readout: one line per locally-built service with its image's CreatedAt age, then
# the one hint that matters. Age comes from the service's CONTAINER's image, not an image-
# name lookup: whatever the container holds is by definition what this launch is playing
# out. A ⚠ marks an image meaningfully older than the newest — same-build.sh siblings
# finish minutes apart, so anything over an hour behind missed a rebuild.
print_built_image_ages() {
  local services svc cid image_id created created_epoch now_epoch newest_epoch i
  local names=() epochs=()

  services="$(built_services)"
  [ -n "$services" ] || return 0   # config unreadable, or nothing locally built: say nothing

  now_epoch="$(date +%s)"
  newest_epoch=0
  for svc in $services; do
    # -a: a container that came up and already stopped still names the image it ran.
    cid="$(compose ps -a -q "$svc" 2>/dev/null | head -n1 || true)"
    created=""
    if [ -n "$cid" ]; then
      image_id="$(docker inspect "$cid" --format '{{.Image}}' 2>/dev/null || true)"
      if [ -n "$image_id" ]; then
        created="$(docker image inspect "$image_id" --format '{{.Created}}' 2>/dev/null || true)"
      fi
    fi
    created_epoch=""
    if [ -n "$created" ]; then
      created_epoch="$(date -d "$created" +%s 2>/dev/null || true)"
    fi
    # No container, no image, or an unparseable date (a first launch mid-build, a coy
    # daemon): report it as freshly built rather than erroring — "built just now" is the
    # honest reading of an image compose only just produced.
    if [ -z "$created_epoch" ]; then
      created_epoch="$now_epoch"
    fi
    names+=("$svc")
    epochs+=("$created_epoch")
    if [ "$created_epoch" -gt "$newest_epoch" ]; then
      newest_epoch="$created_epoch"
    fi
  done
  [ "${#names[@]}" -gt 0 ] || return 0

  echo
  echo "==> built-image ages (informational — launching never rebuilds, gh-#351)"
  for i in "${!names[@]}"; do
    if [ $(( newest_epoch - ${epochs[$i]} )) -gt 3600 ]; then
      printf '    %-12s %s  ⚠ older than the newest build\n' "${names[$i]}" "$(age_phrase "${epochs[$i]}" "$now_epoch")"
    else
      printf '    %-12s %s\n' "${names[$i]}" "$(age_phrase "${epochs[$i]}" "$now_epoch")"
    fi
  done
  echo "    Run ./build.sh (or BUILD=1 ./launch.sh) to rebuild from source."
}

# Pinned-overlay readout: the tags being run. The repo's own published images all live under
# ghcr.io/genwave-org/ — that registry path IS the built-vs-pulled split on a pinned-overlay
# box (upstream pulls like postgres/ollama age on someone else's schedule; nothing to
# report). No rebuild hint here: a pinned-overlay flow never builds, and telling an appliance
# operator to run build.sh would be exactly the wrong advice.
print_pinned_image_tags() {
  local tags tag
  tags="$(compose config --images 2>/dev/null | grep '^ghcr\.io/genwave-org/' | sort -u || true)"
  [ -n "$tags" ] || return 0

  echo
  echo "==> pinned images this launch is running (gh-#351)"
  while IFS= read -r tag; do
    printf '    %s\n' "$tag"
  done <<< "$tags"
}

if [ "$USE_PINNED_OVERLAY" = "1" ]; then
  # --- pinned-overlay flow: pull -> db up -> migrate.sh -> up -d, never builds -----------
  # Covers BOTH shapes that stack compose.pinned.yaml: --pinned (+ compose.demo.yaml, the
  # public appliance) and GW_PRESET=home* (the wizard's LAN station, no demo overlay).
  #
  # Re-run hint carrying the flags this launch was actually given, not a hardcoded
  # "--pinned" — that used to drop --piper-only/--with, steering the user into a different
  # topology (gh-#305: kokoro included, on the 4GB box that opted out of it), and would now
  # also wrongly promote a bare home-preset run to the demo shape. A home-preset run that
  # took no explicit flags at all re-runs bare ("./launch.sh") and lets GW_PRESET resolve it
  # again next time.
  RELAUNCH="./launch.sh"
  [ "$PINNED" = "1" ] && RELAUNCH="$RELAUNCH --pinned"
  [ "$PIPER_ONLY" = "1" ] && RELAUNCH="$RELAUNCH --piper-only"
  [ -n "$WITH" ] && RELAUNCH="$RELAUNCH --with $WITH"

  if [ "$DRY_RUN" = "1" ]; then
    if [ "$STAGED" = "1" ]; then
      plan_line "$(compose_display) pull ${STAGE1_TARGETS[*]}"
    else
      # STAGED=0: see the authoritative comment above STAGED, near CORE_SERVICES — nothing to
      # stage behind, so this stays the exact pre-F136 flow (unscoped).
      plan_line "$(compose_display) pull"
    fi
    plan_line "$(compose_display) up -d --no-recreate db"
    plan_line "$(compose_display) ps -q db"
    plan_line "docker inspect <db container> --format {{.State.Health.Status}} (poll until healthy, up to 30x2s)"
    plan_line "./migrate.sh ${MIGRATE_ARGS[*]}"
    if [ "$STAGED" = "1" ]; then
      # Staged startup (SPEC F136.1) — see the authoritative comment above STAGED for why
      # stage 1 carries --no-deps and stage 2 carries neither --no-deps nor a service list.
      plan_line "$(compose_display) up ${UP1_ARGS[*]} ${STAGE1_TARGETS[*]}"
      plan_line "$(compose_display) pull"
      plan_line "$(compose_display) up -d --remove-orphans"
    else
      plan_line "$(compose_display) up ${UP1_ARGS[*]}"
    fi
    plan_line "record COMPOSE_FILE=$(compose_file_value) in .env (gh-#309)"
    plan_line "docker image prune -af --filter until=168h (success-path hygiene, gh-#441)"
    plan_line "docker builder prune -af"
    plan_line "$(compose_display) ps"
    plan_line "report pinned image tags — $(compose_display) config --images (informational, gh-#351)"
    plan_profiles
    exit 0
  fi

  # gh-#19 preflight — after --dry-run (which must touch nothing and needs no Docker),
  # before the first real docker call.
  preflight_docker
  preflight_env_secrets

  echo "==> pulling published images"
  if ! compose pull "${STAGE1_TARGETS[@]}"; then
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
  # compose.pinned.yaml [+ compose.demo.yaml] render), and it IS the mechanism for the case
  # it does cover.
  #
  # --no-deps only when STAGED — UP1_ARGS/STAGE1_TARGETS were computed once, above (see the
  # authoritative comment near CORE_SERVICES for why). Safe for this call specifically because
  # db is already explicitly health-waited above and api's other dependency (engine) is
  # convenience only (the feeder retries) — both are already CORE_SERVICES members brought up
  # in this same command anyway. When STAGED=0, UP1_ARGS carries no --no-deps and
  # STAGE1_TARGETS is empty, so this call is then byte-for-flags the pre-F136 unstaged
  # `up -d --remove-orphans`.
  if ! compose up "${UP1_ARGS[@]}" "${STAGE1_TARGETS[@]}"; then
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

  # Staged startup (SPEC F136.1/F136.3) — see the authoritative comment above STAGED, near
  # CORE_SERVICES, for why both calls below are deliberately UNSCOPED with no --no-recreate.
  # The core (or, when STAGED=0, the entire topology — it has nothing to stage behind) is on
  # air as of the `up` above. Stage 2 converges everything else next, best-effort: a failure
  # here must leave the already-airing core untouched and must NOT abort a launch that already
  # succeeded — never-silent outranks a complete catch-up, same as it outranks tidiness above.
  # It must also NOT report success (F4): STAGE2_DEGRADED drives a degradation summary + a
  # non-zero exit below, and skips the image prune (see gh-#441 below) so a rollback target
  # survives a degraded run.
  STAGE2_DEGRADED=0
  if [ "$STAGED" = "1" ]; then
    echo "==> pulling the remaining images (TTS/LLM backends + any profile-gated extras) — already on air"
    if ! compose pull; then
      echo "==> stage-2 image pull failed — the on-air core is untouched; everything else stays on whatever it last ran (or never starts, on a fresh box — the safe loop/templated patter carries the show). Re-run to retry: $RELAUNCH" >&2
      STAGE2_DEGRADED=1
    else
      echo "==> converging the full stack onto the pulled images"
      if ! compose up -d --remove-orphans; then
        # Same "status above" idiom as the core failure above — `ps -a`, not `ps`.
        compose ps -a || true
        echo "==> stage-2 up failed part-way (status above) — the on-air core is untouched. Re-run to retry: $RELAUNCH" >&2
        STAGE2_DEGRADED=1
      fi
    fi
  fi

  persist_compose_file

  # gh-#441: superseded release images accumulate ~1.5 GB per release and nothing else ever
  # prunes them — 46 GB of dead tags filled the demo box's disk mid-deploy (2026-08-09), and an
  # SD-card Pi hits that wall far sooner. Success-path hygiene only, gated on STAGE2_DEGRADED
  # (F3): every failure above bails via preflight_fail before reaching here, but a degraded
  # stage 2 (just above) still falls through to here with the on-air core running on its
  # PREVIOUS pins — those previous images are the rollback target, so pruning here would defeat
  # the very "keep the airing core untouched" promise stage 2 makes. `until=168h` keys on image
  # CREATED time, so everything in use plus roughly the last week of releases survives for
  # instant rollback; older tags go. The builder cache is pure waste on a pinned-overlay box,
  # which never builds (BUILD=1 with --pinned/a home* GW_PRESET errors at parse time).
  if [ "$STAGE2_DEGRADED" = "0" ]; then
    echo "==> pruning superseded images (kept: in-use + last 7 days)"
    docker image prune -af --filter "until=168h" | tail -1 || true
    docker builder prune -af >/dev/null 2>&1 || true
  fi

  echo "==> stack status"
  compose ps

  print_pinned_image_tags || true

  # F4: a degraded stage 2 must not read as a clean success — the summary is the LAST thing
  # printed (after every status readout above) and the exit code is distinct from 0 so a
  # caller/cron job can tell. The airing core is never touched by this — never-touch-a-
  # broadcasting-stack — only the catch-up is owed.
  if [ "$STAGE2_DEGRADED" = "1" ]; then
    echo
    echo "==> DEGRADED (exit 4): stage 2 did not fully converge. The on-air core (${CORE_SERVICES[*]}) is untouched and broadcasting normally."
    echo "    prune skipped — previous images kept for rollback."
    echo "    The TTS/LLM heavyweight(s) (${HEAVYWEIGHTS_DESC}) and any profile-gated extras (${EXTRAS_DESC}) may be missing or on stale pins — check the messages above for which stage-2 step failed."
    echo "    Catch up: $(compose_display) pull && $(compose_display) up -d --remove-orphans"
    echo "    Or simply re-run: $RELAUNCH"
    exit 4
  fi

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
  plan_line "report built-image ages — $(compose_display) config + docker image inspect (informational, gh-#351)"
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

print_built_image_ages || true
