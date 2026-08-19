#!/usr/bin/env bash
# setup.sh — the first-run wizard: four questions, generated secrets, a ready-to-launch .env
# (SPEC F132.1-.6, STORY-344).
#
# Lives at repo root, peer of launch.sh — wraps it, never re-implements compose ORCHESTRATION
# (up/pull/down stay launch.sh's alone; adoption mode's own drift probes, below, may still run
# READ-ONLY `docker`/`docker compose` inspection — config/ps/exec-a-select-query — through the
# GW_DOCKER_CMD seam, F137/T319). Plain bash, plain numbered prompts, no whiptail/dialog
# dependency.
#
# Virgin box (no .env yet at this script's target path) -> the four-question interview, secrets
# generation, and one atomic .env write. Existing box (a .env is already there) -> routes to
# setup_adoption_mode: verify (read-only drift report) by default, or verify-then-repair under
# --repair (F137, STORY-346, T319 — see that function's own header, further down, for the full
# contract). This script's write to ENV_FILE only EVER happens on the virgin path — adoption
# mode never opens it for writing at all.
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
#   GW_DOCKER_CMD     — the `docker` binary this script's adoption-mode drift probes invoke
#                       (default docker; T319, STORY-346). Mirrors GW_LAUNCH_CMD's own shape:
#                       every adoption-mode `docker ...`/`docker compose ...` call in this file
#                       goes through this seam, so Story346's specs can point it at a scripted
#                       stub and prove exactly which subcommands ran (read-only in verify mode,
#                       never more than the confirmed fix in repair mode) with no real daemon
#                       anywhere in the loop. tools/preflight.sh's OWN docker calls (preflight_docker
#                       et al, sourced above) are NOT behind this seam — that file is shared with
#                       build.sh/launch.sh and owns its own SKIP_PREFLIGHT escape hatch instead;
#                       adoption mode runs under SKIP_PREFLIGHT=1 in Story346's specs precisely so
#                       preflight's own (already-tested-elsewhere) checks stay out of this file's
#                       own facts.
#   stdin             — the interview's answer channel: a caller pipes newline-terminated
#                       answers in, one per prompt. Doubles as adoption-mode repair's per-item
#                       confirm channel (T319) when --repair runs without --yes.
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
# The handoff (F132.8, print_handoff): the admin URL (hostname-first, then the always-works
# LAN-IP line for other devices on the network, localhost demoted to a last-resort fallback —
# finding 2, gate-run round 2: Dean's own ruling OVERRIDES the earlier T318 review F6 call,
# which rejected the bare hostname outright; his reasoning: this wizard is read over SSH more
# often than not, so a URL that resolves back to the READER's own machine (localhost) is
# actively wrong there, while the plain hostname resolves for other devices via the home
# router's own mDNS/LLMNR), the generated ADMIN_PASSWORD shown exactly this once (T318
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
#
# Adoption mode: verify & repair for existing boxes (F137, STORY-346, T319, setup_adoption_mode
# et al. near the bottom of this file). An existing ENV_FILE routes here instead of the interview
# — never both. Two invocation shapes:
#   ./setup.sh                 VERIFY — read-only. Runs the F134 preflight (preflight_docker +
#                               preflight_env_secrets) plus six drift probes (.env completeness
#                               vs .env.example, unapplied schema migrations vs the repo's db/
#                               max, stale locally-built images — gh-#351, informational only per
#                               Dean's ruling, never a fix — orphaned profile containers, and
#                               disk-prune advice), prints one report, and NEVER writes a file,
#                               starts/stops/recreates a container, or pulls/prunes anything.
#                               Deliberate divergences (a DB-stored settings override, an operator
#                               COMPOSE_FILE customization) print as INFO, never as a finding.
#   ./setup.sh --repair [--yes]  REPAIR — runs the same verify pass first (nothing above changes),
#                               then walks every mechanically-fixable finding: prints its exact
#                               command, warns first if it will stop/restart a container (F137.3),
#                               and waits for a per-item y/N — or applies every one of them
#                               without prompting under --yes (F137.2). A finding with no safe,
#                               scriptable fix (env completeness, stale-image ages) stays
#                               report-only in both modes; the operator edits/rebuilds by hand.
#
# Exit codes (adoption mode only — the interview path's own vocabulary, above, is unaffected):
#   0   verify: no drift found (deliberate divergences, if any, are INFO-only — F137.4's
#       do-no-harm gate). repair: every finding was applied (or none existed to begin with).
#   2   bad invocation (unknown flag).
#   3   tools/preflight.sh's own preflight_fail (a genuine machine/secrets problem — same
#       meaning it always has).
#   5   verify: drift was found and reported (nothing was changed — do-no-harm still holds).
#       repair: at least one finding is still outstanding after the pass (declined, or its fix
#       command itself failed) — re-run ./setup.sh --repair to retry.
set -euo pipefail
cd "$(dirname "$0")"

. tools/preflight.sh

ENV_FILE="${GW_ENV_FILE:-.env}"
SECRET_LENGTH=40   # comfortably over F132.3's >=32-char floor

# GW_STREAM_PORT_DEFAULT — the handoff's own display constant (finding 1, post-v5.3.0 gate run):
# named here, not inline, so both print_handoff's stream block and the poll-timeout diagnostic
# share one source. Deliberately NOT derived from GW_STREAM_URL (the wait_for_on_air_bg poll
# seam, which Story345's specs point at a scratch loopback server on a random port) — the
# handoff has to show the real listener-facing URL a production box actually serves, and
# compose.yaml's own icecast port mapping ("8000:8000") is a fixed literal, not something any
# .env key configures — there is no seam to derive it from. 8000 is also the port launch.sh's
# own access-points printout already names for the same mount.
#
# The one overlay that DOES override this — compose.demo.yaml's `icecast: ports: !override []`
# (unpublishes 8000 entirely; Caddy reaches icecast over the internal `core` network instead,
# right for the public-appliance box that overlay is for) — is unreachable from this wizard: the
# public appliance stays flag-only (`--pinned`, SPEC F136.5), never something this interview can
# select, and resolve_preset_and_topology hardcodes GW_PREFLIGHT_DEMO_VALUE="0" for exactly that
# reason (this wizard has no path to compose.demo.yaml at all). 8000 is therefore not just a
# fallback guess for the runs this script can actually produce — it's the fact.
GW_STREAM_PORT_DEFAULT=8000

# Adoption mode's own CLI surface (T319, STORY-346) — parsed in main(), before the virgin-vs-
# existing routing decision. Meaningless on the virgin (interview) path; a first-run box simply
# ignores them (no --repair-only validation gate here — the least surprising behaviour for an
# operator who passes them out of habit before ever installing).
SETUP_REPAIR=0
SETUP_YES=0

# usage — dumps this file's own header comment (the launch.sh idiom), for -h/--help.
usage() {
  awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "$0"
}

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
#
# Finding 5 (post-v5.3.0 gate run): main() also calls preflight_print_report explicitly, right
# after the checks that populate it, so the table renders before the on-air line instead of
# waiting for this trap to fire at true process exit (after the handoff). This trap's own call
# below is therefore usually a no-op by the time it runs — preflight_print_report's own
# idempotency guard (tools/preflight.sh) is what makes that safe — but it stays here unconditionally
# for every path that never reaches the explicit call (a hard preflight_fail, in particular).
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
  # Adoption mode's own verify_print_report (T319) is called explicitly from
  # setup_adoption_mode, not chained here — its own report has to print BEFORE that function's
  # green/drift-found verdict line, and this trap only ever fires AFTER a function's own exit
  # call, which would put the table below the verdict instead of above it.
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
  echo "   [1] Full — kokoro (richer TTS voice, needs more RAM)"
  echo "   [2] Piper-only — lighter footprint, Piper voice instead of kokoro"
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

# =============================================================================
# Adoption mode: verify & repair for existing boxes (F137, STORY-346, T319)
# =============================================================================
#
# The report: two parallel row arrays (status/label/message), one row per probe/preflight check
# — the SAME shape tools/preflight.sh's own GW_PREFLIGHT_ROW_* arrays already use, kept as a
# SEPARATE set here (GW_VERIFY_ROW_*) since these rows are adoption-mode's own drift probes, not
# F134's machine checks (preflight prints its own table separately, unchanged).
GW_VERIFY_ROW_STATUS=()
GW_VERIFY_ROW_LABEL=()
GW_VERIFY_ROW_MESSAGE=()

# verify_record PASS|WARN|INFO|UNKNOWN "<label>" "<message>" — PASS/INFO/UNKNOWN rows are
# report-only. A WARN row is a "finding" for exit-code purposes (F137's honest-exit rule) whether
# or not it also carries a repairable command — see verify_add_finding below for the repairable
# subset.
verify_record() {
  GW_VERIFY_ROW_STATUS+=("$1")
  GW_VERIFY_ROW_LABEL+=("$2")
  GW_VERIFY_ROW_MESSAGE+=("$3")
}

