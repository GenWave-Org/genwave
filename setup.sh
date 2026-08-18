#!/usr/bin/env bash
# setup.sh — the first-run wizard: four questions, generated secrets, a ready-to-launch .env
# (SPEC F132.1-.6, STORY-344).
#
# Lives at repo root, peer of launch.sh — wraps it, never re-implements compose orchestration
# (this script never calls `docker compose` directly — launch.sh alone owns that). Plain bash,
# plain numbered prompts, no whiptail/dialog dependency.
#
# Virgin box (no .env yet at this script's target path) -> the four-question interview, secrets
# generation, and one atomic .env write. Existing box (a .env is already there) -> routes to
# setup_adoption_mode, a stub for now: real verify/repair is STORY-346/T319's build, arriving in
# the next release slice. This script's contract on that branch is ROUTING and NEVER
# OVERWRITING an existing .env — nothing about the adoption mode's internals.
#
# Idempotency = derive, don't record (F132.4): there is no wizard state file anywhere. The one
# fact this script ever persists is .env itself — its presence *is* the "already set up" signal,
# and every other fact this run needs (SDK version, RAM, arch, audio file count) is read fresh
# from the machine every time, never cached to disk. Abandoning the interview at any point
# (closed stdin, Ctrl-C) leaves .env exactly as it was found: the target is written in ONE
# atomic step (a temp file next to it, then `mv`) only after every question has been answered —
# never incrementally, so a killed run can never leave a half-written .env.
#
# Seams (test-only; each defaults to the real path/value):
#   GW_ENV_FILE      — target .env path (default .env). Its presence/absence is also the
#                       virgin-vs-existing signal — same convention tools/preflight.sh and
#                       launch.sh already read this key with.
#   GW_MEMINFO_FILE   — RAM source for the topology recommendation (default /proc/meminfo —
#                       tools/preflight.sh's own seam of the same name, reused here).
#   GW_ARCH           — overrides `uname -m` for the topology recommendation's arm64/SBC check.
#   GW_FIND_CMD       — overrides the `find` binary count_audio_files shells out to (default
#                       find) — same seam name as tools/preflight.sh's own (sourced above),
#                       whose preflight_media_deep reads it independently; this script's specs
#                       drive both through the one env var.
#   GW_LAUNCH_CMD     — the command this script execs once ready to launch (default
#                       ./launch.sh; T318, STORY-345). Invoked BARE, no arguments — GW_PRESET,
#                       just written, IS the topology (F132.5). Story345's specs point this at a
#                       scripted stub instead of the real launch.sh, which would otherwise try
#                       to talk to a real Docker daemon.
#   GW_STREAM_URL     — the mount wait_for_on_air_bg polls for first audio (default
#                       http://localhost:8000/stream — the same mount launch.sh's own
#                       access-points printout names). Story345's specs point this at a scratch
#                       loopback HTTP server instead of a real Icecast.
#   GW_ONAIR_TIMEOUT_SECONDS — overrides GW_ONAIR_TIMEOUT_SECONDS_DEFAULT (below) — the poll
#                       budget wait_for_on_air_bg gives up after. Exists so Story345's poll-
#                       timeout (sad-path) spec doesn't have to wait the real, generous
#                       production budget out.
#   stdin             — the interview's answer channel: a caller pipes newline-terminated
#                       answers in, one per prompt.
# The .NET SDK probe (Q1) needs no seam of its own: like build.sh's own check, it is just
# `dotnet` present-or-absent on PATH (Gh019's idiom) — a scratch PATH with no dotnet stub
# already proves the pinned-only branch.
#
# Sources tools/preflight.sh for preflight_env_value (routing/topology reads) and its two
# hard-fail entry points, preflight_docker + preflight_env_secrets — run AFTER the .env write,
# before this script ever launches anything, so a machine or .env problem is caught before a
# single docker/compose call happens (a failure here still leaves the just-written .env in
# place; only the machine, not the file, is in question).
#
# T318/STORY-345 (F132.7-.8): once preflight clears, this script hands off to launch.sh itself
# via invoke_launch (GW_LAUNCH_CMD, default ./launch.sh) — bare, no topology flags, same
# convention resolve_preset_and_topology already documents. The mount poller (wait_for_on_air_bg)
# runs CONCURRENTLY with invoke_launch, not after it (T318 review round-1 BLOCKING finding F1):
# a staged pinned launch (home*) is already broadcasting minutes before launch.sh itself
# returns — it spends that gap pulling/converging the catch-up stage — so timing launch.sh's
# own wall clock would measure the WRONG thing entirely. launch.sh's own exit code still
# decides what happens next (the T316 rider, pinned in main): 0 = on air, proceed straight to
# the clock; 4 = DEGRADED-BUT-AIRING (the core is up, the catch-up stage didn't fully converge)
# — same clock/handoff path, but the handoff names the degradation and the catch-up command
# instead of pretending all is well; anything else means the launch genuinely failed — the
# poller is killed, no "On air" line is ever printed (even if it had already fired before the
# failure — the ordering race), and this script exits with launch.sh's own code after pointing
# at `docker compose ps` / `logs`.
#
# The clock (F132.7): t0 is stamped once, right as the interview starts (T0_SECONDS in main,
# bash's own $SECONDS builtin — a pure elapsed-time counter, no `date`/external dependency on
# this path at all) — not at launch, not at the mount poll. wait_for_on_air_bg is started in
# the background (`&`) in the SAME breath as invoke_launch, so its very first sample doubles as
# a stale-mount gate's snapshot: a mount that ALREADY serves audio at that instant can only be
# evidence of something other than this run — round-3 review BLOCKING finding B2: the gate is
# UNIVERSAL, never scoped to a preset (see wait_for_on_air_bg's own header for why "immediate
# 200 = success" is a claim about TIMING, never about which preset is running). Polls
# GW_STREAM_URL every ~1s via curl (F12: the poll interval IS the measurement's granularity now,
# not a separate 2s+2s blind spot) for an HTTP 200 with a nonzero body, up to
# GW_ONAIR_TIMEOUT_SECONDS_DEFAULT seconds (generous — a first run's image pulls can take
# minutes), ignoring any ambient proxy env (`--noproxy '*'` — a stray HTTP_PROXY must never make
# a loopback poll silently fail). curl is required (already a fact of any box that can run this
# stack's own compose healthchecks); its absence degrades to an honest can't-verify message plus
# the full handoff under launch.sh's own exit code, never a fabricated pass or a hardcoded
# failure (round-3 review finding N4). The moment audio is first detected, the poller prints a
# subordinate progress line straight to stdout — wording clearly distinct from the authoritative
# claim below, so an owner on a fresh Pi isn't left staring at a pull log for minutes while
# already live; the authoritative claim itself is still only made once main() has confirmed
# launch.sh's own exit code. Success prints "🎙️ On air in M:SS" and appends one greppable line
# to SETUP_LOG_FILE — a plain file next to ENV_FILE, gitignored, never the secrets file itself —
# that line's ISO timestamp is the one place this whole feature actually calls `date`
# (append_setup_log).
#
# The handoff (F132.8, print_handoff): the admin URL (localhost plus a LAN line for other
# devices on the network — T318 review F6, never the bare short hostname, which resolves on
# nobody's machine but this one), the generated ADMIN_PASSWORD shown exactly this once (T318
# review F2: read straight from SECRET_ADMIN_UI, the value this run itself generated — never
# read back via preflight_env_value, whose process-env-wins precedence can print an ambient
# caller's ADMIN_PASSWORD instead of the one actually written to .env), the persona-shelf deep
# link, what's still arriving in the background (derived from GW_PRESET + ADMIN_PROFILE — never
# hardcoded to one topology's services, and never a service this wizard could not possibly have
# composed — T318 review F3), and the exact next-run commands (T318 review F5: ./setup.sh's own
# line is worded for what it actually does on this branch — routes to a stub — not a verify
# promise STORY-346 hasn't shipped yet).
#
# tools/preflight.sh's own EXIT trap (F134.6's pass/warn summary table) fires after all of the
# above, on any exit path — see the trap setup right below the source line for why this script
# chains onto it rather than replacing it (T317 review LOW finding).
set -euo pipefail
cd "$(dirname "$0")"

