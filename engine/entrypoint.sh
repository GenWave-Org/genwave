#!/usr/bin/env sh
# entrypoint.sh — engine boot wrapper
#
# Fetches effective GW_XFADE_MIN / GW_XFADE_MAX / GW_SAFE_GAP_SECONDS from the api's
# /internal/engine-config endpoint (which merges the station.settings overlay on top of
# appsettings defaults) and exports them into the environment before launching Liquidsoap.
# A fourth key rides the same response (SPEC F211.5, STORY-485): the control plane's own
# display version. It is validated, logged once at boot, and used for nothing else — never
# exported, never read again anywhere in this script or by genwave.liq.
#
# ROBUSTNESS CONTRACT:
#   • 3 attempts, 2-second timeout each.
#   • On any failure (api not yet up, network error, non-200 response, parse error)
#     the existing env values (set by compose as fallback defaults) are preserved.
#   • The engine ALWAYS boots — a missing/slow api is NOT a fatal error here.
#
# SECURITY: /internal/engine-config is anonymous and exposes only these tuning numbers plus
# that display version string — never a secret. It is reachable only on the `core` internal
# Docker network. The version value is allowlist-validated against the WHOLE string — not just
# a prefix of it — before it is ever echoed: it must match ^v[0-9A-Za-z.+-]+$ end to end, or be
# exactly "unknown"; any other byte anywhere in the value (a raw CR, an ANSI escape, a space, a
# shell metacharacter) forces "invalid" instead. The value lands straight in the container log,
# so this is closing a log-injection vector even though the only sender today is the api on a
# trusted network.

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
            GW_APP_VERSION=*)
                # Deliberately NOT exported — this key is for the one boot log line below and
                # nothing else; app_version is a plain shell variable, local to this process.
                val="${line#GW_APP_VERSION=}"
                # Whole-string allowlist, equivalent to ^(unknown|v[0-9A-Za-z.+-]+)$. The invalid
                # arm must precede `v*`: `v*[!...]*` catches a disallowed byte at ANY position.
                # Bracket matching is byte-for-byte, so a raw CR or ESC is simply "not in the class".
                case "${val}" in
                    unknown)                  app_version="${val}" ;;
                    v|v*[!0-9A-Za-z.+-]*)     app_version="invalid" ;;
                    v*)                       app_version="${val}" ;;
                    *)                        app_version="invalid" ;;
                esac
                ;;
        esac
    done << EOF
${FETCHED}
EOF
    echo "[engine-entrypoint] crossfade range: GW_XFADE_MIN=${GW_XFADE_MIN} GW_XFADE_MAX=${GW_XFADE_MAX}" >&2
    echo "[engine-entrypoint] safe-track gap: GW_SAFE_GAP_SECONDS=${GW_SAFE_GAP_SECONDS}" >&2
    echo "[engine-entrypoint] control-plane version: GW_APP_VERSION=${app_version:-<unset>}" >&2
else
    echo "[engine-entrypoint] api unreachable after ${MAX_ATTEMPTS} attempts; using fallback env: GW_XFADE_MIN=${GW_XFADE_MIN:-<unset>} GW_XFADE_MAX=${GW_XFADE_MAX:-<unset>} GW_SAFE_GAP_SECONDS=${GW_SAFE_GAP_SECONDS:-<unset>}" >&2
    # No fallback default exists for the version marker (unlike the three keys above, which
    # compose seeds with real fallback defaults) — there is nothing meaningful to log when the
    # api was never reached, so it is omitted here rather than printed as another "<unset>" line.
fi

exec liquidsoap /genwave.liq
