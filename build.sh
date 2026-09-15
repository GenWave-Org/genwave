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
#   SKIP_PREFLIGHT=1 ./build.sh # bypass preflight_docker_build's checks (gh-#19 escape hatch).
#                                 STORY-437 (gh-#775): build.sh now runs only the BUILD subset
#                                 (docker + compose plugin + version floor), so this flag
#                                 bypasses only those. It also never reaches the
#                                 dotnet test run below, which un-sets it for the child process
#                                 so the Host suite's own preflight specs see a clean environment.
set -euo pipefail
cd "$(dirname "$0")"

SLN="GenWave.sln"
CONFIG="${CONFIG:-Release}"

# --- 0. preflight (gh-#19): fail with guidance BEFORE any tool is invoked ---------------
# build.sh only needs Docker + the compose plugin to RENDER images — no .env, no music
# library, no free ports (STORY-437, gh-#775): preflight_docker_build is the compile-time
# subset of preflight_docker's launch-grade checks. compose.yaml's ${VAR:?} secrets still
# need SOME value to render at all, so step 2 below exports a placeholder for any this
# process doesn't already have — a real value from the shell or .env always wins.
. tools/preflight.sh
[ -f "$SLN" ] && preflight_dotnet_sdk 10
preflight_docker_build

# Tag-derived version stamp (SPEC F65.1, STORY-175): never committed to source, no csproj
# <Version> — derived once here from git and threaded into the api image's InformationalVersion.
GW_VERSION="$(git describe --tags --always --dirty 2>/dev/null || echo 0.0.0-dev)"

echo "==> GenWave build (config: ${CONFIG}, version: ${GW_VERSION})"

# --- 1. .NET solution: libraries + apps, then tests -------------------------------------
if [ -f "$SLN" ]; then
  echo "==> dotnet build ${SLN}"
  dotnet build "$SLN" -c "$CONFIG" --nologo

  if [ "${SKIP_TESTS:-0}" = "1" ]; then
    echo "==> (skip) Tests disabled via SKIP_TESTS=1"
  else
    echo "==> dotnet test"
    # -u SKIP_PREFLIGHT -u SKIP_TESTS: this process may be running under either flag (see the
    # header comment) but the Host suite spun up here contains the preflight specs themselves
    # (Gh019, Story342, this very Story437) — they must see their OWN unset-by-default
    # environment, not build.sh's, or a build.sh escape hatch silently masks the thing it tests.
    env -u SKIP_PREFLIGHT -u SKIP_TESTS dotnet test "$SLN" -c "$CONFIG" --no-build --nologo
  fi
else
  echo "==> (skip) No ${SLN} yet — C# solution arrives in Phase 2"
fi

# --- 2. Docker images -------------------------------------------------------------------
# compose.yaml's ${VAR:?} secrets fail a render three tools deep with no .env in sight — a
# build only needs the file to RENDER, not real secrets, so anything still unset (checked
# via preflight_env_value: process env first, else .env's last assignment) gets a placeholder
# exported into THIS process only — never written to .env, never visible to the caller's
# shell, and never chosen over a real value either place already has.
for name in "${GW_REQUIRED_ENV_VARS[@]}"; do
  [ -n "$(preflight_env_value "$name")" ] && continue
  # MEDIA_DIR is a bind-mount SOURCE (${MEDIA_DIR:?}:/media:ro): compose reads a bare word
  # there as a named-volume reference, not a path, and refuses the project — so its
  # placeholder needs a leading slash; the five password vars are plain env values.
  case "$name" in
    MEDIA_DIR) export "$name=/build-only" ;;
    *)         export "$name=build-only" ;;
  esac
done

if [ -f src/GenWave.Host/Dockerfile ]; then
  echo "==> docker compose build (icecast, engine)"
  docker compose build icecast engine

  # api and admin_ui take GW_VERSION via --build-arg (compose.yaml stays untouched — no
  # build.args: entry; both Dockerfiles' ARG GW_VERSION=0.0.0-dev default means a plain
  # `docker compose build` still works without this flag). The api stamps it into
  # InformationalVersion (SPEC F65.1); the admin_ui inlines it into the version footer (gh-#7).
  echo "==> docker compose build api admin_ui (GW_VERSION=${GW_VERSION})"
  docker compose build --build-arg GW_VERSION="${GW_VERSION}" api admin_ui
else
  echo "==> docker compose build icecast (api image arrives in Phase 6)"
  docker compose build icecast
fi

echo "==> Build complete"