# The repairable subset of findings — parallel arrays again, PLUS one dynamically-named array
# PER finding (GW_VERIFY_FINDING_CMD_<index>) holding its exact command as real argv, not a
# joined string. This is the T316 "one array source, never a printed twin" discipline applied
# here: verify_run_repair's own display line and its own execution both read the SAME array via
# a nameref (see verify_run_repair), so a finding's PRINTED command and its EXECUTED command can
# never diverge — there is only ever one copy.
GW_VERIFY_FINDING_ID=()
GW_VERIFY_FINDING_LABEL=()
GW_VERIFY_FINDING_MESSAGE=()
GW_VERIFY_FINDING_RESTARTS=()
GW_VERIFY_FINDING_COUNT=0

# verify_add_finding <id> <label> <message> <restarts:0|1> <command...>
# Records a WARN report row (verify_record) AND a repairable finding in the same call — every
# repairable finding IS a WARN row; a few probes below call verify_record directly instead for a
# PASS/INFO/UNKNOWN row, or for an advisory WARN that has no safe scripted fix at all (env
# completeness, stale image ages) and so is never offered to verify_run_repair.
verify_add_finding() {
  local id="$1" label="$2" message="$3" restarts="$4"
  shift 4
  local idx="$GW_VERIFY_FINDING_COUNT"
  GW_VERIFY_FINDING_ID+=("$id")
  GW_VERIFY_FINDING_LABEL+=("$label")
  GW_VERIFY_FINDING_MESSAGE+=("$message")
  GW_VERIFY_FINDING_RESTARTS+=("$restarts")
  # Dynamic array naming is the only way bash stores a per-index ARRAY (not a scalar) without a
  # second, parallel, string-joined copy of the same command — exactly the plan/real divergence
  # class T316 closed off for launch.sh's own UP1_ARGS. "$@" is expanded INSIDE the eval'd array
  # literal, so every argument becomes its own quoted element regardless of embedded spaces —
  # never a format-string or word-splitting hazard; every caller here is this script's own probe
  # code, nothing attacker-controlled ever reaches this eval.
  eval "GW_VERIFY_FINDING_CMD_${idx}=(\"\$@\")"
  GW_VERIFY_FINDING_COUNT=$((GW_VERIFY_FINDING_COUNT + 1))
  verify_record WARN "$label" "$message"
}

# verify_print_report — one line per recorded row, emoji-marked by status (PASS/INFO/UNKNOWN vs
# a finding).
#
# F4 (round-3 review): this comment used to claim it was "chained onto the shared EXIT trap" —
# false; setup_exit_trap's own remarks (near the top of this file) say the opposite. The real
# contract: setup_adoption_mode calls this explicitly, ordered BEFORE that function's own
# green/drift-found verdict line, so the table always prints above the verdict rather than below
# it (an EXIT-trap ordering could never guarantee that). Because the call is explicit, not
# trap-driven, an abort mid-probe (a hard crash before setup_adoption_mode reaches this line)
# drops the table entirely — nothing here rescues that path the way a trap-fired call would.
verify_print_report() {
  [ "${#GW_VERIFY_ROW_STATUS[@]}" -gt 0 ] || return 0
  echo
  echo "==> Verify: existing-install drift report (SPEC F137)"
  local i status label message symbol
  for i in "${!GW_VERIFY_ROW_STATUS[@]}"; do
    status="${GW_VERIFY_ROW_STATUS[$i]}"
    label="${GW_VERIFY_ROW_LABEL[$i]}"
    message="${GW_VERIFY_ROW_MESSAGE[$i]}"
    case "$status" in
      PASS)    symbol="✅" ;;
      INFO)    symbol="ℹ️ " ;;
      UNKNOWN) symbol="❓" ;;
      *)       symbol="⚠️ " ;;
    esac
    printf '    %s %-24s %s\n' "$symbol" "$label" "$message"
  done
}

# --- shared plumbing the probes below build on --------------------------------------------

# GW_VERIFY_COMPOSE_FILE_VALUE — COMPOSE_FILE (gh-#309, written by launch.sh's
# persist_compose_file after a successful launch), read ONCE per verify run (round-2 review N6:
# five separate call sites used to each re-read it independently via preflight_env_value — every
# derivation below now reads this one cached copy instead). Populated by
# verify_resolve_env_facts, which MUST run before any of the functions below (setup_adoption_mode
# calls it first, ahead of even preflight_docker/preflight_env_secrets, which also consume the
# topology/demo values derived from it).
GW_VERIFY_COMPOSE_FILE_VALUE=""

# GW_VERIFY_COMPOSE_ARGS — this box's own last-launched file set, straight from
# GW_VERIFY_COMPOSE_FILE_VALUE — NEVER a second, hand-derived resolution of GW_PRESET (F132.5:
# launch.sh is the ONLY reader of that key in the whole repo; adoption mode reads COMPOSE_FILE
# instead, a different key entirely, which is also a strictly BETTER signal here — it names what
# this box actually last ran, not merely what an interview once chose). Empty when the box has
# never completed a launch — every probe that needs it degrades to UNKNOWN rather than guessing.
#
# T321 wire finding 2: also carries `--env-file "$ENV_FILE"`, ahead of the `-f` pairs, once
# COMPOSE_FILE resolves — every `docker compose` call in this file goes through this ONE array
# (never a per-call-site flag), so every render/ps/exec now interpolates `${VAR:?}`-class
# compose refs (compose.demo.yaml's PUBLIC_HOST, etc.) from the SAME file setup.sh's own reads
# already honor via GW_ENV_FILE — not compose's own default of `.env` in $PWD, which a caller
# running this script from outside the checkout (T321 run 1: GW_ENV_FILE pointed elsewhere,
# no .env in cwd at all) leaves silently unset, degrading three probes to UNKNOWN even though
# GW_ENV_FILE itself was honored correctly everywhere else. Added only alongside the `-f`
# pairs (never on its own) — an empty array still means "never launched" to every probe that
# checks its length (verify_orphaned_containers, verify_compose_overrides).
GW_VERIFY_COMPOSE_ARGS=()

verify_resolve_env_facts() {
  GW_VERIFY_COMPOSE_FILE_VALUE="$(preflight_env_value COMPOSE_FILE)"

  GW_VERIFY_COMPOSE_ARGS=()
  if [ -n "$GW_VERIFY_COMPOSE_FILE_VALUE" ]; then
    # `read -ra` (not an unquoted `local -a files=($compose_file)`) splits on IFS without ALSO
    # globbing each resulting word — round-2 review N6: a COMPOSE_FILE value containing a `*`
    # would otherwise expand against whatever happens to be in the current directory. ':' is
    # COMPOSE_FILE's own separator here (COMPOSE_PATH_SEPARATOR's Linux default — confirmed
    # against launch.sh's own persist_compose_file, `paste -sd:`, the only writer of this key;
    # `docker compose ls`'s comma-joined CONFIG FILES column is that command's own display
    # rendering, not the value actually persisted in .env).
    local -a files=()
    local IFS=':'
    read -ra files <<< "$GW_VERIFY_COMPOSE_FILE_VALUE"
    unset IFS
    GW_VERIFY_COMPOSE_ARGS=(--env-file "$ENV_FILE")
    local f
    for f in "${files[@]}"; do
      GW_VERIFY_COMPOSE_ARGS+=(-f "$f")
    done
  fi
}