. tools/preflight.sh

ENV_FILE="${GW_ENV_FILE:-.env}"
SECRET_LENGTH=40   # comfortably over F132.3's >=32-char floor

# T318/F132.7 — the on-air timing log: a plain file beside ENV_FILE (gitignored, never the
# secrets file itself — see append_setup_log). One greppable line per run.
SETUP_LOG_FILE="$(dirname "$ENV_FILE")/setup.log"

# Generous: a first run's core image pull (db/icecast/engine/api, +piper when selected) can
# take several minutes on a slow link before the mount ever starts serving audio. Overridable
# via GW_ONAIR_TIMEOUT_SECONDS (Story345's poll-timeout spec never waits this long for real).
GW_ONAIR_TIMEOUT_SECONDS_DEFAULT=900

# T318 review MEDIUM finding F8: a SET-but-garbage GW_ONAIR_TIMEOUT_SECONDS (a typo, a stray
# non-numeric override) used to go unvalidated straight into wait_for_on_air's `$(( ))`
# arithmetic, deep inside the poller — fail loudly here instead, at parse time, before a
# single question is asked. Unset is fine (falls through to the default above).
if [ -n "${GW_ONAIR_TIMEOUT_SECONDS:-}" ] && ! [[ "$GW_ONAIR_TIMEOUT_SECONDS" =~ ^[0-9]+$ ]]; then
  echo "setup.sh: GW_ONAIR_TIMEOUT_SECONDS must be a non-negative integer (got '${GW_ONAIR_TIMEOUT_SECONDS}')." >&2
  exit 1
fi

# --- EXIT trap: chain onto preflight's own (T317 review MEDIUM finding: stranded temp
# secrets) -----------------------------------------------------------------------------------
# tools/preflight.sh (sourced above) already registered `trap preflight_print_report EXIT` —
# bash keeps exactly one EXIT trap, so replacing it outright (a bare `trap ... EXIT` here)
# would silently drop that summary table (that file's own header CAUTIONs exactly this
# footgun). This registers ONE trap that does both jobs, in order: clean up any still-live
# `.env.setup.*` temp write (SETUP_TMP_ENV_FILE — set by apply_env_write only for the window
# between mktemp and mv, so a signal or a hard failure mid-write never leaves a secret-laden
# stray file on disk), THEN print whatever preflight recorded.
#
# T318 review BLOCKING finding F1: the same chaining discipline now also covers the background
# mount poller (SETUP_POLLER_PID, set only for the window main() has one actually running) and
# its stamp file (SETUP_ONAIR_STAMP_FILE) — a Ctrl-C (or any other signal) during the wait must
# never leave an orphan poller running after this script itself has exited.
SETUP_TMP_ENV_FILE=""
SETUP_POLLER_PID=""
SETUP_ONAIR_STAMP_FILE=""

# discard_poller — N2 (round-3 review): the kill/reap/rm-stamp/clear-PID sequence every path
# that stops trusting the background poller needs (a genuine launch failure, an operator
# Ctrl-C, a poll timeout, a clean join, and the shared EXIT trap below) — extracted once so the
# four call sites can never drift apart. Idempotent: safe to call with either tracker already
# empty (kill/wait on an already-reaped PID, or rm on an already-removed/never-created stamp
# file, are both no-ops), which is exactly what lets the EXIT trap call it unconditionally on
# every exit path.
discard_poller() {
  if [ -n "$SETUP_POLLER_PID" ]; then
    kill "$SETUP_POLLER_PID" 2>/dev/null || true
    wait "$SETUP_POLLER_PID" 2>/dev/null || true
    SETUP_POLLER_PID=""
  fi
  if [ -n "$SETUP_ONAIR_STAMP_FILE" ]; then
    rm -f "$SETUP_ONAIR_STAMP_FILE"
    SETUP_ONAIR_STAMP_FILE=""
  fi
}

setup_exit_trap() {
  discard_poller
  [ -n "$SETUP_TMP_ENV_FILE" ] && rm -f "$SETUP_TMP_ENV_FILE"
  preflight_print_report
}
trap setup_exit_trap EXIT

# =============================================================================
# Reality probes — every one of these reads the machine or the filesystem fresh;
# nothing here is ever cached (F132.4).
# =============================================================================

# check_dotnet10_sdk — Q1's gate: the build-your-own path is offered only when a .NET 10 SDK is
# on PATH right now (mirrors tools/preflight.sh's preflight_dotnet_sdk probe, but never hard-
# fails — an absent SDK just narrows Q1's menu instead of stopping the wizard).
check_dotnet10_sdk() {
  command -v dotnet >/dev/null 2>&1 || return 1
  dotnet --list-sdks 2>/dev/null | grep -q '^10\.'
}

# count_audio_files <dir> — the same case-insensitive .flac/.mp3 rule as
# tools/preflight.sh's preflight_media_deep (F134.5), reusing its GW_PREFLIGHT_AUDIO_EXTENSIONS
# array (sourced above) so the two lists can never drift apart. The walk itself is duplicated,
# not shared, because this script may not edit tools/preflight.sh.
#
# Prints a count, OR nothing at all (empty string) when `find` is missing — the same
# `command -v` guard parity as preflight_media_deep's own check (T317 review MEDIUM finding:
# this used to silently degrade a "couldn't check" machine to a verified-looking 0, which then
# showed interview_music's no-music/Jamendo lane over what might be a full library the probe
# simply couldn't see). Callers must treat an empty result as "unknown", never as zero.
count_audio_files() {
  local dir="$1" find_cmd="${GW_FIND_CMD:-find}"
  command -v "$find_cmd" >/dev/null 2>&1 || return 0

  local find_expr=() ext first=1
  for ext in "${GW_PREFLIGHT_AUDIO_EXTENSIONS[@]}"; do
    if [ "$first" -eq 1 ]; then
      find_expr+=(-iname "*.${ext}")
      first=0
    else
      find_expr+=(-o -iname "*.${ext}")
    fi
  done

  local count=0
  while IFS= read -r _; do
    count=$((count + 1))
  done < <("$find_cmd" "$dir" -type f \( "${find_expr[@]}" \) 2>/dev/null)
  printf '%s' "$count"
}

