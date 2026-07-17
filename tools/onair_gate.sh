#!/usr/bin/env bash
# onair_gate.sh — §0 BLOCKING acceptance gate (PRD §0 / §12), engine-seam layer.
#
# Proves, against the pinned Liquidsoap 2.4.4 engine, that the on-air read can rely on the OUTPUT
# metadata instead of request-level state:
#   1. Round-trip   — our stamped track_id + on_air actually appear in output.icecast.metadata.
#   2. Drain signal — when the queue empties and `safe` airs, the CURRENT output metadata carries
#                     NO track_id. (This is the reliable signal the request.all−queue workaround
#                     could miss, leaving the station stuck on the safe rotation.)
#   3. Recovery     — pushing real content again returns it on air; it does NOT stick on safe.
#
# The C# side of "on-air uses ONLY the output metadata command, never queue listing" is asserted
# hermetically by tests/GenWave.Host.Tests. The full feeder-driven self-heal (PlayoutFeeder
# pulling from the library) is exercised at the end of the phase by the broadcast smoke test sourced
# from the library. This gate covers the engine seam those depend on.
#
# Prereqs: docker compose stack (db, engine, icecast) using this repo's genwave.liq; ffmpeg on the
#          HOST; the engine image has bash (for /dev/tcp). Run from the compose dir (GenWave).
#
# Usage:   tools/onair_gate.sh                  # uses ../.env, leaves the stack up
#          SMOKE_DOWN=1 tools/onair_gate.sh     # tear the stack down afterwards
# Exit:    0 = pass, non-zero = the §0 seam is broken (suitable as a blocking gate).

set -euo pipefail

ENV_FILE="${ENV_FILE:-../.env}"
TONE_SECS="${TONE_SECS:-12}"          # > crossfade duration so the drain is clean
FAIL=0

dc()     { docker compose --env-file "$ENV_FILE" "$@"; }

# Send one telnet command to the engine's control socket (localhost:1234 INSIDE the container,
# honoring the never-publish rule), print the reply with CR stripped.
ls_cmd() {
  dc exec -T -e LSCMD="$1" engine bash -s <<'EOF' | tr -d '\r'
exec 3<>/dev/tcp/127.0.0.1/1234 || { echo "CONNECT_FAILED"; exit 1; }
printf '%s\n' "$LSCMD" >&3
while IFS= read -r line <&3; do case "$line" in END*) break ;; *) printf '%s\n' "$line" ;; esac; done
printf 'exit\n' >&3
EOF
}

# The CURRENT on-air metadata is frame "--- 1 ---" of output.icecast.metadata; older tracks linger
# in higher-numbered frames, so we must read frame 1 specifically (exactly what LiquidsoapControl does).
current_frame() {
  ls_cmd "output.icecast.metadata" | awk '
    /^--- [0-9]+ ---$/ { cur = ($2 == "1"); next }
    cur { print }'
}

MEDIA_DIR="$(grep -E '^MEDIA_DIR=' "$ENV_FILE" | cut -d= -f2-)"
[ -n "$MEDIA_DIR" ] || { echo "MEDIA_DIR not set in $ENV_FILE"; exit 2; }
GATE_DIR="$MEDIA_DIR/_onair_gate"      # must live under MEDIA_DIR — only that is mounted into the engine

cleanup() {
  # Tolerant: the engine may still hold the airing tone open, and on a network mount (NFS) unlinking
  # an open file leaves a .nfs* placeholder so rmdir reports ENOTEMPTY. That must not fail the gate —
  # the placeholder clears once the engine stops airing it. A later run's mkdir -p is fine either way.
  rm -rf "$GATE_DIR" 2>/dev/null || true
  if [ "${SMOKE_DOWN:-0}" = "1" ]; then echo "Tearing down stack..."; dc down; fi
}
trap cleanup EXIT

# --- Generate two short, distinct tones the engine can resolve under /media. ---
mkdir -p "$GATE_DIR"
ffmpeg -nostats -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=440:duration=${TONE_SECS}" \
       -ar 44100 -ac 2 "$GATE_DIR/tone1.flac"
ffmpeg -nostats -hide_banner -loglevel error -y -f lavfi -i "sine=frequency=660:duration=${TONE_SECS}" \
       -ar 44100 -ac 2 "$GATE_DIR/tone2.flac"

# --- Bring up the stack and wait for the control socket. ---
echo "Starting db, engine, icecast..."
dc up -d db engine icecast >/dev/null
echo -n "Waiting for engine control socket"
for _ in $(seq 1 60); do
  if dc exec -T engine bash -c 'echo > /dev/tcp/127.0.0.1/1234' 2>/dev/null; then break; fi
  echo -n "."; sleep 1
done; echo

push() { # track_id path -> assigned RID
  ls_cmd "q.push annotate:track_id=\"$1\",pos=\"0\",replay_gain=\"0.00 dB\",title=\"gate-$1\":$2" | tail -1
}

# ============================ 1. ROUND-TRIP =============================================
echo "== 1. Round-trip: push a stamped track, read it back from the output metadata =="
push "GATEID1" "/media/_onair_gate/tone1.flac" >/dev/null
ON_AIR=0
for _ in $(seq 1 40); do
  CF="$(current_frame || true)"
  if grep -q 'track_id="GATEID1"' <<<"$CF" && grep -q '^on_air=' <<<"$CF"; then ON_AIR=1; break; fi
  sleep 1
done
if [ "$ON_AIR" = "1" ]; then
  echo "  PASS  track_id + on_air round-trip through output.icecast.metadata"
else
  echo "  FAIL  stamped track_id / on_air never appeared in the output metadata"; FAIL=1
fi

# ============================ 2. DRAIN SIGNAL ==========================================
echo "== 2. Drain signal: let the queue empty so 'safe' airs; current metadata must drop track_id =="
DRAINED=0
for _ in $(seq 1 $((TONE_SECS + 40))); do
  CF="$(current_frame || true)"
  if ! grep -q 'track_id=' <<<"$CF"; then DRAINED=1; break; fi
  sleep 1
done
if [ "$DRAINED" = "1" ]; then
  echo "  PASS  safe rotation airing ⇒ no track_id in the current output metadata (drain detectable)"
else
  echo "  FAIL  current metadata still carried a track_id after the queue should have drained"; FAIL=1
fi

# ============================ 3. RECOVERY ==============================================
echo "== 3. Recovery: push real content again; it must return on air (not stick on safe) =="
push "GATEID2" "/media/_onair_gate/tone2.flac" >/dev/null
RECOVERED=0
for _ in $(seq 1 40); do
  if grep -q 'track_id="GATEID2"' <<<"$(current_frame || true)"; then RECOVERED=1; break; fi
  sleep 1
done
if [ "$RECOVERED" = "1" ]; then
  echo "  PASS  real content returned on air after the drain"
else
  echo "  FAIL  station stuck on safe — real content did not return"; FAIL=1
fi

echo
if [ "$FAIL" = "0" ]; then echo "§0 ON-AIR GATE PASSED"; else echo "§0 ON-AIR GATE FAILED"; fi
exit "$FAIL"