# verify_compose_file_is_stacked <basename> — true iff this box's own COMPOSE_FILE names a file
# whose OWN BASENAME is exactly <basename> (T321 wire finding 1 follow-up, reviewer-proven):
# every derivation below used to test `case ":${GW_VERIFY_COMPOSE_FILE_VALUE}:" in
# *"compose.demo.yaml"*)` — a plain SUBSTRING test against the whole colon-joined string — which
# false-positives on compose.demo.yaml.bak, overlays/compose.demo.yaml.local, or
# my-compose.demo.yaml: none of those stack the actual overlay this repo ships, yet the old
# substring test called every one of them a match. Fixed by scanning GW_VERIFY_COMPOSE_ARGS'
# own `-f` pairs (the same scan verify_compose_overrides already uses) and comparing each
# element's BASENAME, not its full value, against <basename> exactly — deliberately basename,
# not the whole element, because the Pi 4's own persisted COMPOSE_FILE is PATH-QUALIFIED
# (`/home/dmills/genwave/compose.demo.yaml`, the box's real, live shape: launch.sh's own
# compose_file_value records whatever `-f` argument that launch actually ran with, and this
# box's own launches always ran from its checkout's own absolute path) — basename comparison
# classifies that box exactly the same as one that persisted a bare `compose.demo.yaml`, one
# rule for both shapes. This repo has never shipped two different compose*.yaml files under
# different directories sharing one basename, so a basename collision is not a risk this
# comparison has to guard against. MUST run after verify_resolve_env_facts (every caller in
# this file already does — setup_adoption_mode calls it first, ahead of every probe).
verify_compose_file_is_stacked() {
  local want="$1" i f
  for ((i = 0; i < ${#GW_VERIFY_COMPOSE_ARGS[@]}; i++)); do
    [ "${GW_VERIFY_COMPOSE_ARGS[$i]}" = "-f" ] || continue
    f="${GW_VERIFY_COMPOSE_ARGS[$((i + 1))]}"
    [ "${f##*/}" = "$want" ] && return 0
  done
  return 1
}

# verify_topology_from_compose_file / verify_demo_from_compose_file — the F134.3a preflight
# inputs, derived from the SAME cached COMPOSE_FILE value above rather than GW_PRESET (same
# single-reader reasoning) — this is what lets adoption mode still run "the F134 preflight" (its
# own F137.1 contract) with a topology-aware disk/port check, on a box this script never
# interviewed.
verify_topology_from_compose_file() {
  if verify_compose_file_is_stacked "compose.piper-only.yaml"; then
    printf 'piper-only'
  else
    printf 'full'
  fi
}

verify_demo_from_compose_file() {
  if verify_compose_file_is_stacked "compose.demo.yaml"; then
    printf '1'
  else
    printf '0'
  fi
}

# verify_resolve_db_container_id — resolves the db service's container id under this box's own
# file set into GW_VERIFY_DB_CONTAINER_ID (empty when it can't be determined: docker unreachable,
# db not running under this project). Every read-only docker/docker compose call in adoption mode
# goes through GW_DOCKER_CMD (default docker — see this file's own header) so Story346's specs
# never need a real daemon.
#
# F8 (round-3 review): memoized — this file has two call sites (verify_migrations,
# verify_db_settings_overrides) that both want the SAME db container id, and calling out to
# `docker compose ... ps -q db` twice per run for one unchanging fact was pure waste (N6's own
# "one source, not two independent readers" discipline, applied here to a lazily-resolved fact
# rather than an eagerly-resolved one like GW_VERIFY_COMPOSE_FILE_VALUE — not every verify run
# reaches either call site at all: verify_migrations short-circuits to UNKNOWN before ever needing
# it once the marker itself can't be established, so resolving it unconditionally up front would
# add a docker call some runs never needed in the first place).
#
# A PLAIN function call, never `$(verify_resolve_db_container_id)` — bash runs a command
# substitution in its OWN subshell, so a naive "memoize inside the function, callers capture its
# stdout via $(...)" shape (this fix's own first draft) silently never memoizes anything at all:
# every $(...) call forks a fresh subshell, sets the RESOLVED flag inside THAT subshell's own
# copy, then the subshell exits and takes the mutation with it — the parent shell's flag never
# flips. Every caller below calls this bare, then reads GW_VERIFY_DB_CONTAINER_ID directly.
GW_VERIFY_DB_CONTAINER_ID=""
GW_VERIFY_DB_CONTAINER_ID_RESOLVED=0

verify_resolve_db_container_id() {
  if [ "$GW_VERIFY_DB_CONTAINER_ID_RESOLVED" != "1" ]; then
    local docker_cmd="${GW_DOCKER_CMD:-docker}"
    GW_VERIFY_DB_CONTAINER_ID="$("$docker_cmd" compose "${GW_VERIFY_COMPOSE_ARGS[@]}" ps -q db 2>/dev/null || true)"
    GW_VERIFY_DB_CONTAINER_ID_RESOLVED=1
  fi
}

# verify_db_psql <sql> — a single-column, single-row read-only query against the running db
# service (never a write — every call site below passes a plain `select`). Prints the trimmed
# result on success; returns 1 (nothing printed) when the query itself failed for any reason —
# callers treat that as UNKNOWN, never a hard failure (the T318 "report honestly, never die
# mid-report" lesson, applied here).
#
# B1 (round-2 review): `exec -T db psql ...` with no `-U` lands as the CONTAINER's own default
# exec user — root, on postgres:16.4 (that image sets no USER directive) — and a bare `psql`
# then tries to connect as role "root", which does not exist: `FATAL: role "root" does not
# exist`, on every real box, always. Fixed the same way db/*-migration.sh's own init scripts
# already do it (they run inside this exact container too): read POSTGRES_USER/POSTGRES_DB from
# the CONTAINER's own environment (Postgres's entrypoint always sets both, so this never has to
# guess or hardcode a role name) via `sh -c`, not the exec'd process's caller-supplied identity.
# `$sql` is passed as `sh -c`'s own positional `$1` (the `_` placeholder fills `$0`), never
# interpolated into the `-c` string itself, so a `$sql` containing a shell metacharacter can
# never widen what actually runs.
#
# B5 (round-2 review, defense in depth): every call site in this file passes a literal `select`
# — refuse anything else outright here too, so a future non-select call is impossible, not
# merely untested by the allowlisted-argv fact (Story346_AdoptionVerifyRepair.cs).
#
# F2 (round-3 review): the `^select` check alone is live-reproof bypassable — psql happily runs
# a `;`-separated statement list in one `-tAc`, so `select 1; delete from station.settings` still
# starts with `select ` and sailed straight through (reviewer-proven: printed `1`, then `DELETE
# 0`). A bare `;` anywhere in `$sql` is refused outright now too — every real call site here is a
# single, plain `select ... ` with no reason to ever contain one.
verify_db_psql() {
  local sql="$1" docker_cmd="${GW_DOCKER_CMD:-docker}" out
  [[ "$sql" =~ ^[[:space:]]*[Ss][Ee][Ll][Ee][Cc][Tt][[:space:]] ]] || return 1
  [[ "$sql" == *';'* ]] && return 1
  out="$("$docker_cmd" compose "${GW_VERIFY_COMPOSE_ARGS[@]}" exec -T db \
    sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -tAc "$1"' _ "$sql" 2>/dev/null)" || return 1
  printf '%s' "$out" | tr -d '\r' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//'
}

# verify_env_file_value <key> — B6 fix (round-2 review, a T318 F2 regression): unlike
# preflight_env_value (process-env-wins, correct for the interview/preflight seam contract this
# probe is NOT part of), this probe reports drift found IN ${ENV_FILE} — reading the process
# environment first is a false-both-ways trap on an adopted box: an ambient exported value
# (a caller's own shell, a systemd unit's Environment=) makes a real placeholder in the file
# read as green, and an ambient garbage value over a clean file fabricates drift that was never
# actually written anywhere. Same grep/tail/cut shape as preflight_env_value, minus the env-var
# layer — the file is the only source of truth for what THIS probe reports.
verify_env_file_value() {
  local name="$1"
  [ -f "$ENV_FILE" ] || return 0
  grep -E "^${name}=" "$ENV_FILE" | tail -n1 | cut -d= -f2- || true
}

# verify_env_key_is_needed <key> — B3 fix (round-2 review): true for every .env.example key
# except the handful .env.example itself documents as overlay/profile-gated — PUBLIC_HOST (only
# read under compose.demo.yaml) and TUNNEL_TOKEN (only read once COMPOSE_PROFILES=tunnel is
# active) — and even then only when THIS box's own COMPOSE_FILE/COMPOSE_PROFILES don't actually
# stack that overlay. Reviewer-proven bug this closes: T317's build_env_content deliberately
# writes both COMMENTED (F136.5's split-overlays ruling — the wizard never fabricates
# PUBLIC_HOST), so the old unconditional scope flagged every wizard-written box as "missing"
# and steered a home operator toward the exact public-appliance value that ruling removed.
# Never keys off GW_PRESET (F132.5: adoption mode reads COMPOSE_FILE, this box's own
# last-launched fact, not the interview's).
verify_env_key_is_needed() {
  local key="$1"
  case "$key" in
    PUBLIC_HOST)
      [ "$(verify_demo_from_compose_file)" = "1" ]
      ;;
    TUNNEL_TOKEN)
      case ",$(preflight_env_value COMPOSE_PROFILES)," in
        *,tunnel,*) return 0 ;;
        *)          return 1 ;;
      esac
      ;;
    *)
      return 0
      ;;
  esac
}

# --- probe 1: .env completeness vs .env.example (F137.1) ----------------------------------
# Reports KEY NAMES ONLY — never a value (hard rule: an operator pasting verify output into an
# issue must never leak a secret). Scoped to .env.example's own UNCOMMENTED keys — a commented
# line there (e.g. #STATION_NAME=...) documents an OPTIONAL setting; its absence from a real
# .env is normal, not drift. Advisory only: the six required secrets + ADMIN_PASSWORD already
# have their own hard-fail floor in preflight_env_secrets (F134.1), which this box's own verify
# pass runs first (setup_adoption_mode) — this probe only ever adds NEW information about the
# remaining, non-required keys.
verify_env_completeness() {
  local example=".env.example"
  if [ ! -f "$example" ]; then
    verify_record UNKNOWN ".env completeness" "${example} not found in this checkout — skipped."
    return
  fi

  local -a missing=() placeholder=()
  local key value
  while IFS= read -r key; do
    [ -n "$key" ] || continue
    if grep -qE "^${key}=" "$ENV_FILE" 2>/dev/null; then
      value="$(verify_env_file_value "$key")"
      preflight_is_placeholder "$value" && placeholder+=("$key")
    else
      verify_env_key_is_needed "$key" && missing+=("$key")
    fi
  done < <(grep -oE '^[A-Z_][A-Z0-9_]*=' "$example" | sed 's/=$//')

  if [ "${#missing[@]}" -eq 0 ] && [ "${#placeholder[@]}" -eq 0 ]; then
    verify_record PASS ".env completeness" "Every key ${example} sets by default is present in ${ENV_FILE}"
    return
  fi
  if [ "${#missing[@]}" -gt 0 ]; then
    verify_record WARN ".env completeness" \
      "Missing from ${ENV_FILE} (present in ${example}): ${missing[*]} — key names only; add each with a real value, then re-run"
  fi
  if [ "${#placeholder[@]}" -gt 0 ]; then
    verify_record WARN ".env completeness" \
      "Still holding a change-me* placeholder in ${ENV_FILE}: ${placeholder[*]} — key names only; edit ${ENV_FILE} and set real values"
  fi
}