detect_ram_gib() {
  local meminfo="${GW_MEMINFO_FILE:-/proc/meminfo}" kib
  [ -r "$meminfo" ] || return 1
  kib="$(grep -m1 '^MemTotal:' "$meminfo" 2>/dev/null | grep -oE '[0-9]+' || true)"
  [ -n "$kib" ] || return 1
  printf '%s' $((kib / 1024 / 1024))
}

detect_arch() {
  printf '%s' "${GW_ARCH:-$(uname -m)}"
}

# recommend_topology <ram_gib-or-empty> <arch> — SPEC F132.2: under tools/preflight.sh's own
# F134.4 RAM floor (GW_PREFLIGHT_RAM_MIN_GIB, reused rather than redefined here), OR SBC-class
# arm64, recommends piper-only; everything else recommends full. The owner can always override
# at the prompt.
#
# "SBC-class arm64" is arm64 GATED ON low/unknown RAM (T317 review LOW finding), never bare
# arch: plenty of arm64 machines (Apple Silicon dev boxes, beefy ARM cloud servers) comfortably
# run Full, and the old bare-arch check over-recommended piper-only on every one of them
# regardless of headroom. A KNOWN sufficient RAM reading on arm64 falls through to the RAM
# check below and recommends full same as any other arch; an UNREADABLE /proc/meminfo on
# arm64 is treated as circumstantial SBC evidence (the class of device this heuristic exists
# for tends to be exactly the kind where that probe is flaky) and still recommends piper-only.
recommend_topology() {
  local ram_gib="$1" arch="$2"
  if [ -n "$ram_gib" ] && [ "$ram_gib" -lt "${GW_PREFLIGHT_RAM_MIN_GIB:-6}" ]; then
    printf 'piper-only'
    return
  fi
  case "$arch" in
    aarch64 | arm64)
      if [ -z "$ram_gib" ]; then
        printf 'piper-only'
        return
      fi
      ;;
  esac
  printf 'full'
}

# =============================================================================
# Secrets (F132.3)
# =============================================================================

# gen_secret [length] — an alnum-only /dev/urandom string (default $SECRET_LENGTH, >=32 per
# F132.3), safe to drop unquoted into .env (no shell-hostile characters). Reads bounded 256-byte
# chunks rather than piping urandom straight into `tr | head -c`: an unbounded upstream reader
# meeting a downstream `head -c` that closes early raises SIGPIPE in the upstream command, which
# under `set -o pipefail` would abort this script on what is otherwise a perfectly successful
# secret. Bounding the read at the SOURCE (this head, not the sink) means every command in the
# pipeline reaches its own natural EOF — no SIGPIPE, ever.
gen_secret() {
  local length="${1:-$SECRET_LENGTH}" secret=""
  while [ "${#secret}" -lt "$length" ]; do
    secret="${secret}$(head -c 256 /dev/urandom | LC_ALL=C tr -dc 'A-Za-z0-9')"
  done
  printf '%s' "${secret:0:$length}"
}

# =============================================================================
# The interview (F132.2) — plain numbered prompts, exactly four questions.
# =============================================================================

# prompt <question text, printed as-is> <result-var-name> [default]
# Reads one line from stdin into the CALLER's variable (bash's dynamic scoping — the caller
# declares it `local` first). On EOF (abandonment: a piped answer stream running dry, or a
# genuine Ctrl-D) this aborts immediately WITHOUT writing anything — .env is only ever written
# in the single atomic step at the very end of the interview, so an abort here always leaves the
# target exactly as it was found (F132.4/AC4).
#
# CAUTION: every local this function declares is double-underscore-prefixed on purpose. Every
# caller in this script passes "answer" as the result-var name — an unprefixed local here named
# (say) `answer` would shadow that same-named variable one frame up, so `printf -v` would set
# THIS function's own local instead of the caller's (bash resolves a bare name to the nearest
# scope in the call stack, innermost first).
prompt() {
  local __resultvar="$2" __default="${3:-}" __answer
  printf '%s' "$1"
  if ! IFS= read -r __answer; then
    echo >&2
    echo "setup.sh: input ended before the interview finished — aborting. Nothing was written; re-run setup.sh to try again." >&2
    exit 1
  fi
  [ -n "$__answer" ] || __answer="$__default"
  printf -v "$__resultvar" '%s' "$__answer"
}

# print_could_not_verify_count <dir> — the "couldn't check" message (T317 review MEDIUM
# finding), shared by both call sites in interview_music so the two can never drift apart.
print_could_not_verify_count() {
  local dir="$1"
  echo "   Could not verify the audio file count under ${dir} (find not found on this machine) — continuing; confirm manually that it has .flac/.mp3 files before launching."
}

# print_no_music_lane <dir> — F132.6: GenWave downloads no audio, ever. This is a starting
# list — Dean finalizes the actual copy at review.
print_no_music_lane() {
  local dir="$1"
  echo
  echo "   No .flac/.mp3 files found yet under ${dir} (those are the only supported formats)."
  echo "   GenWave downloads no audio itself — bring your own, or grab CC-licensed tracks from:"
  echo "     - Jamendo             https://www.jamendo.com/"
  echo "     - Free Music Archive  https://freemusicarchive.org/"
  echo "     - ccMixter            https://ccmixter.org/"
  echo "   You are responsible for the licensing terms of anything you add."
  echo
}

IMAGES_MODE="pinned"

# Q1 — pinned vs build-your-own. The build path is offered ONLY when check_dotnet10_sdk finds a
# .NET 10 SDK right now; otherwise this is pure information, no prompt at all.
interview_images() {
  echo
  echo "1) How should GenWave run?"
  if check_dotnet10_sdk; then
    echo "   [1] Pinned images (recommended) — published GHCR images, no build step"
    echo "   [2] Build from source — a .NET 10 SDK was detected on this machine"
    local answer
    prompt "   Choose [1]: " answer "1"
    case "$answer" in
      2) IMAGES_MODE="dev" ;;
      *) IMAGES_MODE="pinned" ;;
    esac
  else
    echo "   Running from pinned published images (no .NET 10 SDK detected — build-from-source needs one)."
    IMAGES_MODE="pinned"
  fi
}

MEDIA_DIR_ANSWER=""

