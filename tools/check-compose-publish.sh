#!/usr/bin/env bash
# check-compose-publish.sh — SPEC F67.1 / STORY-181 guard.
#
# On the public (compose.demo.yaml) overlay, the merged compose config must publish
# 0.0.0.0 host ports ONLY for the front proxy (caddy 80/443). Every other service must be
# loopback-bound (127.0.0.1:...) or unpublished entirely. This is exactly the Icecast
# :8000 gotcha fixed in 05303ce — this script pins it shut so it can never silently recur.
#
# Usage:
#   tools/check-compose-publish.sh
#     Renders the real merged config (`docker compose -f compose.yaml -f compose.demo.yaml
#     config --format json`, from the repo root) and checks it. Requires the docker CLI.
#     Dummy secrets are exported for the vars compose.yaml/compose.demo.yaml require
#     (${VAR:?}) so this runs standalone, without a real .env — `config` only merges and
#     substitutes text, it never talks to a daemon or starts a container.
#
#   tools/check-compose-publish.sh --config-file <path>
#     Checks an already-rendered `docker compose ... config --format json` document at
#     <path> instead of invoking docker. Lets callers (tests, CI on a docker-less runner)
#     drive fixtures without the docker CLI (STORY-181 AC3).
#
# Exit: 0 — only caddy publishes 0.0.0.0 on 80/443; every other publish is loopback-bound
#           or the service has no `ports:` at all.
#       1 — at least one offending service:port is named on stdout.
#       2 — usage/environment error (bad args, jq missing, file not found).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

ALLOWED_SERVICE="caddy"
ALLOWED_PORTS=(80 443)

# `network_mode: host` makes a container share the host's network namespace outright —
# every port it listens on binds every host interface directly, with no entry in
# `ports:` at all. That bypasses the published-port check below completely, so it needs
# its own offender check. No service is allowlisted for host networking today; if one
# ever legitimately needs it, add it here with a comment justifying the exposure.
ALLOWED_HOST_NETWORK_SERVICES=()

CONFIG_FILE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --config-file)
      [ $# -ge 2 ] || { echo "check-compose-publish: --config-file needs a path" >&2; exit 2; }
      CONFIG_FILE="$2"
      shift 2
      ;;
    -h|--help)
      awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "$0"
      exit 0
      ;;
    *)
      echo "check-compose-publish: unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

command -v jq >/dev/null 2>&1 || { echo "check-compose-publish: jq is required" >&2; exit 2; }

if [ -n "$CONFIG_FILE" ]; then
  [ -f "$CONFIG_FILE" ] || { echo "check-compose-publish: no such file: $CONFIG_FILE" >&2; exit 2; }
  CONFIG_JSON="$(cat "$CONFIG_FILE")"
else
  command -v docker >/dev/null 2>&1 || { echo "check-compose-publish: docker is required (or pass --config-file)" >&2; exit 2; }

  # Dummy values for every ${VAR:?} compose.yaml/compose.demo.yaml require — `config` only
  # renders text, so these never reach a running container. `: "${VAR:=...}"` leaves a
  # caller's real value (e.g. a sourced .env) alone and only fills the gap.
  : "${POSTGRES_PASSWORD:=check-compose-publish-dummy}"
  : "${LIBRARY_DB_PASSWORD:=check-compose-publish-dummy}"
  : "${STATION_DB_PASSWORD:=check-compose-publish-dummy}"
  : "${ICECAST_SOURCE_PASSWORD:=check-compose-publish-dummy}"
  : "${ICECAST_ADMIN_PASSWORD:=check-compose-publish-dummy}"
  : "${ADMIN_PASSWORD:=check-compose-publish-dummy}"
  : "${MEDIA_DIR:=/tmp/check-compose-publish-dummy}"
  : "${PUBLIC_HOST:=check-compose-publish.invalid}"
  export POSTGRES_PASSWORD LIBRARY_DB_PASSWORD STATION_DB_PASSWORD ICECAST_SOURCE_PASSWORD \
         ICECAST_ADMIN_PASSWORD ADMIN_PASSWORD MEDIA_DIR PUBLIC_HOST

  CONFIG_JSON="$(cd "$REPO_ROOT" && docker compose -f compose.yaml -f compose.demo.yaml config --format json)"
fi

# TSV rows, one per published port across every service: service, published (host) port,
# host_ip. Compose Specification: an omitted host_ip means "every interface" (0.0.0.0) —
# identical in effect to an explicit host_ip: 0.0.0.0, so both are treated as public.
PUBLISHES="$(jq -r '
  (.services // {}) | to_entries[] |
  .key as $service |
  ((.value.ports // [])[]) |
  [$service, (.published // "" | tostring), (.host_ip // "")] | @tsv
' <<<"$CONFIG_JSON")"

is_allowed_port() {
  local port="$1" candidate
  for candidate in "${ALLOWED_PORTS[@]}"; do
    [ "$port" = "$candidate" ] && return 0
  done
  return 1
}

is_allowed_host_network_service() {
  local service="$1" candidate
  for candidate in "${ALLOWED_HOST_NETWORK_SERVICES[@]}"; do
    [ "$service" = "$candidate" ] && return 0
  done
  return 1
}

offenders=()
allowed_report=()

# Services running with `network_mode: host` — see ALLOWED_HOST_NETWORK_SERVICES above.
HOST_NETWORK_SERVICES="$(jq -r '
  (.services // {}) | to_entries[] |
  select(.value.network_mode == "host") |
  .key
' <<<"$CONFIG_JSON")"

while IFS= read -r service; do
  [ -n "$service" ] || continue
  is_allowed_host_network_service "$service" && continue
  offenders+=("$service:network_mode=host")
done <<<"$HOST_NETWORK_SERVICES"

while IFS=$'\t' read -r service published host_ip; do
  [ -n "$service" ] || continue
  # Fail-closed: a publish is safe ONLY when host_ip is genuinely loopback. Everything
  # else — omitted, 0.0.0.0, IPv6 wildcards ("::", "::0", and any other all-zeros
  # expansion), or any value we don't recognize — is treated as public and subject to
  # the caddy-80/443 allowlist below. Do not add a case here without confirming the
  # address is loopback-only; a broad catch-all is exactly the fail-open bug this guard
  # exists to prevent.
  case "$host_ip" in
    127.*|::1|::ffff:127.*) continue ;;
  esac
  if [ "$service" = "$ALLOWED_SERVICE" ] && is_allowed_port "$published"; then
    allowed_report+=("$service:$published")
    continue
  fi
  offenders+=("$service:$published")
done <<<"$PUBLISHES"

if [ "${#offenders[@]}" -gt 0 ]; then
  echo "check-compose-publish: FAIL — 0.0.0.0 host publish outside caddy 80/443:"
  for offender in "${offenders[@]}"; do
    echo "  $offender"
  done
  exit 1
fi

echo "check-compose-publish: OK — 0.0.0.0 publishes: ${allowed_report[*]:-(none)}"
exit 0
