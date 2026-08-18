#!/usr/bin/env bash
# setup.sh — the first-run wizard: four questions, generated secrets, a ready-to-launch .env
# (SPEC F132.1-.6, STORY-344).
#
# Lives at repo root, peer of launch.sh — wraps it, never re-implements compose orchestration
# (this script never calls `docker compose` directly; T318/STORY-345 wires the actual launch
# invocation, the clock, and the handoff screen). Plain bash, plain numbered prompts, no
# whiptail/dialog dependency.
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
#   stdin             — the interview's answer channel: a caller pipes newline-terminated
#                       answers in, one per prompt.
# The .NET SDK probe (Q1) needs no seam of its own: like build.sh's own check, it is just
# `dotnet` present-or-absent on PATH (Gh019's idiom) — a scratch PATH with no dotnet stub
# already proves the pinned-only branch.
#
# Sources tools/preflight.sh for preflight_env_value (routing/topology reads) and its two
# hard-fail entry points, preflight_docker + preflight_env_secrets — run AFTER the .env write,
# before this script declares itself done, so a machine or .env problem is caught before the
# printed next command is trusted (a failure here still leaves the just-written .env in place;
# only the machine, not the file, is in question). Actually launching the stack, timing first
# audio, and the handoff screen are STORY-345/T318's build — this script's own EXPLICIT last
# word is "ready to launch" plus the exact command to run next (print_ready_to_launch), but
# NOT the literal last thing printed to the terminal: tools/preflight.sh's own EXIT trap
# (F134.6's pass/warn summary table) still fires after that, on any exit path — see the trap
# setup right below the source line for why this script chains onto it rather than replacing
# it (T317 review LOW finding).
set -euo pipefail
cd "$(dirname "$0")"

. tools/preflight.sh

ENV_FILE="${GW_ENV_FILE:-.env}"
SECRET_LENGTH=40   # comfortably over F132.3's >=32-char floor

# --- EXIT trap: chain onto preflight's own (T317 review MEDIUM finding: stranded temp
# secrets) -----------------------------------------------------------------------------------
# tools/preflight.sh (sourced above) already registered `trap preflight_print_report EXIT` —
# bash keeps exactly one EXIT trap, so replacing it outright (a bare `trap ... EXIT` here)
# would silently drop that summary table (that file's own header CAUTIONs exactly this
# footgun). This registers ONE trap that does both jobs, in order: clean up any still-live
# `.env.setup.*` temp write (SETUP_TMP_ENV_FILE — set by apply_env_write only for the window
# between mktemp and mv, so a signal or a hard failure mid-write never leaves a secret-laden
# stray file on disk), THEN print whatever preflight recorded.
SETUP_TMP_ENV_FILE=""
setup_exit_trap() {
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
  echo "==> ready to launch"
  echo
  echo "    Next: ./launch.sh"
  echo "    (GW_PRESET already selects the ${GW_PRESET} shape — no flags needed; pass"
  echo "     --pinned/--piper-only explicitly any time to override it for one run.)"
}

main() {
  if [ -f "$ENV_FILE" ]; then
    setup_adoption_mode
  fi

  echo "GenWave setup — four quick questions, then you're on air."

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
}

main