# Q2 — where's the music. Validates the path (exists, readable), then counts audio files; zero
# files routes into the F132.6 no-music lane with its own re-check loop.
interview_music() {
  echo
  echo "2) Where is your music library?"
  local answer
  while :; do
    prompt "   Absolute path (.flac/.mp3 files): " answer ""
    if [ -z "$answer" ]; then
      echo "   A path is required."
      continue
    fi
    case "$answer" in
      *'$'*)
        echo "   '${answer}' contains '\$' — \$ is interpolated by compose in .env values, so part of this path would silently resolve to something else. Rename the directory or symlink it to a \$-free path, then try again."
        continue
        ;;
    esac
    # A path containing spaces is fine — MEDIA_DIR is written UNQUOTED (build_env_content)
    # and GenWave's own shell tooling (launch.sh/preflight's `cut -d= -f2-` reader) reads the
    # literal text after `=`, spaces included, with no shell re-parsing along that path (T317
    # review MEDIUM finding: a prior space rejection here was based on a shell-quoting
    # assumption that doesn't apply to how these values are actually read).
    if [ ! -d "$answer" ]; then
      echo "   '${answer}' does not exist or is not a directory — try again."
      continue
    fi
    if [ ! -r "$answer" ] || [ ! -x "$answer" ]; then
      echo "   '${answer}' is not readable by this user — fix its permissions and try again."
      continue
    fi
    break
  done
  MEDIA_DIR_ANSWER="$answer"

  local count nm_choice
  count="$(count_audio_files "$MEDIA_DIR_ANSWER")"
  if [ -z "$count" ]; then
    # "Couldn't check" (T317 review MEDIUM finding), distinct from a verified zero — never the
    # no-music lane's Jamendo lecture over a library the probe simply couldn't see.
    print_could_not_verify_count "$MEDIA_DIR_ANSWER"
    return
  fi
  while [ "$count" -eq 0 ]; do
    print_no_music_lane "$MEDIA_DIR_ANSWER"
    prompt "   [1] I've added files — check again   [2] Continue anyway (airs the Please-Stand-By loop until music lands)   Choose [1]: " nm_choice "1"
    case "$nm_choice" in
      2) break ;;
      *)
        count="$(count_audio_files "$MEDIA_DIR_ANSWER")"
        if [ -z "$count" ]; then
          print_could_not_verify_count "$MEDIA_DIR_ANSWER"
          return
        fi
        ;;
    esac
  done
}

TOPOLOGY="full"

# Q3 — topology preset, recommended from detected RAM/arch; the owner may override.
interview_topology() {
  echo
  echo "3) Topology preset"
  local ram_gib arch recommended default_choice answer
  ram_gib="$(detect_ram_gib || true)"
  arch="$(detect_arch)"
  recommended="$(recommend_topology "$ram_gib" "$arch")"
  echo "   Detected: ${ram_gib:-unknown} GiB RAM, arch ${arch} — recommended: ${recommended}"
  echo "   [1] Full — kokoro + ollama (richer voice/LLM, needs more RAM)"
  echo "   [2] Piper-only — lighter footprint, no LLM-backed TTS"
  default_choice=1
  [ "$recommended" = "piper-only" ] && default_choice=2
  prompt "   Choose [${default_choice}]: " answer "$default_choice"
  case "$answer" in
    2) TOPOLOGY="piper-only" ;;
    *) TOPOLOGY="full" ;;
  esac
}

ADMIN_PROFILE="admin"

# Q4 — optional profiles. admin is on by default; logging/tunnel are pointers to DEPLOYMENT.md,
# never interview questions (F132.2).
interview_profiles() {
  echo
  echo "4) Optional profiles"
  local answer
  prompt "   Enable the Admin UI? [Y/n]: " answer "y"
  case "$answer" in
    [Nn]*) ADMIN_PROFILE="" ;;
    *) ADMIN_PROFILE="admin" ;;
  esac
  echo "   Logging and Cloudflare Tunnel are optional add-ons — see DEPLOYMENT.md to enable them later."
}

# =============================================================================
# apply — the one true mutation (F132.4/AC4)
# =============================================================================

SECRET_POSTGRES=""
SECRET_LIBRARY_DB=""
SECRET_STATION_DB=""
SECRET_ICECAST_SOURCE=""
SECRET_ICECAST_ADMIN=""
SECRET_ADMIN_UI=""
GW_PRESET=""
GW_PREFLIGHT_TOPOLOGY_VALUE=""
GW_PREFLIGHT_DEMO_VALUE=""

apply_generate_secrets() {
  SECRET_POSTGRES="$(gen_secret)"
  SECRET_LIBRARY_DB="$(gen_secret)"
  SECRET_STATION_DB="$(gen_secret)"
  SECRET_ICECAST_SOURCE="$(gen_secret)"
  SECRET_ICECAST_ADMIN="$(gen_secret)"
  SECRET_ADMIN_UI="$(gen_secret)"
}

# resolve_preset_and_topology — the ONE function both the .env write (GW_PRESET) and the
# preflight inputs (GW_PREFLIGHT_TOPOLOGY/GW_PREFLIGHT_DEMO, exported in main) read their
# values from — the T316 one-source lesson: two independent readers deriving "what did the
# interview choose" is exactly the split-brain that bit launch.sh there (a hardcoded `.env`
# reader beside a GW_ENV_FILE-aware comment claiming parity it didn't have). SPEC F132.5's
# closed vocabulary v2 (re-amended 2026-08-18 at the T317 review, Dean's split-overlays
# ruling, SPEC F136.5): home | home-piper-only | dev | dev-piper-only. launch.sh is the ONLY
# reader of GW_PRESET in the whole repo. `home*` stacks base + compose.pinned.yaml (published
# GHCR images, the wizard's LAN station) — never compose.demo.yaml, which stays flag-only
# (`--pinned`) for the public appliance; GW_PREFLIGHT_DEMO_VALUE is therefore always "0" here
# (F134.3a: preflight's demo input is caller-resolved, never a hardcoded guess) — this
# wizard's interview has no path to the demo/public-appliance shape at all.
resolve_preset_and_topology() {
  GW_PREFLIGHT_TOPOLOGY_VALUE="$TOPOLOGY"
  GW_PREFLIGHT_DEMO_VALUE="0"
  case "${IMAGES_MODE}:${TOPOLOGY}" in
    pinned:full) GW_PRESET="home" ;;
    pinned:piper-only) GW_PRESET="home-piper-only" ;;
    dev:full) GW_PRESET="dev" ;;
    dev:piper-only) GW_PRESET="dev-piper-only" ;;
    *)
      echo "setup.sh: internal error — unrecognized images/topology combination '${IMAGES_MODE}:${TOPOLOGY}'" >&2
      exit 1
      ;;
  esac
}

# build_env_content — an ALLOWLIST (T317 review findings B1/B2), not a .env.example
# template pass: emits ONLY the six generated secrets, MEDIA_DIR, COMPOSE_PROFILES, and
# GW_PRESET, plus two commented pointers (#PUBLIC_HOST=, #TUNNEL_TOKEN=) for the public-
# appliance overlay. Nothing else from .env.example is copied — STATION_NAME,
# LIBRARY_ENRICHMENT_CONCURRENCY, and every other optional/documentation line stay OUT: this
# wizard has no answer for them (their C#/compose defaults already cover the wizard's target,
# a LAN station), and copying them anyway would mean fabricating values it was never asked
# about. PUBLIC_HOST/TUNNEL_TOKEN are written blank and COMMENTED — never a fabricated value —
# so compose.demo.yaml's `${VAR:?}` guards stay armed until an operator deliberately opts into
# that overlay (SPEC F136.5: the public appliance stays flag-only, `--pinned`) and fills them
# in themselves. Written CLEAN per the T316 rider: unquoted values, LF line endings, no
# trailing whitespace — a quoted or CRLF GW_PRESET makes launch.sh exit 2 with the \r
# invisible in its own error message.
build_env_content() {
  printf '# .env — written by setup.sh (SPEC F132.2-.5). See .env.example for the full set of\n'
  printf '# variables this stack understands and what each one does; re-running setup.sh never\n'
  printf '# overwrites this file (see the header above) -- edit it directly for anything beyond\n'
  printf '# these four questions.\n'
  printf '\n'
  printf '%s\n' "COMPOSE_PROFILES=${ADMIN_PROFILE}"
  printf '%s\n' "MEDIA_DIR=${MEDIA_DIR_ANSWER}"
  printf '\n'
  printf '%s\n' "POSTGRES_PASSWORD=${SECRET_POSTGRES}"
  printf '%s\n' "LIBRARY_DB_PASSWORD=${SECRET_LIBRARY_DB}"
  printf '%s\n' "STATION_DB_PASSWORD=${SECRET_STATION_DB}"
  printf '%s\n' "ICECAST_SOURCE_PASSWORD=${SECRET_ICECAST_SOURCE}"
  printf '%s\n' "ICECAST_ADMIN_PASSWORD=${SECRET_ICECAST_ADMIN}"
  printf '%s\n' "ADMIN_PASSWORD=${SECRET_ADMIN_UI}"
  printf '\n'
  printf '# for the public appliance — see DEPLOYMENT.md\n'
  printf '#PUBLIC_HOST=\n'
  printf '# for the public appliance — see DEPLOYMENT.md\n'
  printf '#TUNNEL_TOKEN=\n'
  printf '\n'
  printf '# Written by setup.sh (SPEC F132.5) — launch.sh is the ONLY reader of this key in the\n'
  printf '# whole repo. Closed vocabulary: home | home-piper-only | dev | dev-piper-only.\n'
  printf 'GW_PRESET=%s\n' "$GW_PRESET"
}

