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
set -euo pipefail
cd "$(dirname "$0")"

SLN="GenWave.sln"
CONFIG="${CONFIG:-Release}"

echo "==> GenWave build (config: ${CONFIG})"

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
  echo "==> docker compose build (all services)"
  docker compose build
else
  echo "==> docker compose build icecast (api image arrives in Phase 6)"
  docker compose build icecast
fi

echo "==> build complete"