# --- probe 2: unapplied migrations vs the repo's db/ max (F137.1) -------------------------
# GenWave's migrations record no version/tracking table of their own — every db/NN-*-migration.sh
# is idempotent, always-safe-to-re-run DDL (see migrate.sh's own header: "bash scripts as
# baseline", a real migration runner is future work, gh-#12). The schema itself is therefore the
# only honest record of what has been applied: this probe reads whether the newest migration's
# own artifact exists, via a read-only query through the docker seam. Never claims precision this
# repo's own migration story can't back up — a missing marker WARNs "may be behind", never a
# specific missing migration number.
verify_repo_migration_max() {
  local f base max=0 num
  for f in db/*-migration.sh; do
    [ -f "$f" ] || continue
    base="${f##*/}"   # strip the db/ prefix via bash's own parameter expansion — no `basename`
                       # dependency needed for a single, already-known-shaped path segment.
    num="$(printf '%s' "$base" | grep -oE '^[0-9]+')"
    [ -n "$num" ] || continue
    num=$((10#$num))
    [ "$num" -gt "$max" ] && max="$num"
  done
  printf '%02d' "$max"
}

# verify_derive_migration_marker — B2 fix (round-2 review): the hand-maintained marker constant
# this replaced went silently stale the moment a future migration shipped without updating it —
# reviewer-proven: a scratch db/38 with the marker table already present made the OLD code print
# "schema is current through db/38" and exit 0, even though db/38 itself was never checked at
# all. Derived fresh every run instead: scans every db/*-migration.sh for a
# `create table if not exists <schema>.<table>` line (case-insensitive — this repo spells it both
# ways) and keeps the one from the HIGHEST-numbered file that has at least one match. That file
# both NAMES the artifact this probe checks for AND PROVES the migration number that artifact
# actually backs — a later migration that only ALTERs existing tables (no CREATE TABLE at all,
# e.g. a hypothetical db/38 with only an ADD COLUMN) is invisible to this scan by construction,
# which is exactly the honesty this probe now owes: it can only ever claim precision through a
# migration that demonstrably created something, never fabricate a claim about one that didn't.
# A file with more than one CREATE TABLE (db/37 creates five) contributes its LAST one — an
# arbitrary but stable and sufficient single artifact to stand for "this migration ran".
# Sets GW_VERIFY_MIGRATION_MARKER_NUM/_TABLE; both left empty when db/ has no table-creating
# migration at all (a repo state this codebase has never actually been in — verify_migrations
# below still degrades to an honest UNKNOWN rather than assuming it can't happen).
GW_VERIFY_MIGRATION_MARKER_NUM=""
GW_VERIFY_MIGRATION_MARKER_TABLE=""

verify_derive_migration_marker() {
  GW_VERIFY_MIGRATION_MARKER_NUM=""
  GW_VERIFY_MIGRATION_MARKER_TABLE=""

  local f base num table best_num=-1 best_table=""
  for f in db/*-migration.sh; do
    [ -f "$f" ] || continue
    base="${f##*/}"
    num="$(printf '%s' "$base" | grep -oE '^[0-9]+')"
    [ -n "$num" ] || continue
    num=$((10#$num))

    table="$(grep -ioE 'create[[:space:]]+table[[:space:]]+if[[:space:]]+not[[:space:]]+exists[[:space:]]+[a-z_][a-z0-9_]*\.[a-z_][a-z0-9_]*' "$f" 2>/dev/null \
      | tail -n1 | grep -oE '[a-zA-Z_][a-zA-Z0-9_]*\.[a-zA-Z_][a-zA-Z0-9_]*$' || true)"
    [ -n "$table" ] || continue

    if [ "$num" -gt "$best_num" ]; then
      best_num="$num"
      best_table="${table,,}"
    fi
  done

  if [ "$best_num" -ge 0 ]; then
    GW_VERIFY_MIGRATION_MARKER_NUM="$best_num"
    GW_VERIFY_MIGRATION_MARKER_TABLE="$best_table"
  fi
}

verify_migrations() {
  local repo_max db_cid result
  repo_max="$(verify_repo_migration_max)"

  verify_derive_migration_marker
  if [ -z "$GW_VERIFY_MIGRATION_MARKER_NUM" ]; then
    verify_record UNKNOWN "Schema migrations" \
      "No create-table migration found under db/ to check against — repo's db/ max is db/${repo_max}. Verify manually; ./migrate.sh is always safe to run (idempotent)."
    return
  fi

  local marker_num="$GW_VERIFY_MIGRATION_MARKER_NUM" marker_table="$GW_VERIFY_MIGRATION_MARKER_TABLE"
  local marker_num_fmt
  marker_num_fmt="$(printf '%02d' "$marker_num")"

  if [ "$marker_num" -lt "$((10#$repo_max))" ]; then
    # B2: db/(marker_num+1..repo_max) add no new table this scan can see — never claim
    # "current through db/${repo_max}" on evidence that only reaches db/${marker_num_fmt}.
    #
    # F11 (round-3 review): the unverifiable gap is that whole range, not just repo_max alone —
    # a message naming only the last file implied every file between marker_num and repo_max-1
    # had somehow been accounted for, when none of them were scanned either. Named as a range
    # once the gap spans more than one file; a single-file gap still reads exactly as it always
    # has ("db/NN adds no new table").
    local gap_start gap_start_fmt gap_label gap_verb
    gap_start=$((10#$marker_num + 1))
    gap_start_fmt="$(printf '%02d' "$gap_start")"
    if [ "$gap_start" -eq "$((10#$repo_max))" ]; then
      gap_label="db/${repo_max}"
      gap_verb="adds"
    else
      gap_label="db/${gap_start_fmt}-db/${repo_max}"
      gap_verb="add"
    fi
    verify_record UNKNOWN "Schema migrations" \
      "Can't verify past db/${marker_num_fmt} — ${gap_label} ${gap_verb} no new table (repo's db/ max). Verify manually; ./migrate.sh is always safe to run (idempotent)."
    return
  fi

  verify_resolve_db_container_id
  db_cid="$GW_VERIFY_DB_CONTAINER_ID"
  if [ -z "$db_cid" ]; then
    verify_record UNKNOWN "Schema migrations" \
      "Could not reach the db service to check (not running, or docker unreachable) — repo's db/ max is db/${repo_max}. Start the stack, then re-run; ./migrate.sh is always safe to run (idempotent)."
    return
  fi

  result="$(verify_db_psql "select to_regclass('${marker_table}') is not null")" || result=""
  case "$result" in
    t)
      verify_record PASS "Schema migrations" "Schema is current through db/${marker_num_fmt} (${marker_table} present)"
      ;;
    f)
      verify_add_finding "migrations" "Schema migrations" \
        "Repo's db/ max is db/${marker_num_fmt}, but its marker (${marker_table}) is missing from the schema — this box may be behind." \
        0 \
        ./migrate.sh
      ;;
    *)
      verify_record UNKNOWN "Schema migrations" \
        "Could not determine the applied schema version (query failed) — repo's db/ max is db/${marker_num_fmt}. ./migrate.sh is always safe to run (idempotent)."
      ;;
  esac
}

# --- probe 3: stale locally-built images, gh-#351 (F137.1) --------------------------------
# INFORMATIONAL ONLY — Dean's ruling on gh-#351 (docs/MEMORY.md): "no implicit build, no
# rebuild-if-stale heuristic, no prompt." This never becomes a finding and is never offered to
# verify_run_repair, no matter how old an image is. Mirrors launch.sh's own
# built_services/print_built_image_ages (gh-#351) in shape — reimplemented small and self-
# contained here rather than sourced, the same "this script may not edit launch.sh" boundary
# count_audio_files already accepts for tools/preflight.sh above.
#
# N1 (round-2 review): mirrors launch.sh's OWN USE_PINNED_OVERLAY split too — compose.yaml +
# compose.pinned.yaml still renders a `build:` key for api/icecast (that overlay only resets
# admin_ui/engine/piper's build context, per its own header; api/icecast carry `image:` +
# `pull_policy: always` ALONGSIDE the inherited `build:`), so the dev-flow scan below used to
# misreport a healthy pinned/home* box as "locally built" and print "Run ./build.sh" — advice
# launch.sh's own pinned/home* flow explicitly REJECTS at parse time (BUILD=1 is a hard error
# there) and never prints itself. A pinned box gets launch.sh's OTHER readout instead: the
# published tags actually running, never an age (a tag IS the fact on a pinned box, not a date).
GW_VERIFY_IMAGE_AGE_WARN_SECONDS=3600   # matches launch.sh's own "> 1h behind the newest build"

# verify_pinned_from_compose_file — true once compose.pinned.yaml (SPEC F136.5) is stacked,
# same derivation shape (and the same verify_compose_file_is_stacked basename comparison, T321
# wire finding 1 follow-up) as verify_topology_from_compose_file/verify_demo_from_compose_file.
#
# T321 wire finding 1: also true for a COMPOSE_FILE naming compose.demo.yaml WITHOUT
# compose.pinned.yaml — an old-vintage box (adopted before the F136.5 pins/topology split)
# persisted its own COMPOSE_FILE back when compose.demo.yaml alone carried the published-
# GHCR-image mechanics that live in compose.pinned.yaml today; compose.pinned.yaml did not
# exist yet for that launch to have named it. That box is still a pinned appliance — it has
# never once built an image and never will (the demo overlay's own `image:`/`pull_policy:
# always` directives, unchanged by the split, are still exactly what's running) — so it must
# still get the pinned-tags readout below, never verify_built_image_ages' "Run ./build.sh"
# advice, which launch.sh's own pinned/home* flow rejects outright at parse time (N1, above)
# and a box this shape could never act on anyway (live evidence: T321 run-2 on the Pi 4
# printed that exact advice over a healthy, published-image appliance). A CURRENT-vintage box
# stacking compose.demo.yaml always also stacks compose.pinned.yaml (`--pinned` implies both,
# launch.sh's own USE_PINNED_OVERLAY/PINNED split) — this OR never fires against a genuinely
# locally-built box.
verify_pinned_from_compose_file() {
  verify_compose_file_is_stacked "compose.pinned.yaml" && return 0
  verify_compose_file_is_stacked "compose.demo.yaml" && return 0
  return 1
}

verify_stale_images() {
  local docker_cmd="${GW_DOCKER_CMD:-docker}"
  if verify_pinned_from_compose_file; then
    verify_pinned_image_tags "$docker_cmd"
  else
    verify_built_image_ages "$docker_cmd"
  fi
}

# Pinned-overlay readout (mirrors launch.sh's own print_pinned_image_tags): the published GHCR
# tags this box is actually running. No rebuild hint — a pinned/home* box never builds, and
# ./build.sh would be exactly the wrong advice for it.
verify_pinned_image_tags() {
  local docker_cmd="$1" tags tag joined=""
  tags="$("$docker_cmd" compose "${GW_VERIFY_COMPOSE_ARGS[@]}" config --images 2>/dev/null | grep '^ghcr\.io/genwave-org/' | sort -u)" || tags=""
  if [ -z "$tags" ]; then
    verify_record UNKNOWN "Pinned image tags" "Could not render this box's compose configuration — skipped."
    return
  fi
  while IFS= read -r tag; do
    [ -n "$tag" ] || continue
    if [ -z "$joined" ]; then joined="$tag"; else joined="${joined}, ${tag}"; fi
  done <<< "$tags"
  verify_record INFO "Pinned image tags" "This box is running (gh-#351): ${joined}"
}

# Dev-flow readout (mirrors launch.sh's own built_services/print_built_image_ages).
verify_built_image_ages() {
  local docker_cmd="$1" rendered
  rendered="$("$docker_cmd" compose "${GW_VERIFY_COMPOSE_ARGS[@]}" config 2>/dev/null)" || rendered=""
  if [ -z "$rendered" ]; then
    verify_record UNKNOWN "Built image ages" "Could not render this box's compose configuration — skipped."
    return
  fi

  # F9 (round-3 review): `mapfile -t` (not an unquoted `built=($(...))`) — same word-splitting/
  # globbing hazard verify_resolve_env_facts and verify_compose_overrides already guard against
  # (round-2 review N6) — a service name that happened to contain a shell glob character would
  # otherwise expand against whatever is in the current directory.
  local -a built=()
  mapfile -t built < <(printf '%s\n' "$rendered" | awk '
    /^services:/                 { in_services = 1; next }
    in_services && /^[^ ]/       { in_services = 0 }
    in_services && /^  [^ ]/     { svc = $1; sub(/:.*$/, "", svc); next }
    in_services && /^    build:/ { print svc }
  ')

  if [ "${#built[@]}" -eq 0 ]; then
    verify_record INFO "Built image ages" "No locally-built services in this box's compose config — every image here is pulled/pinned."
    return
  fi

  local now_epoch newest_epoch=0 svc cid image_id created created_epoch
  now_epoch="$(date +%s)"
  local -a names=() epochs=()
  for svc in "${built[@]}"; do
    cid="$("$docker_cmd" compose "${GW_VERIFY_COMPOSE_ARGS[@]}" ps -a -q "$svc" 2>/dev/null | head -n1)"
    created="" created_epoch=""
    if [ -n "$cid" ]; then
      image_id="$("$docker_cmd" inspect "$cid" --format '{{.Image}}' 2>/dev/null || true)"
      if [ -n "$image_id" ]; then
        created="$("$docker_cmd" image inspect "$image_id" --format '{{.Created}}' 2>/dev/null || true)"
      fi
    fi
    [ -n "$created" ] && created_epoch="$(date -d "$created" +%s 2>/dev/null || true)"
    [ -n "$created_epoch" ] || continue
    names+=("$svc")
    epochs+=("$created_epoch")
    [ "$created_epoch" -gt "$newest_epoch" ] && newest_epoch="$created_epoch"
  done

  if [ "${#names[@]}" -eq 0 ]; then
    verify_record INFO "Built image ages" "Locally-built services found, but none have a running/created container yet."
    return
  fi

  local i part joined="" age flag
  local -a parts=()
  for i in "${!names[@]}"; do
    age=$(( (now_epoch - epochs[i]) / 3600 ))
    flag=""
    [ $(( newest_epoch - epochs[i] )) -gt "$GW_VERIFY_IMAGE_AGE_WARN_SECONDS" ] && flag=" (older than the newest build)"
    parts+=("${names[$i]} ~${age}h${flag}")
  done
  joined="${parts[0]}"
  for part in "${parts[@]:1}"; do joined="${joined}, ${part}"; done
  verify_record INFO "Built image ages" \
    "Informational only (gh-#351 — never auto-rebuilt): ${joined}. Run ./build.sh or BUILD=1 ./launch.sh to refresh."
}

# --- probe 4: orphaned profile containers (F137.1) -----------------------------------------
# The piper/kokoro leftover gotcha: a container for a service that is DEFINED but no longer
# profile-selected survives `docker compose ... up --remove-orphans` (that flag's own "orphan"
# is narrower than "not currently selected by profile" — see launch.sh's own comment on it) and
# is invisible to compose's own profile-aware `ps`. Found instead via a raw, project-labeled
# `docker ps` (the Engine API has no notion of "profiles" at all, so a raw list is never
# profile-filtered) against the EXPECTED set this box's own resolved compose config selects
# right now (`docker compose config --services`, itself profile-aware) — never a hardcoded
# service list on either side (the T316/T318 review lesson).
#
# N6 (round-2 review): checks GW_VERIFY_COMPOSE_ARGS itself (already derived, once, by
# verify_resolve_env_facts) rather than re-reading COMPOSE_FILE a second time here — one source,
# not two independent readers of the same fact that could drift apart.
verify_orphaned_containers() {
  local docker_cmd="${GW_DOCKER_CMD:-docker}"
  if [ "${#GW_VERIFY_COMPOSE_ARGS[@]}" -eq 0 ]; then
    verify_record UNKNOWN "Orphaned containers" "No COMPOSE_FILE recorded yet (this box has never completed a launch) — nothing to check."
    return
  fi

  # N6 (round-2 review): considered folding this and the `--format json` call below into ONE
  # `config --format json` render (it answers services/build/project all at once) — declined:
  # this repo has no `jq` dependency anywhere, and reliably pulling a services LIST plus a build-
  # key CHECK plus the project name back out of nested JSON with pure bash/grep/sed would be far
  # uglier and more fragile than two purpose-built, single-value calls. `--services` and
  # `--format json` stay separate on purpose.
  local expected
  expected="$("$docker_cmd" compose "${GW_VERIFY_COMPOSE_ARGS[@]}" config --services 2>/dev/null)" || expected=""
  if [ -z "$expected" ]; then
    verify_record UNKNOWN "Orphaned containers" "Could not render this box's compose configuration — skipped."
    return
  fi

  local project
  project="$("$docker_cmd" compose "${GW_VERIFY_COMPOSE_ARGS[@]}" config --format json 2>/dev/null \
    | grep -o '"name"[[:space:]]*:[[:space:]]*"[^"]*"' | head -n1 | sed -E 's/.*"([^"]*)"[[:space:]]*$/\1/')"
  if [ -z "$project" ]; then
    verify_record UNKNOWN "Orphaned containers" "Could not determine the compose project name — skipped."
    return
  fi

  # N3 (round-2 review): this lists `ps -a` — every container regardless of state, running or
  # long-exited — but used to WARN "is running" and print the stop/restart caution unconditionally
  # for all of them. `{{.State}}` names what was actually observed, and the restart-class caution
  # (F137.3) now fires only when the leftover genuinely IS running; an already-exited one gets
  # `rm -f` offered with no false "this will stop a running container" caution attached to it.
  local svc name state found=0 restarts
  while IFS='|' read -r svc name state; do
    [ -n "$svc" ] || continue
    case $'\n'"${expected}"$'\n' in
      *$'\n'"${svc}"$'\n'*) continue ;;   # still selected under this box's own profiles — fine
    esac
    found=1
    restarts=0
    [ "$state" = "running" ] && restarts=1
    verify_add_finding "orphan:${name}" "Orphaned container (${svc})" \
      "'${name}' (state: ${state}) is not part of this box's currently selected profiles/topology — the piper/kokoro leftover gotcha ('up --remove-orphans' does not catch a profile-gated-off service's own leftover container)." \
      "$restarts" \
      "$docker_cmd" rm -f "$name"
  done < <("$docker_cmd" ps -a --filter "label=com.docker.compose.project=${project}" \
    --format '{{.Label "com.docker.compose.service"}}|{{.Names}}|{{.State}}' 2>/dev/null)

  if [ "$found" = "0" ]; then
    verify_record PASS "Orphaned containers" "None found for project '${project}'"
  fi
}