# apply_env_write — builds the COMPLETE file in a temp path next to the target (same
# filesystem, so `mv` is an atomic rename) and only then moves it into place. Abandonment
# before this point leaves nothing; abandonment during it is impossible for anything outside
# this script to observe (the target is untouched until the rename). SETUP_TMP_ENV_FILE (set
# for exactly this window) is what the shared EXIT trap near the top of this script cleans up
# if the process is killed between mktemp and mv — e.g. a signal mid-write (T317 review
# MEDIUM finding: stranded temp secrets). Named `.env.setup.*` so it matches the .gitignore
# entry that keeps a leftover from ever being staged by accident.
apply_env_write() {
  local dir
  dir="$(dirname "$ENV_FILE")"
  [ -d "$dir" ] || mkdir -p "$dir"
  SETUP_TMP_ENV_FILE="$(mktemp "${dir}/.env.setup.XXXXXX")"
  build_env_content > "$SETUP_TMP_ENV_FILE"
  mv -f "$SETUP_TMP_ENV_FILE" "$ENV_FILE"
  SETUP_TMP_ENV_FILE=""
}

# =============================================================================
# Routing (F132.1/AC6) + the ready-to-launch handoff
# =============================================================================

# setup_adoption_mode — an existing checkout/stack (a .env is already at ENV_FILE) never re-runs
# the interview. STUB for this task: full verify/repair is STORY-346/T319's build. This
# function's whole contract is ROUTING here and NEVER touching ENV_FILE — it exits before
# reading a single line of stdin.
setup_adoption_mode() {
  # "a .env already exists here", not "already configured" (T317 review LOW finding) — this
  # stub never opens the file, so it has no basis to claim the install is actually correct or
  # complete, only that the virgin-vs-existing signal (F132.4) tripped.
  echo "==> A .env already exists here (${ENV_FILE})."
  echo "    Verify/repair for existing installs is arriving in the next release slice (STORY-346)."
  echo "    Nothing was changed — your .env is untouched."
  echo "    To relaunch as-is: ./launch.sh (honors GW_PRESET from ${ENV_FILE})."
  exit 0
}

print_ready_to_launch() {
  echo
  echo "==> .env written to ${ENV_FILE} (GW_PRESET=${GW_PRESET})"
  echo "==> ready to launch — GW_PRESET already selects the ${GW_PRESET} shape, no flags needed"
}

# =============================================================================
# Launch, the clock, the handoff (F132.7-.8, STORY-345/T318)
# =============================================================================

# T0_SECONDS — stamped in main(), right as the interview starts (F132.7: t0 = first prompt).
# Bash's own $SECONDS (elapsed wall-clock seconds since this shell started) — not `date`: the
# clock only ever needs a DURATION, never a calendar timestamp, and $SECONDS is a shell builtin
# with zero external dependency (no `date` binary needed anywhere on the launch/poll path — see
# wait_for_on_air_bg below). $SECONDS is inherited by the background poller's subshell and keeps
# counting from the SAME reference point there (proven: bash forks a subshell's own $SECONDS
# base from the parent's current value, it does not reset to 0), so a duration computed inside
# that subshell is directly comparable to one computed here in main().
T0_SECONDS=""

# invoke_launch — the ONE place this script ever starts the stack: GW_LAUNCH_CMD (default
# ./launch.sh), invoked BARE — no topology flags. GW_PRESET, just written to ENV_FILE, IS the
# topology; launch.sh reads it itself (F132.5) whenever it's given no explicit flag. This
# function's return value is launch.sh's own exit code — main() is what interprets it (the
# T316 rider: 0/4 proceed to the clock, anything else is a genuine launch failure).
invoke_launch() {
  "${GW_LAUNCH_CMD:-./launch.sh}"
}

# mount_serves_audio <url> — one-shot check: HTTP 200 with a nonzero body, ignoring any ambient
# proxy env (T318 review LOW finding F9 — a stray HTTP_PROXY/http_proxy must never make a
# loopback poll silently fail or, worse, silently succeed against something else entirely).
# Assumes curl is present — callers check that once, up front.
mount_serves_audio() {
  local url="$1" response code size
  response="$(curl -s --noproxy '*' --max-time 2 -o /dev/null -w '%{http_code} %{size_download}' "$url" 2>/dev/null || true)"
  code="${response%% *}"
  size="${response##* }"
  [ "$code" = "200" ] && [[ "$size" =~ ^[0-9]+$ ]] && [ "$size" -gt 0 ]
}

