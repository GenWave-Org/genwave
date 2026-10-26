#!/usr/bin/env bash
# check-compose-socket.sh — SPEC F78.2 / STORY-203 guard.
#
# /var/run/docker.sock is the keys to the host: any container that can read it can control
# every other container (and, transitively, the host). Exactly two services may ever bind-mount
# it, both read-only: the `alloy` metrics/log collector (PLAN T49) and the `dockerproxy`
# allowlisting socket proxy behind the Health page's container stats (gh-#148). This guard pins
# that invariant shut across every render this repo produces: base (compose.yaml), base+pinned
# (compose.pinned.yaml), base+piper-only (compose.piper-only.yaml, gh-#242) and base+pinned+
# piper-only, under every profile combination (none, admin, logging, fallback, admin+logging+fallback).
#
# The socket is matched by basename pattern (any source/target path ending in `docker.sock`),
# not an exact `/var/run/docker.sock` string — `/var/run` is a symlink to `/run` on modern
# Linux, so `source: /run/docker.sock` mounts the same host daemon socket and must be caught
# too.
#
# Today `dockerproxy` mounts it in every render and `alloy` under the `logging` profile, both
# read-only. The guard fails naming the offender if either loses `read_only: true`, or if any
# other service picks up the socket.
#
# Usage:
#   tools/check-compose-socket.sh
#     Renders the real merged config itself (`docker compose ... config --format json`, from
#     the repo root) for every file-set x profile-combination below and checks each one.
#     Requires the docker CLI. Dummy secrets are exported for the vars compose.yaml
#     requires (${VAR:?}) so this runs standalone, without a real .env —
#     `config` only merges and substitutes text, it never talks to a daemon or starts a
#     container. Same idiom as tools/check-compose-publish.sh.
#
#   tools/check-compose-socket.sh --config-file <path>
#     Checks a single already-rendered `docker compose ... config --format json` document at
#     <path> instead of invoking docker. Lets callers (tests, CI on a docker-less runner) drive
#     fixtures without the docker CLI (STORY-203 AC).
#
# Exit: 0 — every render checked: only `alloy`/`dockerproxy` mount docker.sock, and only read-only.
#       1 — at least one offending service:render is named on stdout.
#       2 — usage/environment error (bad args, jq missing, file not found).
#       A failed `docker compose config` render exits with docker's own non-zero code (usually
#       1), with docker's error on stderr and no offender printed.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Matches any volume source/target whose basename is docker.sock — /var/run/docker.sock,
# /run/docker.sock (the /var/run symlink target on modern Linux), or any other path ending in
# it. Anchored with `$`: "docker.sock" must be the final path segment.
SOCKET_PATTERN='docker\.sock$'
ALLOWED_SERVICES=("alloy" "dockerproxy")

is_allowed_service() {
  local candidate="$1" allowed
  for allowed in "${ALLOWED_SERVICES[@]}"; do
    [ "$candidate" = "$allowed" ] && return 0
  done
  return 1
}

CONFIG_FILE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --config-file)
      [ $# -ge 2 ] || { echo "check-compose-socket: --config-file needs a path" >&2; exit 2; }
      CONFIG_FILE="$2"
      shift 2
      ;;
    -h|--help)
      awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "$0"
      exit 0
      ;;
    *)
      echo "check-compose-socket: unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

command -v jq >/dev/null 2>&1 || { echo "check-compose-socket: jq is required" >&2; exit 2; }

