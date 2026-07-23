#!/usr/bin/env bash
# build.sh — build everything: C# libraries + apps, run tests, build Docker images.
#
# Phase-aware: steps whose inputs don't exist yet are skipped with a notice, so this is
# safe to run at any point in the implementation (PRD §13). As src/ and the api image land
# in later phases, the corresponding steps activate automatically.
#
# Env overrides:
#   CONFIG=Debug ./build.sh     # default Release
#   SKIP_TESTS=1 ./build.sh     # build but don't run tests
#   SKIP_PREFLIGHT=1 ./build.sh # bypass machine preflight checks (gh-#19 escape hatch)
set -euo pipefail
cd "$(dirname "$0")"

SLN="GenWave.sln"
CONFIG="${CONFIG:-Release}"

# --- 0. preflight (gh-#19): fail with guidance BEFORE any tool is invoked ---------------
# The docker compose build below renders compose.yaml, whose ${VAR:?} secrets fail the run
# from three tools deep with no advice — so the machine and .env are checked up front.
. tools/preflight.sh
[ -f "$SLN" ] && preflight_dotnet_sdk 10
preflight_docker
preflight_env_secrets

# Tag-derived version stamp (SPEC F65.1, STORY-175): never committed to source, no csproj
# <Version> — derived once here from git and threaded into the api image's InformationalVersion.
GW_VERSION="$(git describe --tags --always --dirty 2>/dev/null || echo 0.0.0-dev)"

echo "==> GenWave build (config: ${CONFIG}, version: ${GW_VERSION})"

# --- 1. .NET solution: libraries + apps, then tests -------------------------------------
if [ -f "$SLN" ]; then
  echo "==> dotnet build ${SLN}"
  dotnet build "$SLN" -c "$CONFIG" --nologo

  if [ "${SKIP_TESTS:-0}" = "1" ]; then
    echo "==> (skip) tests disabled via SKIP_TESTS=1"
  else
    echo "==> dotnet test"
    dotnet test "$SLN" -c "$CONFIG" --no-build --nologo
  fi
else
  echo "==> (skip) no ${SLN} yet — C# solution arrives in Phase 2"
fi

# --- 2. Docker images -------------------------------------------------------------------
if [ -f src/GenWave.Host/Dockerfile ]; then
  echo "==> docker compose build (icecast, engine, admin_ui)"
  docker compose build icecast engine admin_ui

  # api takes GW_VERSION via --build-arg (compose.yaml stays untouched — no build.args: entry;
  # the Dockerfile's ARG GW_VERSION=0.0.0-dev default means a plain `docker compose build` still
  # works without this flag).
  echo "==> docker compose build api (GW_VERSION=${GW_VERSION})"
  docker compose build --build-arg GW_VERSION="${GW_VERSION}" api
else
  echo "==> docker compose build icecast (api image arrives in Phase 6)"
  docker compose build icecast
fi

echo "==> build complete"