# wait_for_on_air_bg <stamp-file> — T318 review BLOCKING finding F1's fix. Started in the
# BACKGROUND (`&`, in main()) in the same breath as invoke_launch — CONCURRENTLY with
# launch.sh, not after it returns. The old design measured launch.sh's own wall time: a staged
# pinned launch is already broadcasting minutes before it returns (it spends that gap on its
# stage-2 catch-up pull/up), so a "poll after launch returns" design measures the wrong thing
# entirely — a reviewer-scripted stub that aired at t+1 and returned at t+8 printed "On air in
# 0:09" under the old code.
#
# Polls GW_STREAM_URL every ~1s (F12: the poll interval IS the measurement's granularity now,
# not a separate 2s-request-timeout + 2s-sleep blind spot) for up to GW_ONAIR_TIMEOUT_SECONDS.
# On success, prints the round-3 review's progress line (see below) and writes the T0-relative
# elapsed $SECONDS to <stamp-file>, then returns 0. Return codes are DISTINCT past that (round-3
# review finding N4 — "couldn't verify" and "genuinely never came up" are different facts that
# main() must react to differently): 1 = polled the whole budget and never saw a 200, nothing
# written; 2 = curl isn't on this machine at all, nothing was ever polled. This function never
# prints "On air" itself — only main() does, and only once it has confirmed launch.sh's own exit
# code was 0 or 4 (see the ordering-race note in main(): a hard launch failure must never show a
# success line, even if this poller had already detected audio moments before that failure).
#
# Stale-mount gate (the F1 fix's second half; made UNIVERSAL at the round-3 review's BLOCKING
# finding B2 — dev-only scoping was provably wrong): this function's own first sample runs at
# essentially the same instant invoke_launch starts (both are kicked off by the same pair of
# statements in main(), no meaningful scheduling gap), so a mount that ALREADY serves audio at
# that instant can only be evidence of something OTHER than this run — a stale stack left over
# from a previous run, or an unrelated server already bound to the port. That holds no matter
# which preset was chosen: home's own first act is compose pull -> db -> migrate -> up, so
# nothing THIS run started could possibly be serving audio at t≈0 either — "immediate 200 =
# success" is a claim about TIMING, never about topology, and scoping it to preset names would
# leak launch.sh's internal flow into this script besides. A 200 is trusted only once this
# poller has since observed a non-serving sample — the gap a real launch actually produces
# before its own mount comes up, whether or not that launch tears anything down first. The true
# fast path is untouched: on a real first run nothing serves at t0, require_gap never arms, and
# first bytes are trusted immediately — this gate costs that path nothing.
wait_for_on_air_bg() {
  local stamp_file="$1"
  local url="${GW_STREAM_URL:-http://localhost:8000/stream}"
  local timeout="${GW_ONAIR_TIMEOUT_SECONDS:-$GW_ONAIR_TIMEOUT_SECONDS_DEFAULT}"

  if ! command -v curl >/dev/null 2>&1; then
    echo "==> can't verify on-air automatically (curl not found on this machine) — check manually: curl -I ${url}" >&2
    return 2
  fi

  echo "==> waiting for ${url} to serve audio (a first run's image pulls can take a few minutes)..."

  # B2: a mount already serving BEFORE this run's launch even began is never trusted until a
  # non-serving gap is observed — for every preset, unconditionally.
  local require_gap=0
  mount_serves_audio "$url" && require_gap=1

  local deadline
  deadline=$(( SECONDS + timeout ))
  while [ "$SECONDS" -lt "$deadline" ]; do
    if mount_serves_audio "$url"; then
      if [ "$require_gap" = "0" ]; then
        local detected_elapsed=$(( SECONDS - T0_SECONDS ))
        # UX addition ratified at the round-2 review: a subordinate progress line, printed
        # straight to stdout from this background poller the moment audio is first detected —
        # so an owner on a fresh Pi isn't staring at a pull log for minutes while already live.
        # Wording is deliberately distinct from the authoritative "🎙️ On air" claim (only
        # main() ever prints that, only after confirming launch.sh's own exit code) — nothing is
        # written to SETUP_LOG_FILE here.
        printf '    audio detected at %s — finishing the catch-up stage…\n' "$(format_mmss "$detected_elapsed")"
        printf '%s' "$detected_elapsed" > "$stamp_file"
        return 0
      fi
    else
      require_gap=0
    fi
    sleep 1
  done
  return 1
}

# format_mmss <seconds> — "M:SS", zero-padded seconds only (F132.7's exact clock format).
format_mmss() {
  local total="$1"
  printf '%d:%02d' "$(( total / 60 ))" "$(( total % 60 ))"
}

# append_setup_log <elapsed-seconds> — one greppable line per run: ISO-8601 UTC timestamp, the
# same M:SS duration the clock line prints, and GW_PRESET. Never a secret (F132.8's never-log-
# the-password rule) — SETUP_LOG_FILE is deliberately not where ADMIN_PASSWORD lives;
# print_handoff reads that straight from SECRET_ADMIN_UI instead.
append_setup_log() {
  local elapsed="$1"
  printf '%s on-air=%s preset=%s\n' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(format_mmss "$elapsed")" "$GW_PRESET" \
    >> "$SETUP_LOG_FILE"
}

# heavyweights_desc / extras_desc — T318 review BLOCKING finding F3: the DEGRADED-BUT-AIRING
# banner's list of services that MIGHT still be stale/missing, derived from GW_PRESET +
# ADMIN_PROFILE — never hardcoded (the previous copy claimed "kokoro/ollama, plus
# admin_ui/alloy/etc. when selected" unconditionally, which is wrong on two counts this wizard
# can actually produce: ollama exists only in compose.demo.yaml, which no wizard preset ever
# stacks — GW_PREFLIGHT_DEMO_VALUE is hardcoded "0" in resolve_preset_and_topology precisely
# because this wizard has no path to that overlay at all — and a piper-only preset has no
# kokoro to catch up on in the first place). Mirrors the SHAPE of launch.sh's own
# HEAVYWEIGHTS_DESC/EXTRAS_DESC derivation (that script owns those, this one may not edit it —
# F11: a spec fact pins per-preset parity between the two instead of a shared function).
#
# admin_ui is the only profile-gated extra named here (unlike launch.sh's own EXTRAS_DESC,
# which also lists alloy/cloudflared/dockerproxy): Q4 is this wizard's ONLY profile question,
# and it only ever writes COMPOSE_PROFILES=admin or empty (interview_profiles) — logging/tunnel
# are DEPLOYMENT.md pointers, never wizard-settable (F132.2), so a wizard-generated .env can
# never select alloy/cloudflared/dockerproxy in the first place.
heavyweights_desc() {
  case "$GW_PRESET" in
    *-piper-only) printf '%s' "" ;;
    *)            printf '%s' "kokoro" ;;
  esac
}

extras_desc() {
  [ -n "$ADMIN_PROFILE" ] && printf '%s' "admin_ui"
  return 0
}

# catchup_services_desc — heavyweights_desc + extras_desc, comma-joined, falling back to an
# honest "nothing beyond the core" when this preset+profile combination has nothing to name at
# all (e.g. a piper-only preset with the Admin UI declined). A manual join loop, not an
# `IFS=', '` + "${parts[*]}" trick — bash's `$*` expansion only ever uses the FIRST character
# of IFS as the separator, silently dropping the space and printing "kokoro,admin_ui".
catchup_services_desc() {
  local parts=() hw ex joined part
  hw="$(heavyweights_desc)"
  ex="$(extras_desc)"
  [ -n "$hw" ] && parts+=("$hw")
  [ -n "$ex" ] && parts+=("$ex")
  if [ "${#parts[@]}" -eq 0 ]; then
    printf 'nothing beyond the core'
    return
  fi
  joined="${parts[0]}"
  for part in "${parts[@]:1}"; do
    joined="${joined}, ${part}"
  done
  printf '%s' "$joined"
}

# describe_still_arriving <launch_exit> — F132.8/F136.4: what's still pulling/initializing in
# the background, derived from the CHOSEN preset (GW_PRESET) — never hardcoded to one
# topology's services (the T316 review lesson: a home-piper-only run must never claim kokoro is
# coming, and a home run must never claim piper's HF voice download is). Enrichment backfill
# applies to every preset alike (F135), so it always prints.
#
# T318 review BLOCKING finding F3: wording now varies on <launch_exit> too — at handoff time
# launch.sh HAS already returned, so on a clean exit (0) kokoro was actually pulled AND started
# by launch.sh's own stage 2 ("pulling" would be stale news); the honest "still arriving" claim
# there is warm-up/initialization, not the pull itself. On exit 4 (DEGRADED-BUT-AIRING) the
# catch-up stage genuinely may not have converged, so the original pulling-and-initializing
# wording stays accurate.
describe_still_arriving() {
  local launch_exit="$1"
  case "$GW_PRESET" in
    *-piper-only)
      echo "      - piper's voice (first-run Hugging Face model download)"
      ;;
    *)
      if [ "$launch_exit" = "4" ]; then
        echo "      - kokoro (TTS voice model — catching up: still pulling and/or initializing)"
      else
        echo "      - kokoro (TTS voice model — up; warming up and initializing after launch)"
      fi
      ;;
  esac
  echo "      - loudness/cue/energy/mood enrichment for your library (tracks already play; this fills in over time)"
}

