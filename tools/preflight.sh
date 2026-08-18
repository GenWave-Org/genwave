#!/usr/bin/env bash
# tools/preflight.sh — shared preflight checks for build.sh / launch.sh (gh-#19, SPEC F134).
#
# The scripts are the only supported way to build and launch the stack, so they check the
# machine BEFORE touching anything, and every failure exit says how to proceed — never a
# bare stack trace from three tools deep. Sourced, not executed; callers rely on their own
# `set -euo pipefail`.
#
# Contract:
#   * a HARD-FAIL check either passes silently or calls preflight_fail (exit 3) with concrete
#     next steps on stderr — same as always;
#   * a non-blocking check calls preflight_record (PASS/WARN) instead — it never stops the
#     run. Every recorded row prints once, in one summary table, via an EXIT trap (F134.6) —
#     so a clean run still surfaces its cautions, and a hard-failing one still surfaces
#     whatever was recorded before the failure.
#   * SKIP_PREFLIGHT=1 bypasses every check, hard-fail and recorded alike (documented escape
#     hatch for unusual setups);
#   * GW_ENV_FILE overrides which env file preflight_env_secrets (and every check that reads
#     an env value) reads (default .env) — exists for the script test suite; compose itself
#     always reads .env.
#
# Topology awareness (F134.3/F134.4) is a CALLER CONTRACT, not something this script derives
# itself. SPEC F132.5 (amended 2026-08-18) makes `launch.sh` the ONLY reader of GW_PRESET in
# the whole repo — it alone resolves flags + preset into concrete topology. This script takes
# that resolution as two explicit inputs instead of reading GW_PRESET (or any preset) itself:
#   * GW_PREFLIGHT_TOPOLOGY — `full` or `piper-only` (default `full` when unset) — selects the
#     disk-headroom constant.
#   * GW_PREFLIGHT_DEMO     — `0` or `1` (default `0`) — `1` adds the demo overlay's 80/443 to
#     the port check.
#   * COMPOSE_PROFILES — unchanged, the existing compose mechanism; an "admin" entry still adds
#     port 3000 to the port check (rides the same env-value convention as MEDIA_DIR below:
#     process env wins, else the last assignment in GW_ENV_FILE).
# launch.sh wires GW_PREFLIGHT_TOPOLOGY/GW_PREFLIGHT_DEMO at T316; until then both default to
# the base stack, which is the honest state of the world today.
#
# Test seams (preflight-only; each defaults to the real path/command; a probe tool missing
# from PATH degrades its check to a WARN naming what went unchecked, it never hard-fails):
#   * GW_CMDLINE_FILE         — Pi kernel cmdline (default /boot/firmware/cmdline.txt)
#   * GW_MEMINFO_FILE         — RAM source (default /proc/meminfo)
#   * GW_MOUNTS_FILE          — mount table for the MEDIA_DIR NFS check (default /proc/mounts)
#   * GW_SS_CMD               — port-probe command name (default ss)
#   * GW_DF_CMD               — disk-probe command name (default df)
#   * GW_FIND_CMD             — MEDIA_DIR audio-file walk command name (default find)
#   * GW_DOCKER_ROOT_FALLBACK — conventional Docker storage-root fallback (default /var/lib/docker),
#                               used when `docker info` can't be parsed — see preflight_docker_root_dir
#
# shellcheck shell=bash

# ---- failure helper ---------------------------------------------------------------------
# preflight_fail "<what is wrong>" "<how to proceed line>" [more lines...]
preflight_fail() {
  local problem="$1"
  shift
  {
    echo "preflight: ✗ ${problem}"
    echo "  How to proceed:"
    local line
    for line in "$@"; do
      echo "    - ${line}"
    done
  } >&2
  exit 3
}

preflight_enabled() {
  [ "${SKIP_PREFLIGHT:-0}" != "1" ]
}

# ---- pass/warn summary table (F134.6) ----------------------------------------------------
# One row per non-blocking check, rendered once at process exit. A hard-fail check never reaches
# here (preflight_fail exits before its caller can record anything) — the two stay separate so
# the pinned hard-fail message format (gh-#19) never changes shape.
#
# Three parallel arrays, not pipe-delimited strings — a message containing "|" would otherwise
# corrupt the split back apart.
GW_PREFLIGHT_ROW_STATUS=()
GW_PREFLIGHT_ROW_LABEL=()
GW_PREFLIGHT_ROW_MESSAGE=()

