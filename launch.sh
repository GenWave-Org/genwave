#!/usr/bin/env bash
# launch.sh — tear the Docker stack down and bring it back up.
#
# Building is build.sh's job; this script just (re)launches. Compose will still build any
# missing image for a service that has a build: context, so a first launch works too.
#
# Single-station stack. Everything is published on localhost — no proxy, no FQDNs.
#
# Env overrides:
#   BUILD=1 ./launch.sh     # force a rebuild on the way up
set -euo pipefail
cd "$(dirname "$0")"

UP_ARGS=(-d)
[ "${BUILD:-0}" = "1" ] && UP_ARGS+=(--build)

echo "==> tearing down stack"
docker compose down --remove-orphans

echo "==> bringing the database up first"
docker compose up "${UP_ARGS[@]}" db

# The persistent pgdata volume only runs db/01-library.sh on a FRESH volume; an existing
# volume never picks up schema added since. The db/*-migration.sh scripts are idempotent
# in-place upgrades (ADD COLUMN IF NOT EXISTS), so applying them on every launch is safe and
# keeps the schema converged BEFORE the api (which queries the new columns) starts — otherwise
# the api crash-loops on a missing column and the stream falls back to the safe loop.
db_cid="$(docker compose ps -q db)"
for _ in $(seq 1 30); do
  if [ "$(docker inspect "$db_cid" --format '{{.State.Health.Status}}' 2>/dev/null)" = "healthy" ]; then break; fi
  sleep 2
done
# The migration loop itself now lives in ./migrate.sh (also usable standalone against a
# running stack that isn't being launched — see its header). --keep-going preserves this
# script's historical behaviour exactly: a failing migration is reported but never stops
# the launch, so `|| true` keeps that true here too.
./migrate.sh --keep-going || true

echo "==> bringing the rest of the stack up"
docker compose up "${UP_ARGS[@]}"

echo "==> stack status"
docker compose ps

echo
echo "==> access points (all on localhost — no proxy)"
printf '    %-12s %s\n' "Admin UI" "http://localhost:3000/"
printf '    %-12s %s\n' "API"      "http://localhost:8080/  (health: /health)"
printf '    %-12s %s\n' "Stream"   "http://localhost:8000/stream"
printf '    %-12s %s\n' "Icecast"  "http://localhost:8000/  (status page)"