# primary_lan_address — T318 review LOW finding F6: the box's outward-facing IP (the first
# address `hostname -I` reports), or empty if unavailable. Never the bare short hostname (the
# previous copy printed `http://thor:3000/`, which resolves on nobody's machine but this one,
# and not reliably even there) — localhost covers the operator's own browser, this covers every
# other device on the LAN this station is actually meant to be shared with (CLAUDE.md: "a small
# private community").
primary_lan_address() {
  command -v hostname >/dev/null 2>&1 || return 0
  # round-4 review F2: gate on `command -v awk` rather than letting a MISSING awk leak "awk:
  # command not found" onto stderr through the pipeline below — that failure comes from the
  # shell's own exec of awk, not from hostname, so the pre-existing `2>/dev/null` on `hostname
  # -I` alone never covered it.
  command -v awk >/dev/null 2>&1 || return 0

  # B1 (round-3 review): `hostname -I` is a GNU-ism — on busybox/Alpine/macOS `command -v
  # hostname` succeeds but `-I` exits 1; under `set -euo pipefail` an unguarded pipeline failure
  # here would propagate straight out of this function, and the call site's plain
  # `lan_addr="$(primary_lan_address)"` assignment would then trip errexit and kill the script
  # mid-handoff — after the admin URL line but before the once-only password, the persona link,
  # and the next-run commands. This function must be TOTAL: `|| true` absorbs any pipeline
  # failure, so a box that can't answer just yields an empty string, which the caller already
  # treats as "skip the LAN line" (T318 review LOW finding F6's own contract).
  local candidate
  candidate="$(hostname -I 2>/dev/null | awk '{print $1}')" || true

  # round-4 review F2: shape-check the candidate before trusting it — a `hostname -I` that
  # misbehaves can write a USAGE message to stdout instead of failing cleanly (repro: candidate
  # "usage", handoff prints `http://usage::3000/`). Only an IPv4- or IPv6-looking token is ever
  # returned; anything else (including empty) degrades to "no LAN line", the same contract a
  # hostname that can't answer -I at all already has.
  if [[ "$candidate" =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]] ||
     { [[ "$candidate" == *:* ]] && [[ "$candidate" =~ ^[0-9A-Fa-f:]+$ ]]; }; then
    printf '%s' "$candidate"
  fi
  return 0
}

# print_handoff <launch_exit> — F132.8: the once-only screen with everything the owner needs to
# actually use the station. ADMIN_PASSWORD (T318 review BLOCKING finding F2) is read straight
# from SECRET_ADMIN_UI — the value THIS run generated (apply_generate_secrets) and wrote to
# ENV_FILE — never read back via preflight_env_value: that reader's process-env-wins precedence
# means an ambient ADMIN_PASSWORD exported in the caller's own shell would print instead of the
# one this run actually generated and wrote. Never written to SETUP_LOG_FILE or anywhere else.
print_handoff() {
  local launch_exit="$1" lan_addr

  if [ "$launch_exit" = "4" ]; then
    echo
    echo "==> DEGRADED-BUT-AIRING: the core is broadcasting, but launch.sh's catch-up stage"
    echo "    ($(catchup_services_desc)) did not fully converge on the first try."
    echo "    Catch up any time: ./launch.sh"
  fi

  echo
  echo "==> you're on the air — here's everything you need:"
  echo

  if [ -n "$ADMIN_PROFILE" ]; then
    echo "    Admin UI       http://localhost:3000/"
    lan_addr="$(primary_lan_address)"
    if [ -n "$lan_addr" ]; then
      # round-4 review N2: primary_lan_address's own shape check admits IPv6 tokens (colon-
      # separated hex groups) as well as IPv4 — an IPv6 literal dropped straight into a URL
      # without brackets is ambiguous with the port's own colon (`http://2001:db8::1:3000/`).
      # Bracket only when the candidate actually contains a colon; an IPv4 dotted-quad never
      # does, so this never touches the common case.
      case "$lan_addr" in
        *:*) echo "                   http://[${lan_addr}]:3000/  (from other devices on your network)" ;;
        *)   echo "                   http://${lan_addr}:3000/  (from other devices on your network)" ;;
      esac
    fi
    echo "    Password       ${SECRET_ADMIN_UI}   (shown once — it's also in ${ENV_FILE}; change it there any time)"
    echo "    Hire a DJ      http://localhost:3000/persona-catalog"
  else
    echo "    Admin UI       disabled (fail-closed — 'admin' isn't in COMPOSE_PROFILES). Add it"
    echo "                   to ${ENV_FILE} and re-run ./launch.sh to turn it on."
  fi

  echo
  echo "    Still arriving in the background:"
  describe_still_arriving "$launch_exit"

  echo
  echo "    Next runs:"
  echo "      ./launch.sh    restart/relaunch as-is (GW_PRESET=${GW_PRESET} in ${ENV_FILE})"
  echo "      ./setup.sh     re-run any time — routes to a short stub today; guided verify/repair"
  echo "                     lands with STORY-346"
}

