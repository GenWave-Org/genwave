#!/usr/bin/env bash
# tools/test-pronunciation.sh — hear how the TTS engine says something, fast (gh-#37).
#
# The problem: Kokoro mangles some names ("Kevin MacLeod" → "Mah-Cleo-Duh"). The fix is a spoken
# respelling ("muh-CLOUD") — but finding the spelling that works is trial and error. This script
# is the loop: feed it text, hear the render, adjust, repeat.
#
# Once a spelling works, make it permanent as a SPEECH CORRECTION (Admin UI → Settings →
# Speech corrections, SPEC F70): a match/replace rule applied at the TTS hand-off for every
# DJ — no per-persona backstory hacks needed.
#
# Usage:
#   tools/test-pronunciation.sh "Kevin MacLeod"          # render + play once
#   tools/test-pronunciation.sh -v af_bella "muh-CLOUD"  # specific voice
#   tools/test-pronunciation.sh                          # interactive loop (blank line quits)
#   tools/test-pronunciation.sh -o take1.wav "text"      # keep the wav (implies no cleanup)
#
# Options / env:
#   -v VOICE       voice id (default: the station voice — server-side default)
#   -u URL         api base url            (default: http://localhost:8080, or GW_API_URL)
#   -o FILE        save the wav here instead of a temp file
#   ADMIN_PASSWORD admin password; read from .env, else prompted (never echoed)
#
# Renders through the real admin preview endpoint (POST /api/tts/preview) — the identical
# normalize→synthesize path patter uses, so what you hear is what airs. Requires a running stack
# (./launch.sh) and curl; plays via the first of mpv/ffplay/aplay/paplay found, else keeps the
# file and prints its path.
set -euo pipefail
cd "$(dirname "$0")/.."

BASE_URL="${GW_API_URL:-http://localhost:8080}"
VOICE=""
OUT_FILE=""

usage() { awk 'NR==1{next} /^#/{sub(/^# ?/,""); print; next} {exit}' "$0"; }

while getopts ":v:u:o:h" opt; do
  case "$opt" in
    v) VOICE="$OPTARG" ;;
    u) BASE_URL="$OPTARG" ;;
    o) OUT_FILE="$OPTARG" ;;
    h) usage; exit 0 ;;
    \?) echo "test-pronunciation.sh: unknown option -$OPTARG" >&2; usage >&2; exit 2 ;;
    :) echo "test-pronunciation.sh: -$OPTARG needs a value" >&2; usage >&2; exit 2 ;;
  esac
done
shift $((OPTIND - 1))

command -v curl >/dev/null 2>&1 || { echo "test-pronunciation.sh: curl is required" >&2; exit 3; }

# --- admin password: env, then .env, then a silent prompt --------------------------------
password="${ADMIN_PASSWORD:-}"
if [ -z "$password" ] && [ -f .env ]; then
  password="$(grep -E '^ADMIN_PASSWORD=' .env | tail -n1 | cut -d= -f2- || true)"
fi
if [ -z "$password" ]; then
  read -r -s -p "Admin password: " password
  echo
fi
[ -n "$password" ] || { echo "test-pronunciation.sh: no admin password (ADMIN_PASSWORD env, .env, or prompt)" >&2; exit 3; }

# --- login once, cookie jar for the session ----------------------------------------------
cookie_jar="$(mktemp)"
trap 'rm -f "$cookie_jar"' EXIT

login_status="$(curl -s -o /dev/null -w '%{http_code}' -c "$cookie_jar" \
  -H 'Content-Type: application/json' \
  -d "$(printf '{"password":%s}' "$(printf '%s' "$password" | sed 's/\\/\\\\/g; s/"/\\"/g; s/^/"/; s/$/"/')")" \
  "$BASE_URL/api/auth/login")" || {
    echo "test-pronunciation.sh: cannot reach $BASE_URL — is the stack up? (./launch.sh)" >&2; exit 3; }

if [ "$login_status" != "204" ]; then
  echo "test-pronunciation.sh: login failed ($login_status) — wrong ADMIN_PASSWORD, or none configured (fail-closed)." >&2
  exit 3
fi

# --- player selection: first available, else keep the file -------------------------------
play() {
  local file="$1"
  if command -v mpv >/dev/null 2>&1; then mpv --really-quiet "$file"
  elif command -v ffplay >/dev/null 2>&1; then ffplay -nodisp -autoexit -loglevel quiet "$file"
  elif command -v aplay >/dev/null 2>&1; then aplay -q "$file"
  elif command -v paplay >/dev/null 2>&1; then paplay "$file"
  else
    echo "    (no player found — mpv/ffplay/aplay/paplay; wav kept at: $file)"
    KEEP_FILE=1
  fi
}

render() {
  local text="$1" file
  KEEP_FILE=0
  file="${OUT_FILE:-$(mktemp --suffix=.wav)}"

  local body status
  body="$(printf '{"text":%s%s}' \
    "$(printf '%s' "$text" | sed 's/\\/\\\\/g; s/"/\\"/g; s/^/"/; s/$/"/')" \
    "$([ -n "$VOICE" ] && printf ',"voice":"%s"' "$VOICE")")"

  status="$(curl -s -o "$file" -w '%{http_code}' -b "$cookie_jar" \
    -H 'Content-Type: application/json' -d "$body" "$BASE_URL/api/tts/preview")"

  if [ "$status" != "200" ]; then
    echo "    render failed ($status) — the TTS engine may be down or over its render budget; see: docker compose logs api" >&2
    rm -f "$file"
    return 1
  fi

  play "$file"
  if [ -n "$OUT_FILE" ]; then
    echo "    saved: $OUT_FILE"
  elif [ "${KEEP_FILE:-0}" != "1" ]; then
    rm -f "$file"
  fi
}

# --- one-shot or interactive loop --------------------------------------------------------
if [ $# -gt 0 ]; then
  render "$*"
  exit $?
fi

echo "Interactive pronunciation loop — type a spelling, hear it; blank line quits."
echo "When a spelling works, add it as a speech correction (Admin UI → Settings) so every DJ uses it."
while true; do
  read -r -p "say> " text || break
  [ -n "$text" ] || break
  render "$text" || true
done