# --- probe 5: disk-prune advice (F137.1) ----------------------------------------------------
# Read-only (`docker system df`, never `docker system prune`/`docker image prune` — this probe
# never runs either, in verify OR repair mode).
#
# B4 (round-2 review): INFO-ONLY, by design, never a finding — F137.1 itself calls this "advice",
# never a fix. Any box that has ever upgraded has SOME reclaimable space (a superseded image from
# the previous pin, still referenced by nothing) the instant a newer one lands; the OLD code
# turned that into a WARN/exit-5, so a perfectly healthy, just-upgraded box could never verify
# green — F137.4's own do-no-harm gate (and T321's Pi 4 wire, which expects exit 0 on a healthy
# box) both depend on this NOT happening. It also could never have been offered as a scripted
# repair honestly: the trigger read UNFILTERED `system df` reclaimable while the old repair
# command pruned only `until=168h`-old images — young reclaimable space (a same-day pin bump)
# would trip the finding but the repair would then prune nothing, an unclearable loop. The exact
# prune command still prints, for the operator to run by hand when THEY judge it worth it — see
# N4 (round-2 review) for why its own message spells out the blast radius rather than implying
# it's GenWave-scoped.
GW_VERIFY_PRUNE_UNTIL_HOURS=168   # matches launch.sh's own gh-#441 prune filter

