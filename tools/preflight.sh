#!/usr/bin/env bash
# tools/preflight.sh — shared preflight checks for build.sh / launch.sh (gh-#19).
#
# The scripts are the only supported way to build and launch the stack, so they check the
# machine BEFORE touching anything, and every failure exit says how to proceed — never a
# bare stack trace from three tools deep. Sourced, not executed; callers rely on their own
# `set -euo pipefail`.
#
# Contract:
#   * every check either passes silently or calls preflight_fail (exit 3) with concrete
#     next steps on stderr;
#   * SKIP_PREFLIGHT=1 bypasses every check (documented escape hatch for unusual setups);
#   * GW_ENV_FILE overrides which env file preflight_env_secrets reads (default .env) —
#     exists for the script test suite; compose itself always reads .env.
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
    elif printf '%s' "$value" | grep -q "^change-me"; then
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
}