# preflight_record PASS|WARN "<label>" "<message, how-to-proceed baked in for WARN>"
preflight_record() {
  GW_PREFLIGHT_ROW_STATUS+=("$1")
  GW_PREFLIGHT_ROW_LABEL+=("$2")
  GW_PREFLIGHT_ROW_MESSAGE+=("$3")
}

preflight_print_report() {
  [ "${#GW_PREFLIGHT_ROW_STATUS[@]}" -gt 0 ] || return 0
  echo
  echo "==> preflight summary"
  local i status label message symbol
  for i in "${!GW_PREFLIGHT_ROW_STATUS[@]}"; do
    status="${GW_PREFLIGHT_ROW_STATUS[$i]}"
    label="${GW_PREFLIGHT_ROW_LABEL[$i]}"
    message="${GW_PREFLIGHT_ROW_MESSAGE[$i]}"
    case "$status" in
      PASS) symbol="✓" ;;
      *)    symbol="△" ;;
    esac
    printf '    %s %-5s %-22s %s\n' "$symbol" "$status" "$label" "$message"
  done
}

# Rendered on an EXIT trap, not an explicit call at the end of preflight_env_secrets — a caller
# that only runs preflight_docker (build.sh has no secrets to check), or that hard-fails partway
# through preflight_env_secrets, still gets every row recorded up to that point printed before
# the process actually exits. Registered once, here, at source time — fires no matter which
# function (or preflight_fail's `exit 3`, left byte-identical) ends the process.
# CAUTION: a caller that registers its own `trap ... EXIT` AFTER sourcing this file replaces this
# one outright (bash keeps a single EXIT trap) — that caller must invoke preflight_print_report
# itself, or the summary table never prints.
trap preflight_print_report EXIT

# ---- docker -----------------------------------------------------------------------------
preflight_docker() {
  preflight_enabled || return 0

  if ! command -v docker >/dev/null 2>&1; then
    preflight_fail "Docker is not installed (docker not found in PATH)." \
      "Install Docker Engine: https://docs.docker.com/engine/install/" \
      "Then re-run this script."
  fi

  local info_err
  if ! info_err="$(docker info 2>&1 >/dev/null)"; then
    if printf '%s' "$info_err" | grep -qi "permission denied"; then
      preflight_fail "Docker is installed but this user cannot talk to the daemon (permission denied)." \
        "Add yourself to the docker group: sudo usermod -aG docker \$USER" \
        "Log out and back in (or run: newgrp docker), then re-run this script."
    fi
    preflight_fail "Docker is installed but the daemon is not running." \
      "Start it: sudo systemctl start docker   (on desktop: start Docker Desktop)" \
      "Check it: docker info" \
      "Then re-run this script."
  fi

  if ! docker compose version >/dev/null 2>&1; then
    preflight_fail "The Docker Compose plugin is missing (docker compose does not work)." \
      "Install the compose plugin: https://docs.docker.com/compose/install/linux/" \
      "Check it: docker compose version" \
      "Then re-run this script."
  fi

  preflight_compose_version
  preflight_ports
  preflight_resources
}

# ---- compose version floor (F134.2) ------------------------------------------------------
# v2.24 is the floor the demo overlay's `!override`/`!reset` merge tags need (HARDWARE.md's
# compatibility table). Unparseable output (a stub, an unexpected banner) WARNs instead of
# failing — a version we can't read is not proof it's too old.
GW_PREFLIGHT_COMPOSE_MIN_MAJOR=2
GW_PREFLIGHT_COMPOSE_MIN_MINOR=24