verify_prune_advice() {
  local docker_cmd="${GW_DOCKER_CMD:-docker}" df_out reclaimable
  df_out="$("$docker_cmd" system df 2>/dev/null)" || df_out=""
  if [ -z "$df_out" ]; then
    verify_record UNKNOWN "Disk (docker images)" "Could not read 'docker system df' — skipped."
    return
  fi

  reclaimable="$(printf '%s\n' "$df_out" | awk '$1=="Images" {print $5}')"
  if [ -z "$reclaimable" ]; then
    verify_record UNKNOWN "Disk (docker images)" "Could not parse 'docker system df' output — skipped."
    return
  fi

  if [ "$reclaimable" = "0B" ]; then
    verify_record PASS "Disk (docker images)" "0B reclaimable — nothing to prune"
    return
  fi

  verify_record INFO "Disk (docker images)" \
    "${reclaimable} reclaimable from superseded/dangling images (docker system df) — advice, not a finding (F137.1); this is normal after any upgrade. Prune by hand if you want it back: ${docker_cmd} image prune -af --filter until=${GW_VERIFY_PRUNE_UNTIL_HOURS}h — removes every unused image on this machine older than 7 days, not just GenWave's, including rollback targets you may still want."
}

# --- deliberate divergences: INFO only, never a finding (F137.2/AC3) -----------------------

# A DB-stored settings override: StationSettingsAllowlist.All documents Station:Name as the
# canonical example (env STATION_NAME only catches up the Icecast stream name on the next
# ENGINE restart; the api-side value is whatever's live in station.settings, an operator's own
# Admin UI edit). Any row here for that key IS by design the currently-winning value — never
# drift, never a finding.
verify_db_settings_overrides() {
  local db_cid value
  verify_resolve_db_container_id
  db_cid="$GW_VERIFY_DB_CONTAINER_ID"
  [ -n "$db_cid" ] || return 0   # can't check — silently skipped (optional/informational only,
                                  # distinct from verify_migrations' own required UNKNOWN row)
  value="$(verify_db_psql "select value from station.settings where key = 'Station:Name'")" || return 0
  [ -n "$value" ] || return 0
  value="${value#\"}"
  value="${value%\"}"
  verify_record INFO "Station name" \
    "The database has an operator-set override ('${value}', via the Admin UI) — this wins over any STATION_NAME in ${ENV_FILE} for the running station; not drift, never offered as a fix."
}