# jq filter shared by every render: emits one TSV row per volume entry across every service —
# service, source, target, read_only (normalized to "true"/"false"). Volumes normally render as
# long-syntax objects under `config --format json`, but short-syntax strings
# ("/var/run/docker.sock:/var/run/docker.sock:ro") are handled too — defensive, cheap, and it
# costs nothing to catch a rendering shape we don't currently rely on.
VOLUME_ROWS_FILTER='
  def normalize_volume($service; $v):
    if ($v | type) == "string" then
      ($v | split(":")) as $parts |
      {
        service: $service,
        source: ($parts[0] // ""),
        target: ($parts[1] // ""),
        read_only: (($parts[2] // "") | split(",") | any(. == "ro"))
      }
    else
      {
        service: $service,
        source: ($v.source // ""),
        target: ($v.target // ""),
        read_only: ($v.read_only // false)
      }
    end;

  (.services // {}) | to_entries[] |
  .key as $service |
  ((.value.volumes // [])[]) |
  normalize_volume($service; .) |
  [.service, .source, .target, (.read_only | tostring)] | @tsv
'

# Checks one rendered config (a JSON document, already in $1) for socket-mount offenders.
# Appends "label: offender" strings to the global `offenders` array; does not exit.
check_render() {
  local label="$1" config_json="$2"
  local rows service source target read_only

  rows="$(jq -r "$VOLUME_ROWS_FILTER" <<<"$config_json")"

  while IFS=$'\t' read -r service source target read_only; do
    [ -n "$service" ] || continue
    [[ "$source" =~ $SOCKET_PATTERN ]] || [[ "$target" =~ $SOCKET_PATTERN ]] || continue

    if is_allowed_service "$service"; then
      if [ "$read_only" = "true" ]; then
        allowed_report+=("$label:$service")
      else
        offenders+=("$label: $service mounts docker.sock read-write (must be read_only: true)")
      fi
    else
      offenders+=("$label: $service mounts docker.sock (only ${ALLOWED_SERVICES[*]} may)")
    fi
  done <<<"$rows"
}

offenders=()
allowed_report=()

if [ -n "$CONFIG_FILE" ]; then
  [ -f "$CONFIG_FILE" ] || { echo "check-compose-socket: no such file: $CONFIG_FILE" >&2; exit 2; }
  config_file_json="$(cat "$CONFIG_FILE")"
  jq empty <<<"$config_file_json" >/dev/null 2>&1 || {
    echo "check-compose-socket: malformed JSON in $CONFIG_FILE" >&2
    exit 2
  }
  check_render "config-file" "$config_file_json"
else
  command -v docker >/dev/null 2>&1 || { echo "check-compose-socket: docker is required (or pass --config-file)" >&2; exit 2; }

  # Dummy values for every ${VAR:?} compose.yaml requires — `config` only
  # renders text, so these never reach a running container. `: "${VAR:=...}"` leaves a
  # caller's real value (e.g. a sourced .env) alone and only fills the gap.
  : "${POSTGRES_PASSWORD:=check-compose-socket-dummy}"
  : "${LIBRARY_DB_PASSWORD:=check-compose-socket-dummy}"
  : "${STATION_DB_PASSWORD:=check-compose-socket-dummy}"
  : "${ICECAST_SOURCE_PASSWORD:=check-compose-socket-dummy}"
  : "${ICECAST_ADMIN_PASSWORD:=check-compose-socket-dummy}"
  : "${ADMIN_PASSWORD:=check-compose-socket-dummy}"
  : "${MEDIA_DIR:=/tmp/check-compose-socket-dummy}"
  export POSTGRES_PASSWORD LIBRARY_DB_PASSWORD STATION_DB_PASSWORD ICECAST_SOURCE_PASSWORD \
         ICECAST_ADMIN_PASSWORD ADMIN_PASSWORD MEDIA_DIR

  # Every file-set this repo actually deploys: the base stack, the --pinned published-image
  # pins, and the piper-only low-memory overlay on each (gh-#242 — always merged last,
  # matching launch.sh --piper-only). Add a new overlay here the day one ships.
  FILE_SETS=(
    "base:compose.yaml"
    "base+pinned:compose.yaml compose.pinned.yaml"
    "base+piper-only:compose.yaml compose.piper-only.yaml"
    "base+pinned+piper-only:compose.yaml compose.pinned.yaml compose.piper-only.yaml"
  )

  # Every profile combination that changes which services exist. "admin" gates admin_ui,
  # "fallback" gates the piper fallback voice, "logging" gates alloy (and its socket mount).
  # An unknown profile name is not an error to `docker compose config` (verified) — it just
  # filters out services that don't opt in, exactly like a real one.
  PROFILE_COMBOS=(
    "none:"
    "admin:admin"
    "logging:logging"
    "fallback:fallback"
    "admin+logging+fallback:admin,logging,fallback"
  )

  for file_set in "${FILE_SETS[@]}"; do
    files_label="${file_set%%:*}"
    files="${file_set#*:}"
    compose_file_args=()
    for f in $files; do compose_file_args+=(-f "$f"); done

    for combo in "${PROFILE_COMBOS[@]}"; do
      combo_label="${combo%%:*}"
      profiles="${combo#*:}"
      compose_profile_args=()
      if [ -n "$profiles" ]; then
        IFS=',' read -ra profs <<<"$profiles"
        for p in "${profs[@]}"; do compose_profile_args+=(--profile "$p"); done
      fi

      render_label="$files_label (profiles: $combo_label)"
      config_json="$(cd "$REPO_ROOT" && docker compose "${compose_profile_args[@]}" "${compose_file_args[@]}" config --format json)"
      check_render "$render_label" "$config_json"
    done
  done
fi

if [ "${#offenders[@]}" -gt 0 ]; then
  echo "check-compose-socket: FAIL — docker.sock mounted outside the ${ALLOWED_SERVICES[*]} read-only carve-outs:"
  for offender in "${offenders[@]}"; do
    echo "  $offender"
  done
  exit 1
fi

echo "check-compose-socket: OK — docker.sock mounts: ${allowed_report[*]:-(none)}"
exit 0