main() {
  if [ -f "$ENV_FILE" ]; then
    setup_adoption_mode
  fi

  echo "GenWave setup — four quick questions, then you're on air."

  T0_SECONDS="$SECONDS"   # F132.7: t0 stamps at the first prompt, right here.

  interview_images
  interview_music
  interview_topology
  interview_profiles

  apply_generate_secrets
  resolve_preset_and_topology
  apply_env_write

  echo
  echo "==> checking the machine and the .env just written"
  # Handed to preflight as the caller-resolved explicit input (F134.3a) — preflight itself
  # reads no preset/topology key. Both values come from resolve_preset_and_topology's own
  # output (above), the same source GW_PRESET itself was just written from — never a second,
  # independently-hardcoded read of the interview's answer (the T316 one-source lesson).
  export GW_PREFLIGHT_TOPOLOGY="$GW_PREFLIGHT_TOPOLOGY_VALUE"
  export GW_PREFLIGHT_DEMO="$GW_PREFLIGHT_DEMO_VALUE"
  preflight_docker
  preflight_env_secrets

  print_ready_to_launch

  # T318/STORY-345 (F132.7-.8, review round-1 BLOCKING finding F1) — launch, staged, no
  # topology flags (F132.5 already wrote GW_PRESET). The mount poller runs CONCURRENTLY with
  # invoke_launch, started here in the same breath: it, not launch.sh's own wall clock, is
  # what proves and times first audio (see wait_for_on_air_bg's own header for the full
  # reasoning and its stale-mount gate). SETUP_ONAIR_STAMP_FILE/SETUP_POLLER_PID are the shared
  # EXIT trap's own responsibility too (near the top of this script) — a Ctrl-C here can never
  # orphan the poller.
  local onair_stamp_file
  onair_stamp_file="$(mktemp)"
  SETUP_ONAIR_STAMP_FILE="$onair_stamp_file"

  wait_for_on_air_bg "$onair_stamp_file" &
  SETUP_POLLER_PID=$!

  # round-4 review F1 (BLOCKING): the trap is installed BEFORE invoke_launch, not after — bash
  # defers a SIGINT that arrives while it is blocked on a FOREGROUND child (invoke_launch is
  # exactly that) until the child itself exits; when the child SWALLOWS the signal (traps it and
  # keeps running — the shape of a staged launch.sh whose own inner `compose` call traps INT and
  # exits 130, which launch.sh itself maps to its own DEGRADED exit 4) rather than dying from it,
  # bash still runs whichever INT trap is live AT THE MOMENT the child exits, which is why the
  # trap has to predate this call. A trap installed only around the later poller join (the
  # round-3 fix, below) is too late for that shape: the signal was already deferred by the time
  # such a trap existed, and an un-caught deferred signal is instead consumed by the very NEXT
  # `wait` builtin — which used to be the poller join, returning 130 with no trap live to catch
  # it, landing straight in the poll-timeout branch and blaming the stack for the operator's own
  # Ctrl-C. Reset only once the whole concurrent phase (launch + poller join) is over, so default
  # signal handling covers everything past this point.
  local interrupted=0
  trap 'interrupted=1' INT

  # launch.sh's own exit code decides what happens next (the T316 rider): 0/4 proceed to the
  # clock; anything else is a genuine failure — checked below only once `interrupted` has been
  # ruled out. An operator abort takes PRECEDENCE over every other branch here: `interrupted` can
  # already be "1" by the time this line's own `invoke_launch` returns (see the trap note above).
  local launch_exit=0
  invoke_launch || launch_exit=$?

  local poller_exit=0
  if [ "$interrupted" != "1" ]; then
    case "$launch_exit" in
      0 | 4) ;;   # 0 = clean launch; 4 = DEGRADED-BUT-AIRING — both proceed to the mount poll.
      *)
        # Ordering race (F1): the poller may already have detected audio (or even written a
        # stamp) before this failure was observed — discard it regardless; a hard launch
        # failure never earns a success line or the handoff, no matter what the mount was
        # doing a moment ago.
        discard_poller
        echo >&2
        echo "setup.sh: launch failed (exit ${launch_exit}) — the stack is not in a good state." >&2
        echo "  Audio may have been briefly detected during the attempt, but the launch itself" >&2
        echo "  did not succeed, so no station is reliably on the air." >&2
        echo "  Diagnostics: docker compose ps   /   docker compose logs <service>" >&2
        exit "$launch_exit"
        ;;
    esac

    # launch.sh succeeded (0/4) — join the poller for whatever remains of its own budget; on a
    # fast launch it has very likely already stamped by the time we get here. An operator Ctrl-C
    # landing HERE (rather than mid-launch, above) interrupts this `wait` builtin early (bash
    # returns it with a status >128 once a trapped signal arrives while it's blocked) — the
    # round-3 fix's own shape, still correct: `interrupted` catches it below all the same.
    wait "$SETUP_POLLER_PID" || poller_exit=$?
  fi
  trap - INT

  # round-4 review F1 item 2 / F4: an interrupt that arrived mid-launch (poller_exit is still its
  # "0" default — the join above never ran) is folded into the same signalled bucket the `wait`
  # builtin's own >128 return already represents when the signal instead lands during the join —
  # both are the identical operator-abort fact, just observed at a different point.
  [ "$interrupted" = "1" ] && poller_exit=130

  # F3 (round-4 review): read whatever the stamp file holds (empty unless the poller actually
  # stamped) and discard the poller in ONE place, immediately after the join, rather than at
  # every branch below. The poller-exit vocabulary is now explicit: 0 = stamped, 1 = timeout,
  # 2 = no prober, >128 = signalled — never conflated with each other.
  local elapsed
  elapsed="$(cat "$onair_stamp_file" 2>/dev/null || true)"
  discard_poller

  case "$poller_exit" in
    0)
      echo
      echo "🎙️ On air in $(format_mmss "$elapsed")"
      append_setup_log "$elapsed"
      ;;
    2)
      # N4 (round-3 review): no prober on this box is not the same fact as "never proved it's
      # airing" — never fabricate a timing claim, and never mask launch.sh's own verdict behind
      # a hardcoded failure exit (launch itself may well have succeeded; this box just can't
      # prove it). Falls through to the same full handoff as a stamped success below, just
      # without the On-air line or the setup.log entry.
      ;;
    1)
      echo >&2
      echo "setup.sh: gave up waiting for the stream to serve audio — the stack came up but" >&2
      echo "  never proved it's actually broadcasting." >&2
      echo "  Diagnostics: docker compose ps   /   docker compose logs <service>" >&2
      # F7: the stack may well be up regardless — the operator still needs a way in.
      echo "  Your secrets (including the Admin UI password) are in ${ENV_FILE}." >&2
      if [ -n "$ADMIN_PROFILE" ]; then
        echo "  Admin UI (once you've confirmed the stream): http://localhost:3000/" >&2
      fi
      exit 1
      ;;
    *)
      # round-4 review N3: this catch-all used to treat EVERY non-{0,1,2} value as "signalled",
      # which also swallowed a genuinely unexpected internal poller exit (3-127 — e.g. some
      # future bug aborting the background subshell under this script's own `set -e`) under the
      # operator-abort message. `case` patterns can't express a numeric 129-255 range directly,
      # so the split is numeric, inside the one remaining catch-all arm.
      if [ "$poller_exit" -ge 129 ] 2>/dev/null && [ "$poller_exit" -le 255 ] 2>/dev/null; then
        # 129-255 = signalled — an operator Ctrl-C, whether it landed mid-launch (bash defers
        # running the trap until invoke_launch's own foreground child exits — see the trap
        # install above) or later during the join on $SETUP_POLLER_PID itself.
        echo >&2
        echo "setup.sh: interrupted — stopped waiting for the stream because you pressed Ctrl-C," >&2
        echo "  not because of a stack problem. The stack may still be starting; check with:" >&2
        echo "  docker compose ps   /   docker compose logs <service>" >&2
        exit 130
      fi
      # 3-127: not a value this script's own poller ever returns on purpose — an honest,
      # distinct message rather than misattributing it to either a timeout or an operator abort.
      echo >&2
      echo "setup.sh: the background stream-availability check ended unexpectedly (exit ${poller_exit})" >&2
      echo "  — this looks like a bug in setup.sh's own polling, not a stack problem." >&2
      echo "  Diagnostics: docker compose ps   /   docker compose logs <service>" >&2
      exit 1
      ;;
  esac

  print_handoff "$launch_exit"

  # F4: propagate launch.sh's own exit code (0 clean, 4 degraded-but-airing) — the handoff
  # screen is shown either way, but a degraded install must never report a clean 0 to a
  # caller (install.sh execs this script; a silent 0 would read as complete success).
  exit "$launch_exit"
}

main