# An operator COMPOSE_FILE customization: any file COMPOSE_FILE names beyond this repo's own
# shipped compose*.yaml set (derived by listing the checkout itself, never a hardcoded filename
# array) is a deliberate operator overlay — gh-#309's own documented mechanism for exactly this.
verify_compose_overrides() {
  [ "${#GW_VERIFY_COMPOSE_ARGS[@]}" -eq 0 ] && return 0

  local -a shipped=()
  local f
  for f in compose*.yaml; do
    [ -f "$f" ] || continue
    shipped+=("$f")
  done

  # F7 (round-3 review): GW_VERIFY_COMPOSE_ARGS itself — already derived once by
  # verify_resolve_env_facts (N6's own discipline) — is the ONE source now, rather than this
  # function independently re-splitting GW_VERIFY_COMPOSE_FILE_VALUE a second time (the same
  # IFS=':' `read -ra` it already cites as the reason not to do that).
  #
  # T321 wire finding 2: scans for the element FOLLOWING each literal `-f` token, rather than
  # assuming every odd index is a filename — GW_VERIFY_COMPOSE_ARGS now also carries a leading
  # `--env-file "$ENV_FILE"` pair ahead of the `-f` pairs (verify_resolve_env_facts, above),
  # which the old fixed odd/even parity would have misread the env-file's own PATH as an
  # "unknown compose file" on every adopted box. Correct only on the invariant
  # verify_resolve_env_facts itself guarantees — every `-f` token in this array is followed by
  # its own file argument, never a trailing/dangling `-f`; not a claim about any other flag
  # shape this array might one day carry.
  local -a files=()
  local i
  for ((i = 0; i < ${#GW_VERIFY_COMPOSE_ARGS[@]}; i++)); do
    [ "${GW_VERIFY_COMPOSE_ARGS[$i]}" = "-f" ] || continue
    files+=("${GW_VERIFY_COMPOSE_ARGS[$((i + 1))]}")
  done
  local -a extra=()
  local s known
  for f in "${files[@]}"; do
    known=0
    for s in "${shipped[@]}"; do
      [ "$f" = "$s" ] && { known=1; break; }
    done
    [ "$known" = "0" ] && extra+=("$f")
  done

  if [ "${#extra[@]}" -gt 0 ]; then
    verify_record INFO "Compose file overrides" \
      "COMPOSE_FILE in ${ENV_FILE} names a file this repo doesn't ship (${extra[*]}) — a deliberate operator customization; not drift, never offered as a fix."
  fi
}

# --- repair: per-item confirm, --yes for bulk (F137.2/F137.3) -------------------------------
# GW_VERIFY_REPAIR_REMAINING is a global rather than this function's own stdout — every item's
# progress line (the exact command, the restart warning, the prompt, "Done."/"Skipped.") has to
# reach the operator's terminal directly; capturing this function via $(...) to get a return
# value would swallow all of that into the captured string instead.
GW_VERIFY_REPAIR_REMAINING=0

verify_run_repair() {
  GW_VERIFY_REPAIR_REMAINING=0
  local i
  for i in "${!GW_VERIFY_FINDING_ID[@]}"; do
    local label="${GW_VERIFY_FINDING_LABEL[$i]}" message="${GW_VERIFY_FINDING_MESSAGE[$i]}"
    local restarts="${GW_VERIFY_FINDING_RESTARTS[$i]}"
    # The nameref IS the single source both this display line and the execution below read —
    # they can never diverge (see verify_add_finding's own remarks on why this array exists).
    local -n cmdref="GW_VERIFY_FINDING_CMD_${i}"

    echo
    echo "-- ${label}"
    echo "   ${message}"
    printf '   Fix: %s\n' "${cmdref[*]}"
    if [ "$restarts" = "1" ]; then
      echo "   ⚠️  this will stop/restart a running container — printed before the confirm, never a surprise (F137.3)."
    fi

    local proceed="$SETUP_YES"
    if [ "$proceed" != "1" ]; then
      printf '   Apply this fix? [y/N]: '
      local answer
      if IFS= read -r answer; then
        case "$answer" in
          [Yy]*) proceed=1 ;;
          *)     proceed=0 ;;
        esac
      else
        # N2 (round-2 review): EOF here is NOT the interview's own abandonment signal — an
        # ssh/cron-piped --repair with a shorter answer stream than there are findings hits this
        # constantly, and `prompt`'s own EOF handling (exit 1, "Nothing was written") would be
        # false the moment an EARLIER item in this same run was already applied. Treat it as a
        # decline for THIS item only: never applied, counted toward the remaining total, and the
        # loop continues (every SUBSEQUENT read also hits EOF and also declines) — the run still
        # ends via the ordinary "N finding(s) still outstanding" exit 5 below, never a crash.
        echo
        proceed=0
      fi
    fi

    if [ "$proceed" = "1" ]; then
      if "${cmdref[@]}"; then
        echo "   Done."
      else
        echo "   FAILED — left as-is; re-run ./setup.sh --repair to retry." >&2
        GW_VERIFY_REPAIR_REMAINING=$((GW_VERIFY_REPAIR_REMAINING + 1))
      fi
    else
      echo "   Skipped."
      GW_VERIFY_REPAIR_REMAINING=$((GW_VERIFY_REPAIR_REMAINING + 1))
    fi
  done
}

# setup_adoption_mode — an existing checkout/stack (a .env is already at ENV_FILE) never re-runs
# the interview; this is VERIFY (SETUP_REPAIR=0) or VERIFY-then-REPAIR (SETUP_REPAIR=1), never
# both, and VERIFY never writes/starts/stops/pulls/prunes anything (F137.4's do-no-harm gate) —
# only a confirmed repair item, run from verify_run_repair above, ever mutates the box.
setup_adoption_mode() {
  # "a .env already exists here", not "already configured" (T317 review LOW finding) — this
  # mode's own probes are what actually establishes whether the install is correct/complete;
  # this line only ever reports the virgin-vs-existing signal (F132.4) that routed here.
  echo "==> A .env already exists here (${ENV_FILE})."
  if [ "$SETUP_REPAIR" = "1" ]; then
    echo "    Repairing (SPEC F137, STORY-346) — verifying first; nothing changes until you confirm."
  else
    echo "    Verifying (SPEC F137, STORY-346) — read-only; nothing here is changed."
  fi

  # N6 (round-2 review): COMPOSE_FILE read exactly once here, ahead of everything else that
  # derives from it — GW_PREFLIGHT_TOPOLOGY/GW_PREFLIGHT_DEMO below, GW_VERIFY_COMPOSE_ARGS, and
  # every probe's own topology/demo/override check, all now read the one cached copy.
  verify_resolve_env_facts

  export GW_PREFLIGHT_TOPOLOGY GW_PREFLIGHT_DEMO
  GW_PREFLIGHT_TOPOLOGY="$(verify_topology_from_compose_file)"
  GW_PREFLIGHT_DEMO="$(verify_demo_from_compose_file)"
  preflight_docker
  preflight_env_secrets

  verify_env_completeness
  verify_migrations
  verify_stale_images
  verify_orphaned_containers
  verify_prune_advice
  verify_db_settings_overrides
  verify_compose_overrides

  verify_print_report

  local has_finding=0 unknown_count=0 status
  for status in "${GW_VERIFY_ROW_STATUS[@]}"; do
    [ "$status" = "WARN" ] && has_finding=1
    [ "$status" = "UNKNOWN" ] && unknown_count=$((unknown_count + 1))
  done

  if [ "$SETUP_REPAIR" != "1" ]; then
    if [ "$has_finding" = "1" ]; then
      echo
      echo "==> Drift found — nothing was changed. Re-run: ./setup.sh --repair"
      exit 5
    fi
    echo
    # F5 (round-3 review): UNKNOWN is not drift (still exit 0 — a probe that couldn't be
    # verified is not the same claim as one that failed), but "no drift found" alone overclaims
    # when several probes never actually got to look (e.g. the daemon-up-containers-down case,
    # where most of adoption mode's own probes degrade to UNKNOWN) — say so instead of implying
    # a clean sweep.
    if [ "$unknown_count" -gt 0 ]; then
      echo "==> Green — no drift found (${unknown_count} check(s) could not be verified), nothing to do."
    else
      echo "==> Green — no drift found, nothing to do."
    fi
    exit 0
  fi

  if [ "$GW_VERIFY_FINDING_COUNT" -eq 0 ]; then
    echo
    echo "==> Nothing here is auto-repairable — see the report above for anything advisory."
    exit 0
  fi

  echo
  echo "==> Repairing — per-item confirm (--yes=${SETUP_YES})"
  verify_run_repair
  if [ "$GW_VERIFY_REPAIR_REMAINING" -gt 0 ]; then
    echo
    echo "==> ${GW_VERIFY_REPAIR_REMAINING} finding(s) still outstanding — re-run ./setup.sh --repair to retry, or ./setup.sh to verify."
    exit 5
  fi

  echo
  echo "==> Repaired — re-run ./setup.sh to verify green."
  exit 0
}

print_ready_to_launch() {
  echo
  echo "==> .env written to ${ENV_FILE} (GW_PRESET=${GW_PRESET})"
  echo "==> Ready to launch — GW_PRESET already selects the ${GW_PRESET} shape, no flags needed"
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
#
# Finding 5 (post-v5.3.0 gate run): SKIP_PREFLIGHT=1 forced on THIS call only — main() has
# already run preflight_docker + preflight_env_secrets against this exact machine and this
# exact .env moments earlier (nothing observable changes in the gap; print_ready_to_launch does
# no I/O). Without this, the real launch.sh (which sources tools/preflight.sh itself) redundantly
# re-checks the identical machine state and renders its OWN preflight summary table as this
# subprocess exits — a genuine duplicate render Dean's transcript caught landing BEFORE the
# on-air line, with this script's own (single, now explicitly-placed — see main()) table still
# to come after the handoff. An env-var prefix, not an argv flag, so this still honors "invoked
# BARE" above.
invoke_launch() {
  SKIP_PREFLIGHT=1 "${GW_LAUNCH_CMD:-./launch.sh}"
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
    echo "==> Can't verify on-air automatically (curl not found on this machine) — check manually: curl -I ${url}" >&2
    return 2
  fi

  echo "==> Waiting for ${url} to serve audio (a first run's image pulls can take a few minutes)..."

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
# address `hostname -I` reports), or empty if unavailable. This function's own mechanism is
# UNCHANGED by finding 2 (gate-run round 2, see print_handoff's own header): it is still the
# always-works second line every URL block prints — the plain hostname (primary_hostname,
# below) leads now, but a hostname can fail to resolve for some OTHER device on the LAN even
# when the command itself succeeds on this box (no mDNS/LLMNR reflected on that network, a
# router that doesn't forward it), so the numeric address stays the one guaranteed-reachable
# fallback (CLAUDE.md: "a small private community").
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

# primary_hostname — finding 2 (gate-run round 2): Dean's own ruling — this wizard is usually
# read over SSH, from a machine that is NOT this box, so a URL that says "localhost" there
# quietly means the READER's own laptop, not the station (the exact confusion Dean hit). The
# plain hostname (never the FQDN) resolves for other devices on the same LAN via the home
# router's own mDNS/LLMNR, and it's what Dean asked for. Total function, same contract as
# primary_lan_address above: a missing/empty `hostname`, or the literal string "localhost" (a
# candidate that would defeat the whole point of this function), degrades to "skip this line" —
# never a crash, never a lie.
#
# N1 (gate-run round 2 review): a `hostname` command that EXISTS but misbehaves also has to
# degrade the same way, mirroring primary_lan_address's own round-4 F2 guard, which this
# function originally lacked — a `hostname` writing a USAGE message to stdout instead of failing
# cleanly used to flow straight into the lead URL (repro: candidate "usage: hostname [-v]",
# handoff prints `http://usage: hostname [-v]:8000/stream`), and so did bare whitespace (repro:
# candidate "   ", handoff prints `http://   :3000/`). Only a hostname-SHAPED token — letters,
# digits, hyphens, dots, no whitespace or other punctuation — is ever returned; anything else
# (garbage, empty, "localhost") degrades to "skip this line", and the caller's own contract
# (print_url_block/primary_url) already falls through to the LAN line from there.
primary_hostname() {
  command -v hostname >/dev/null 2>&1 || return 0
  local candidate
  candidate="$(hostname 2>/dev/null)" || true
  if [[ "$candidate" =~ ^[A-Za-z0-9.-]+$ ]] && [ "$candidate" != "localhost" ]; then
    printf '%s' "$candidate"
  fi
  return 0
}

# format_url <host> <port> <path> — the ONE place the IPv6-bracket logic lives (round-4 review
# N2: an IPv6 literal dropped straight into a URL without brackets is ambiguous with the port's
# own colon). PR #586 extracted print_lan_line for exactly this reason (two call sites had
# started drifting apart); finding 2 (gate-run round 2 review, N3) re-grew a THIRD copy across
# print_lan_line/print_url_block/primary_url — extracted one level further, to a primitive that
# builds the bare "http://host:port/path" string (no label, no trailing annotation), so the
# bracket case can never drift again no matter how many call sites need a URL.
format_url() {
  local host="$1" port="$2" path="$3"
  case "$host" in
    *:*) printf 'http://[%s]:%s%s' "$host" "$port" "$path" ;;
    *)   printf 'http://%s:%s%s' "$host" "$port" "$path" ;;
  esac
}

# print_lan_line <lan_addr> <port> <path> — the "(from other devices on your network)" line,
# shared by every URL block (finding 1, post-v5.3.0 gate run).
print_lan_line() {
  local addr="$1" port="$2" path="$3"
  echo "                   $(format_url "$addr" "$port" "$path")  (from other devices on your network)"
}

# print_url_block <label> <host_name> <lan_addr> <port> <path> — finding 2 (gate-run round 2):
# hostname leads (if it resolved), the LAN-IP is the always-works second line (unchanged
# print_lan_line mechanism), and localhost is a LAST-RESORT single line — printed only when
# NEITHER of the other two resolved, never as the lead (that is the whole finding). Column
# alignment (14-wide label) matches the pre-existing literal spacing print_lan_line's own
# 19-space continuation indent was built against.
print_url_block() {
  local label="$1" host="$2" lan="$3" port="$4" path="$5" led=0
  if [ -n "$host" ]; then
    printf '    %-14s %s\n' "$label" "$(format_url "$host" "$port" "$path")"
    led=1
  fi
  if [ -n "$lan" ]; then
    if [ "$led" = "1" ]; then
      print_lan_line "$lan" "$port" "$path"
    else
      printf '    %-14s %s\n' "$label" "$(format_url "$lan" "$port" "$path")"
    fi
    led=1
  fi
  # `if`, not `[ ... ] && printf ...` — under `set -e` a bare `&&` chain that ends up FALSE
  # (the common case: led=1, nothing left to print) returns the function's own exit status as
  # non-zero, which a caller invoking this as a plain statement (not itself inside an `if`)
  # would trip errexit on — the exact footgun round-4 review F2/B1 already found in
  # primary_lan_address, repeated here if this were left as a one-liner.
  if [ "$led" = "0" ]; then
    printf '    %-14s %s\n' "$label" "http://localhost:${port}${path}"
  fi
}

# primary_url <host_name> <lan_addr> <port> <path> — the single best URL for a one-line message
# (the poll-timeout diagnostics, the Hire-a-DJ deep link): same hostname-then-LAN-then-
# localhost-last-resort priority as print_url_block, collapsed to one line for call sites that
# don't want the full two-line block (the admin block right above Hire-a-DJ already showed both
# addresses; repeating them for a plain deep link would be clutter).
primary_url() {
  local host="$1" lan="$2" port="$3" path="$4"
  if [ -n "$host" ]; then
    format_url "$host" "$port" "$path"
  elif [ -n "$lan" ]; then
    format_url "$lan" "$port" "$path"
  else
    printf 'http://localhost:%s%s' "$port" "$path"
  fi
}

# print_handoff <launch_exit> — F132.8: the once-only screen with everything the owner needs to
# actually use the station. ADMIN_PASSWORD (T318 review BLOCKING finding F2) is read straight
# from SECRET_ADMIN_UI — the value THIS run generated (apply_generate_secrets) and wrote to
# ENV_FILE — never read back via preflight_env_value: that reader's process-env-wins precedence
# means an ambient ADMIN_PASSWORD exported in the caller's own shell would print instead of the
# one this run actually generated and wrote. Never written to SETUP_LOG_FILE or anywhere else.
#
# The stream block (finding 1, post-v5.3.0 gate run): Dean's own vmtest report — the handoff
# named the admin URL/password/persona link but never the single most important URL a radio
# station has, and an all-'n' interview (admin declined) printed no URL at all. Always printed,
# independent of ADMIN_PROFILE, first (it's the one thing every run has, admin or not) — same
# hostname + LAN treatment as the admin URL block below (finding 2, gate-run round 2 — see this
# function's own header two paragraphs up), plus one honest cloud-firewall line: this exact
# confusion (wizard said on-air, VLC timed out on Hetzner's firewall) cost the gate run an hour.
print_handoff() {
  local launch_exit="$1" lan_addr host_name

  if [ "$launch_exit" = "4" ]; then
    echo
    echo "==> DEGRADED-BUT-AIRING: the core is broadcasting, but launch.sh's catch-up stage"
    echo "    ($(catchup_services_desc)) did not fully converge on the first try."
    echo "    Catch up any time: ./launch.sh"
  fi

  echo
  echo "==> You're on the air — here's everything you need:"
  echo

  # round-4 review N2's shape check admits IPv6 tokens as well as IPv4 — resolved once here,
  # shared by the stream block below and the admin block further down (print_lan_line,
  # print_url_block). host_name likewise resolved once (primary_hostname) — finding 2.
  lan_addr="$(primary_lan_address)"
  host_name="$(primary_hostname)"

  print_url_block "Stream" "$host_name" "$lan_addr" "$GW_STREAM_PORT_DEFAULT" "/stream"
  echo "                   Listening from another machine (e.g. a cloud VM)? Port ${GW_STREAM_PORT_DEFAULT}"
  echo "                   must be reachable — check your host/cloud firewall if playback times out."

  echo
  if [ -n "$ADMIN_PROFILE" ]; then
    print_url_block "Admin UI" "$host_name" "$lan_addr" 3000 "/"
    echo "    Password       ${SECRET_ADMIN_UI}   (shown once — it's also in ${ENV_FILE}; change it there any time)"
    echo "    Hire a DJ      $(primary_url "$host_name" "$lan_addr" 3000 "/persona-catalog")"
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
  echo "      ./setup.sh     re-run any time — verifies this install (read-only); add --repair to fix"
  echo "                     drift it finds (SPEC F137, STORY-346)"
}

main() {
  local arg
  for arg in "$@"; do
    case "$arg" in
      --repair) SETUP_REPAIR=1 ;;
      --yes)    SETUP_YES=1 ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        echo "setup.sh: unknown argument: $arg" >&2
        echo "  ./setup.sh                 first-run interview, or verify an existing install (read-only)" >&2
        echo "  ./setup.sh --repair [--yes] fix drift verify finds — per-item confirm, or --yes for all" >&2
        exit 2
        ;;
    esac
  done

  if [ -f "$ENV_FILE" ]; then
    setup_adoption_mode
  fi

  # N7 (round-2 review): a virgin box with `--repair` used to silently fall through to the
  # interview with no acknowledgment that the flag itself did nothing — --repair is adoption
  # mode's own surface (F137), meaningless on the virgin path (this file's own header already
  # documents that "no --repair-only validation gate" is deliberate). One honest line, then the
  # ordinary interview, rather than a flag an operator passed out of habit vanishing silently.
  if [ "$SETUP_REPAIR" = "1" ]; then
    echo "==> No install here yet (${ENV_FILE} not found) — --repair has nothing to fix; running first-run setup instead."
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
  echo "==> Checking the machine and the .env just written"
  # Handed to preflight as the caller-resolved explicit input (F134.3a) — preflight itself
  # reads no preset/topology key. Both values come from resolve_preset_and_topology's own
  # output (above), the same source GW_PRESET itself was just written from — never a second,
  # independently-hardcoded read of the interview's answer (the T316 one-source lesson).
  export GW_PREFLIGHT_TOPOLOGY="$GW_PREFLIGHT_TOPOLOGY_VALUE"
  export GW_PREFLIGHT_DEMO="$GW_PREFLIGHT_DEMO_VALUE"
  preflight_docker
  preflight_env_secrets
  # Finding 5 (post-v5.3.0 gate run): rendered explicitly HERE, right after the checks that
  # populate it, so the operator reads it before "On air" — where it belongs — rather than
  # after the handoff's own "Next runs" block. setup_exit_trap (near the top of this file) still
  # calls preflight_print_report unconditionally on every exit path (an early preflight_fail
  # never reaches this line at all and still needs its own render); the idempotency guard inside
  # preflight_print_report itself (tools/preflight.sh) is what keeps that a harmless no-op here
  # rather than a second render of the same table.
  preflight_print_report

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
      # Finding 1 follow-up (post-v5.3.0 gate run): this path already names the admin URL and
      # points at the secrets file — it must name the stream URL too, for the same reason and
      # just as consistently (the poll gave up, but the stream itself may still be there).
      # Finding 2 (gate-run round 2): hostname-first here too — same primary_url priority
      # print_handoff's own blocks use (its header has the full Dean-ruling rationale).
      local host_name lan_addr
      host_name="$(primary_hostname)"
      lan_addr="$(primary_lan_address)"
      echo "  Check the stream directly: $(primary_url "$host_name" "$lan_addr" "$GW_STREAM_PORT_DEFAULT" "/stream")" >&2
      echo "  Your secrets (including the Admin UI password) are in ${ENV_FILE}." >&2
      if [ -n "$ADMIN_PROFILE" ]; then
        echo "  Admin UI (once you've confirmed the stream): $(primary_url "$host_name" "$lan_addr" 3000 "/")" >&2
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

main "$@"