preflight_compose_version() {
  local raw major minor
  raw="$(docker compose version 2>/dev/null || true)"

  # Anchored on the literal "version" token — an unanchored v?([0-9]+)\.([0-9]+) would happily
  # false-pass on a stray leading number anywhere else in unexpected output.
  if [[ "$raw" =~ version[[:space:]]+v?([0-9]+)\.([0-9]+) ]]; then
    major="${BASH_REMATCH[1]}"
    minor="${BASH_REMATCH[2]}"
  else
    preflight_record WARN "Compose version" \
      "Could not determine it from 'docker compose version' — verify manually it is >= v${GW_PREFLIGHT_COMPOSE_MIN_MAJOR}.${GW_PREFLIGHT_COMPOSE_MIN_MINOR}."
    return 0
  fi

  if [ "$major" -gt "$GW_PREFLIGHT_COMPOSE_MIN_MAJOR" ] || \
     { [ "$major" -eq "$GW_PREFLIGHT_COMPOSE_MIN_MAJOR" ] && [ "$minor" -ge "$GW_PREFLIGHT_COMPOSE_MIN_MINOR" ]; }; then
    preflight_record PASS "Compose version" "v${major}.${minor}.x"
    return 0
  fi

  preflight_fail "Docker Compose v${major}.${minor} is older than the v${GW_PREFLIGHT_COMPOSE_MIN_MAJOR}.${GW_PREFLIGHT_COMPOSE_MIN_MINOR} floor this stack requires (the demo overlay's override/reset merge tags depend on it)." \
    "Upgrade the Compose plugin: https://docs.docker.com/compose/install/linux/" \
    "Check it: docker compose version" \
    "Then re-run this script."
}

# ---- port availability (F134.3) -----------------------------------------------------------
# Base ports are always published (icecast + api, see compose.yaml); 3000 (admin_ui) only
# when the "admin" profile is active; 80/443 (caddy) only under the demo overlay
# (GW_PREFLIGHT_DEMO — see the header's caller-contract note; this script never reads GW_PRESET).
GW_PREFLIGHT_BASE_PORTS=(8000 8080 8081)
GW_PREFLIGHT_ADMIN_PORT=3000
GW_PREFLIGHT_DEMO_PORTS=(80 443)

# ---- port ownership (F134.3b) -------------------------------------------------------------
# `./launch.sh` IS the restart command and `--pinned` IS the upgrade path — a port already held
# by THIS stack's own containers (icecast/api/admin_ui on a broadcasting box) is a PASS, not a
# conflict. Precise ownership would mean replicating compose's project-name resolution
# (COMPOSE_PROJECT_NAME override, else the working directory's basename) purely to filter
# `docker ps --filter label=com.docker.compose.project=<name>` — fragile exactly where it
# matters most: get the project name wrong (this runs before COMPOSE_FILE resolution in some
# flows) and a real restart trips right back into the bug this fix exists to close. Chosen
# instead: a flat union of every container Docker has published a port for, project-agnostic.
# Any docker-published port is, by definition, not an unmanaged foreign process fighting this
# stack for a socket — it's something Docker itself already accounts for, and `docker compose
# up` reusing/replacing that same container on restart is exactly the case that must sail
# through. `docker ps` unreachable here is rare (preflight_docker already proved the daemon
# responds earlier in the same run) but degrades the affected ports to a WARN rather than a
# silent PASS or a false hard-fail.
preflight_docker_published_ports() {
  docker ps --format '{{.Ports}}' 2>/dev/null
}

# preflight_port_is_docker_owned <port> <docker-ps-ports-blob>
preflight_port_is_docker_owned() {
  printf '%s' "$2" | grep -qE "[:]${1}->"
}

