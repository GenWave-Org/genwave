#!/usr/bin/env sh
# entrypoint.sh — engine boot wrapper
#
# Fetches effective GW_XFADE_MIN / GW_XFADE_MAX / GW_SAFE_GAP_SECONDS from the api's
# /internal/engine-config endpoint (which merges the station.settings overlay on top of
# appsettings defaults) and exports them into the environment before launching Liquidsoap.
#
# ROBUSTNESS CONTRACT:
#   • 3 attempts, 2-second timeout each.
#   • On any failure (api not yet up, network error, non-200 response, parse error)
#     the existing env values (set by compose as fallback defaults) are preserved.
#   • The engine ALWAYS boots — a missing/slow api is NOT a fatal error here.
#
# SECURITY: /internal/engine-config is anonymous and exposes only these tuning numbers.
# It is reachable only on the `core` internal Docker network.

API_HOST="${API_HOST:-api}"
API_PORT="${API_PORT:-8080}"
CONFIG_URL="http://${API_HOST}:${API_PORT}/internal/engine-config"

MAX_ATTEMPTS=3
ATTEMPT=0
FETCHED=""

while [ "${ATTEMPT}" -lt "${MAX_ATTEMPTS}" ]; do
    ATTEMPT=$(( ATTEMPT + 1 ))
    RESPONSE=$(curl --silent --max-time 2 --fail "${CONFIG_URL}" 2>/dev/null)
    if [ $? -eq 0 ] && [ -n "${RESPONSE}" ]; then
        FETCHED="${RESPONSE}"
        break
    fi
    echo "[engine-entrypoint] attempt ${ATTEMPT}/${MAX_ATTEMPTS}: could not reach ${CONFIG_URL}" >&2
done

if [ -n "${FETCHED}" ]; then
    # Parse each line of the form KEY=VALUE and export into the environment.
    # Only accept the expected keys to avoid arbitrary env injection.
    while IFS= read -r line; do
        case "${line}" in
            GW_XFADE_MIN=*)
                val="${line#GW_XFADE_MIN=}"
                if [ -n "${val}" ]; then
                    GW_XFADE_MIN="${val}"
                    export GW_XFADE_MIN
                fi
                ;;
            GW_XFADE_MAX=*)
                val="${line#GW_XFADE_MAX=}"
                if [ -n "${val}" ]; then
                    GW_XFADE_MAX="${val}"
                    export GW_XFADE_MAX
                fi
                ;;
            GW_SAFE_GAP_SECONDS=*)
                val="${line#GW_SAFE_GAP_SECONDS=}"
                if [ -n "${val}" ]; then
                    GW_SAFE_GAP_SECONDS="${val}"
                    export GW_SAFE_GAP_SECONDS
                fi
                ;;
        esac
    done << EOF
${FETCHED}
EOF
    echo "[engine-entrypoint] crossfade range: GW_XFADE_MIN=${GW_XFADE_MIN} GW_XFADE_MAX=${GW_XFADE_MAX}" >&2
    echo "[engine-entrypoint] safe-track gap: GW_SAFE_GAP_SECONDS=${GW_SAFE_GAP_SECONDS}" >&2
else
    echo "[engine-entrypoint] api unreachable after ${MAX_ATTEMPTS} attempts; using fallback env: GW_XFADE_MIN=${GW_XFADE_MIN:-<unset>} GW_XFADE_MAX=${GW_XFADE_MAX:-<unset>} GW_SAFE_GAP_SECONDS=${GW_SAFE_GAP_SECONDS:-<unset>}" >&2
fi

exec liquidsoap /genwave.liq