preflight_ports() {
  local profiles ports=("${GW_PREFLIGHT_BASE_PORTS[@]}")
  profiles="$(preflight_env_value COMPOSE_PROFILES)"
  case ",${profiles}," in
    *,admin,*) ports+=("$GW_PREFLIGHT_ADMIN_PORT") ;;
  esac
  if [ "${GW_PREFLIGHT_DEMO:-0}" = "1" ]; then
    ports+=("${GW_PREFLIGHT_DEMO_PORTS[@]}")
  fi

  local ss_cmd="${GW_SS_CMD:-ss}"
  if ! command -v "$ss_cmd" >/dev/null 2>&1; then
    preflight_record WARN "Ports" \
      "Not checked (${ss_cmd} not found) — verify these are free before launching: ${ports[*]}."
    return 0
  fi

  local docker_ports docker_ok=1
  docker_ports="$(preflight_docker_published_ports)" || docker_ok=0

  local ss_out port line owner unverified=()
  ss_out="$("$ss_cmd" -ltnp 2>/dev/null || true)"
  for port in "${ports[@]}"; do
    line="$(printf '%s\n' "$ss_out" | grep -E ":${port}[[:space:]]" | head -n1 || true)"
    [ -n "$line" ] || continue

    if [ "$docker_ok" -eq 0 ]; then
      unverified+=("$port")
      continue
    fi
    preflight_port_is_docker_owned "$port" "$docker_ports" && continue

    owner="$(printf '%s' "$line" | grep -oE 'users:\(\("[^"]+"' | sed -E 's/users:\(\("//; s/"$//' || true)"
    [ -n "$owner" ] || owner="an unidentified process (re-run ${ss_cmd} -ltnp with sudo to name it)"

    preflight_fail "Port ${port} is already in use by ${owner} — this stack needs it free before launching." \
      "Find it: sudo ${ss_cmd} -ltnp | grep ':${port} '" \
      "Stop that process or the conflicting service, then re-run this script."
  done

  if [ "${#unverified[@]}" -gt 0 ]; then
    preflight_record WARN "Ports" \
      "${unverified[*]} appear bound but 'docker ps' could not be read to confirm a Docker container publishes them — verify manually before launching."
  else
    preflight_record PASS "Ports" "${ports[*]} free (or published by a Docker container)"
  fi
}

# ---- resource checks (F134.4) -------------------------------------------------------------
# Constants mirror SPEC F134.4's documented figures — named here so a future re-measure is a
# one-line change, not a text hunt.
GW_PREFLIGHT_DISK_MIN_GIB_FULL=12
GW_PREFLIGHT_DISK_MIN_GIB_PIPER_ONLY=4
GW_PREFLIGHT_RAM_MIN_GIB=6

preflight_resources() {
  preflight_disk_headroom
  preflight_ram_headroom
  preflight_pi_cgroup_memory
}

# Docker writes images/volumes under its own storage root, not necessarily the filesystem this
# repo checkout lives on — measuring "." can report headroom on the wrong disk entirely. Falls
# back to the conventional /var/lib/docker (GW_DOCKER_ROOT_FALLBACK) if `docker info` can't be
# parsed, then to "." (with a WARN naming the approximation) if that doesn't exist either.
#
# Assigns GW_PREFLIGHT_DISK_TARGET rather than returning the value on stdout: a caller reading it
# back via `target="$(preflight_docker_root_dir)"` runs this function in a subshell, so the
# preflight_record WARN on the fallback path would append to that subshell's copy of the row
# arrays — discarded the instant the subshell exits, leaving the WARN unrenderable and the
# operator looking at an authoritative PASS measured against the wrong filesystem. Calling it
# directly (not inside $(...)) keeps preflight_record on the same process as every other check.
GW_PREFLIGHT_DISK_TARGET=""

preflight_docker_root_dir() {
  local root
  root="$(docker info --format '{{.DockerRootDir}}' 2>/dev/null || true)"
  if [ -n "$root" ] && [ -d "$root" ]; then
    GW_PREFLIGHT_DISK_TARGET="$root"
    return 0
  fi

  local fallback="${GW_DOCKER_ROOT_FALLBACK:-/var/lib/docker}"
  if [ -d "$fallback" ]; then
    GW_PREFLIGHT_DISK_TARGET="$fallback"
    return 0
  fi

  preflight_record WARN "Disk headroom target" \
    "Could not determine Docker's storage root — measuring the current directory's filesystem instead, which may not be where Docker actually writes images/volumes."
  GW_PREFLIGHT_DISK_TARGET="."
}

preflight_disk_headroom() {
  local df_cmd="${GW_DF_CMD:-df}"
  if ! command -v "$df_cmd" >/dev/null 2>&1; then
    preflight_record WARN "Disk headroom" "Not checked (${df_cmd} not found) — verify manually before launching."
    return 0
  fi

  # Direct call, not $(...) — see preflight_docker_root_dir's comment for why a subshell here
  # would swallow its own fallback WARN.
  preflight_docker_root_dir
  local target="$GW_PREFLIGHT_DISK_TARGET"

  local avail_kib
  avail_kib="$("$df_cmd" -Pk "$target" 2>/dev/null | awk 'NR==2 {print $4}' || true)"
  if ! [[ "$avail_kib" =~ ^[0-9]+$ ]]; then
    preflight_record WARN "Disk headroom" "Could not parse '${df_cmd} -Pk ${target}' output — verify manually before launching."
    return 0
  fi

  local topology="full stack" min_gib="$GW_PREFLIGHT_DISK_MIN_GIB_FULL"
  if [ "${GW_PREFLIGHT_TOPOLOGY:-full}" = "piper-only" ]; then
    topology="piper-only"
    min_gib="$GW_PREFLIGHT_DISK_MIN_GIB_PIPER_ONLY"
  fi

  local avail_gib=$(( avail_kib / 1024 / 1024 ))
  if [ "$avail_gib" -lt "$min_gib" ]; then
    preflight_record WARN "Disk headroom" \
      "${avail_gib} GiB free, below the ~${min_gib} GiB ${topology} guideline — free up space (or pick a lighter topology) before launching."
  else
    preflight_record PASS "Disk headroom" "${avail_gib} GiB free (>= ~${min_gib} GiB ${topology} guideline)"
  fi
}

preflight_ram_headroom() {
  local meminfo="${GW_MEMINFO_FILE:-/proc/meminfo}"
  if [ ! -r "$meminfo" ]; then
    preflight_record WARN "RAM" "Could not read ${meminfo} — verify manually before launching."
    return 0
  fi

  local kib
  kib="$(grep -m1 '^MemTotal:' "$meminfo" 2>/dev/null | grep -oE '[0-9]+' || true)"
  if [ -z "$kib" ]; then
    preflight_record WARN "RAM" "Could not parse MemTotal from ${meminfo} — verify manually before launching."
    return 0
  fi

  local gib=$(( kib / 1024 / 1024 ))
  if [ "$gib" -lt "$GW_PREFLIGHT_RAM_MIN_GIB" ]; then
    preflight_record WARN "RAM" \
      "${gib} GiB total, under ~${GW_PREFLIGHT_RAM_MIN_GIB} GiB — the piper-only topology (no kokoro/ollama) fits this box better than the full stack."
  else
    preflight_record PASS "RAM" "${gib} GiB total"
  fi
}

# Pi kernels ship the memory cgroup disabled — every `mem_limit` in compose.yaml is silently
# discarded until cgroup_enable=memory is added to the boot cmdline (HARDWARE.md, gh-#307).
# A missing cmdline file just means this isn't that kind of boot layout — nothing to check,
# nothing recorded (keeps the summary table free of an N/A row on every non-Pi box).
preflight_pi_cgroup_memory() {
  local cmdline="${GW_CMDLINE_FILE:-/boot/firmware/cmdline.txt}"
  [ -f "$cmdline" ] || return 0

  if grep -q 'cgroup_enable=memory' "$cmdline" 2>/dev/null; then
    preflight_record PASS "Pi memory cgroup" "cgroup_enable=memory present in ${cmdline}"
  else
    preflight_record WARN "Pi memory cgroup" \
      "cgroup_enable=memory is missing from ${cmdline} — every mem_limit is silently discarded until you add it and reboot. See HARDWARE.md's 'Enable the memory cgroup' section."
  fi
}

# ---- .NET SDK ---------------------------------------------------------------------------
# preflight_dotnet_sdk <major> — the SDK that builds GenWave.sln.
preflight_dotnet_sdk() {
  preflight_enabled || return 0
  local major="$1"

  if ! command -v dotnet >/dev/null 2>&1; then
    preflight_fail ".NET SDK is not installed (dotnet not found in PATH)." \
      "Install the .NET ${major} SDK: https://dotnet.microsoft.com/download/dotnet/${major}.0" \
      "Then re-run this script."
  fi

  if ! dotnet --list-sdks 2>/dev/null | grep -q "^${major}\."; then
    preflight_fail ".NET SDK ${major}.x is required but not installed (found: $(dotnet --list-sdks 2>/dev/null | cut -d' ' -f1 | paste -sd, - || echo none))." \
      "Install the .NET ${major} SDK: https://dotnet.microsoft.com/download/dotnet/${major}.0" \
      "Check it: dotnet --list-sdks" \
      "Then re-run this script."
  fi
}

# ---- .env secrets -----------------------------------------------------------------------
# The compose file fails loudly on unset ${VAR:?} secrets — but only after teardown has
# already begun. Checking here keeps "config missing" strictly BEFORE "stack touched".
# Required list mirrors compose.yaml's `${VAR:?}` interpolations exactly.
GW_REQUIRED_ENV_VARS=(
  POSTGRES_PASSWORD
  LIBRARY_DB_PASSWORD
  STATION_DB_PASSWORD
  ICECAST_SOURCE_PASSWORD
  ICECAST_ADMIN_PASSWORD
  MEDIA_DIR
)

# Effective value of $1: process environment wins, else the env file's last assignment.
preflight_env_value() {
  local name="$1" env_file="${GW_ENV_FILE:-.env}"
  if [ -n "${!name:-}" ]; then
    printf '%s' "${!name}"
    return 0
  fi
  [ -f "$env_file" ] || return 0
  # `|| true` — an absent assignment is an empty value, not a pipefail under the caller's set -e.
  grep -E "^${name}=" "$env_file" | tail -n1 | cut -d= -f2- || true
}

# A surviving `.env.example` placeholder — shared by the six required-secret loop below and by
# ADMIN_PASSWORD's own posture check (F134.1), so the "what counts as a placeholder" rule lives
# in exactly one place.
preflight_is_placeholder() {
  printf '%s' "$1" | grep -q '^change-me'
}

preflight_env_secrets() {
  preflight_enabled || return 0
  local env_file="${GW_ENV_FILE:-.env}"

  if [ ! -f "$env_file" ]; then
    preflight_fail "No ${env_file} file found — the stack's secrets are not configured." \
      "Create one from the template: cp .env.example .env" \
      "Edit .env: set every change-me-* value and point MEDIA_DIR at your music library." \
      "Then re-run this script."
  fi

  local name value missing=() placeholder=()
  for name in "${GW_REQUIRED_ENV_VARS[@]}"; do
    value="$(preflight_env_value "$name")"
    if [ -z "$value" ]; then
      missing+=("$name")
    elif preflight_is_placeholder "$value"; then
      placeholder+=("$name")
    fi
  done

  if [ "${#missing[@]}" -gt 0 ]; then
    preflight_fail "Required settings are missing from ${env_file}: ${missing[*]}" \
      "Compare against the template: diff .env.example ${env_file}" \
      "Set each missing value in ${env_file}, then re-run this script."
  fi

  if [ "${#placeholder[@]}" -gt 0 ]; then
    preflight_fail "These ${env_file} settings still hold their change-me placeholder: ${placeholder[*]}" \
      "Edit ${env_file} and set real values (any long random string works for passwords)." \
      "Then re-run this script."
  fi

  local media_dir
  media_dir="$(preflight_env_value MEDIA_DIR)"
  if [ ! -d "$media_dir" ]; then
    preflight_fail "MEDIA_DIR (${media_dir}) does not exist or is not a directory." \
      "Point MEDIA_DIR in ${env_file} at the absolute path of your music library." \
      "Then re-run this script."
  fi

  preflight_admin_password
  preflight_media_deep "$media_dir"
  # No explicit preflight_print_report call — the EXIT trap registered above renders it exactly
  # once no matter which function (or preflight_fail) ends the process.
}

# ---- ADMIN_PASSWORD posture (F134.1) ------------------------------------------------------
# Unlike the six required secrets above, ADMIN_PASSWORD is legitimately optional: compose.yaml
# defaults it to empty (`${ADMIN_PASSWORD:-}`), and empty is the documented headless/appliance
# posture (DEPLOYMENT.md: "empty = admin locked entirely, fail-closed") — so empty WARNs
# instead of failing. A surviving change-me* placeholder is never intentional, so that still
# hard-fails exactly like the six required vars above.
preflight_admin_password() {
  local env_file="${GW_ENV_FILE:-.env}" value
  value="$(preflight_env_value ADMIN_PASSWORD)"

  if preflight_is_placeholder "$value"; then
    preflight_fail "ADMIN_PASSWORD in ${env_file} still holds its change-me placeholder." \
      "Edit ${env_file} and set a real ADMIN_PASSWORD (any long random string), or leave it empty to keep the admin UI locked." \
      "Then re-run this script."
  fi

  if [ -z "$value" ]; then
    preflight_record WARN "ADMIN_PASSWORD" \
      "Empty — the admin UI stays locked entirely (fail-closed); this is the documented appliance posture. Set one in ${env_file} to enable admin sign-in."
  else
    preflight_record PASS "ADMIN_PASSWORD" "Set (value not shown)"
  fi
}

# ---- MEDIA_DIR deep checks (F134.5) --------------------------------------------------------
# GenWave.MediaLibrary's ScanService lowercases each file's extension before matching
# LibraryOptions.SupportedExtensions — a case-INSENSITIVE compare. This probe mirrors that
# exactly with `find -iname`, so an uppercase Track01.FLAC counts here the same as it counts to
# the scanner. Anything else, e.g. WAVs, is invisible to the scanner regardless of what's here
# (the T314 smoke finding).
GW_PREFLIGHT_AUDIO_EXTENSIONS=(flac mp3)

preflight_media_deep() {
  local dir="$1"

  if [ ! -r "$dir" ] || [ ! -x "$dir" ]; then
    preflight_fail "MEDIA_DIR (${dir}) exists but is not readable by this user." \
      "Fix its permissions so it can be read (and listed): chmod a+rX \"${dir}\"" \
      "Then re-run this script."
  fi

  local find_cmd="${GW_FIND_CMD:-find}"
  if ! command -v "$find_cmd" >/dev/null 2>&1; then
    preflight_record WARN "MEDIA_DIR audio files" \
      "Count unchecked (${find_cmd} not found) — verify manually that ${dir} has .flac/.mp3 files before launching."
  else
    # One walk, not one per extension: an -iname alternation built from
    # GW_PREFLIGHT_AUDIO_EXTENSIONS, so the source-of-truth list stays a single array. F134.5
    # only needs zero-vs-nonzero plus a display count, not a second full stat of a 9k-file tree.
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
      count=$(( count + 1 ))
    done < <("$find_cmd" "$dir" -type f \( "${find_expr[@]}" \) 2>/dev/null)

    if [ "$count" -eq 0 ]; then
      preflight_record WARN "MEDIA_DIR audio files" \
        "0 .flac/.mp3 files under ${dir} (case-insensitive match) — the stack will start silent (no-music route). Seed it with your library, or see DEPLOYMENT.md for CC-licensed sources, then re-run."
    else
      preflight_record PASS "MEDIA_DIR audio files" "${count} .flac/.mp3 files found under ${dir} (case-insensitive match)"
    fi
  fi

  preflight_media_nfs_notes "$dir"
}

# Longest-prefix match against the mount table: MEDIA_DIR itself may be the mount point, or
# sit somewhere under one. NFS mounts get the two gotchas that bite an operator by surprise —
# a stale inode after the export is recreated (restart api+engine to pick it back up) and a
# case-sensitive server backing a client that assumes otherwise.
preflight_media_nfs_notes() {
  local dir="$1" mounts="${GW_MOUNTS_FILE:-/proc/mounts}"
  [ -r "$mounts" ] || return 0

  local mnt_dev mp fs rest best_mp="" best_fs=""
  while read -r mnt_dev mp fs rest; do
    case "$dir" in
      "$mp"|"$mp"/*)
        if [ "${#mp}" -ge "${#best_mp}" ]; then
          best_mp="$mp"
          best_fs="$fs"
        fi
        ;;
    esac
  done < "$mounts"

  case "$best_fs" in
    nfs|nfs4)
      preflight_record WARN "MEDIA_DIR filesystem" \
        "NFS-mounted (${best_mp}, ${best_fs}). Two gotchas: a stale inode after the export is recreated (restart api+engine to pick it back up), and case-sensitivity differences from the NFS server."
      ;;
    *)
      preflight_record PASS "MEDIA_DIR filesystem" "local (${best_fs:-unknown})"
      ;;
  esac
}
