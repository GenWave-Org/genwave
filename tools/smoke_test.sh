#!/usr/bin/env bash
# smoke_test.sh — end-to-end regression gate for the broadcast pipeline.
#
# MANUAL PRE-RELEASE GATE. CI intentionally does NOT run this script (SPEC F32, gitea-#179) —
# `.gitea/workflows/dotnet-ci.yml` only runs `dotnet test` + the admin-ui tsc/jest/build
# steps. There is no plan to wire this in; run it yourself, by hand, before a release.
#
# NEVER RUN AGAINST A LIVE STATION. This script pushes two test tracks directly onto
# whatever engine it targets and records ~90s of its real Icecast stream — that preempts
# real programming. Always target an isolated scratch project:
#   - a fresh checkout in a scratch directory, or
#   - the same checkout with an explicit `docker compose -p <scratch-project> ...`
# Either way, use your own `.env` (own ADMIN_PASSWORD / Icecast passwords, own MEDIA_DIR),
# own ports (STREAM_URL below must match), own volumes — never the host's production
# project or `.env`. SMOKE_DOWN=1 tears the scratch stack down when the run finishes.
#
# Proves, WITHOUT a human listening, that:
#   1. The engine accepts annotated pushes over the control socket.
#   2. Level matching works: two tracks with very DIFFERENT source loudness, each given a
#      precomputed replay_gain, both come out of the stream at ~target loudness.
#   3. The crossfade actually overlaps (no silent gap) at the transition.
#
# It records the live Icecast output and measures it back with ebur128 — so a wrong
# replay_gain format, an unloaded password, or a broken crossfade all fail here loudly.
#
# Prereqs: docker compose stack defined (db, engine, icecast) using the project's genwave.liq;
#          ffmpeg on the HOST; jq; engine image has bash (for /dev/tcp). Run find_smoke_candidates
#          first to produce smoke-candidates.json.
#
# Usage:   ./smoke_test.sh                 # uses ./smoke-candidates.json
#          SMOKE_DOWN=1 ./smoke_test.sh    # tear the (scratch!) stack down afterwards
# Exit code: 0 = pass, non-zero = regression.
#
# Footgun: CROSSFADE (below) must track GW_XFADE_MAX / cross(duration=xfade_max) in
# genwave.liq — change one, change the other, or the analysis windows misalign.

set -euo pipefail

CANDIDATES="${1:-smoke-candidates.json}"
STREAM_URL="${STREAM_URL:-http://localhost:8000/stream}"
CROSSFADE="${CROSSFADE:-8}"        # must match GW_XFADE_MAX / cross(duration=xfade_max) in genwave.liq
TOL_LU="${TOL_LU:-5.0}"            # allowed deviation from target (LU). This is a coarse spine gate:
                                   # it measures finite windows of real, dynamic program material
                                   # against a whole-track integrated target, so a few LU of slack is
                                   # expected. The gross failures it must catch (wrong replay_gain
                                   # format, no normalization) show up as 10-20 LU on the loud track.
SILENCE_FLOOR="${SILENCE_FLOOR:--45}"  # transition window must stay above this (proves no gap)
REC="$(mktemp -t smoke-XXXX.wav)"
FAIL=0

cleanup() {
  rm -f "$REC"
  if [ "${SMOKE_DOWN:-0}" = "1" ]; then echo "Tearing down stack..."; docker compose down; fi
}
trap cleanup EXIT

command -v jq >/dev/null || { echo "jq required"; exit 2; }
[ -f "$CANDIDATES" ] || { echo "Missing $CANDIDATES — run find_smoke_candidates first"; exit 2; }

TARGET=$(jq -r '.target_lufs'   "$CANDIDATES")
PATH_A=$(jq -r '.a.MediaPath'   "$CANDIDATES")   # quiet source, needs boosting
GAIN_A=$(jq -r '.a.GainDb'      "$CANDIDATES")
DUR_A=$(jq -r  '.a.DurationSec' "$CANDIDATES")
PATH_B=$(jq -r '.b.MediaPath'   "$CANDIDATES")   # loud source, needs cutting
GAIN_B=$(jq -r '.b.GainDb'      "$CANDIDATES")

echo "Target ${TARGET} LUFS | A ${PATH_A} (gain ${GAIN_A} dB) | B ${PATH_B} (gain ${GAIN_B} dB)"

# --- Liquidsoap control helper: send one command, print response (CR stripped). ---
#     Talks to localhost:1234 INSIDE the engine container, honoring the never-publish rule.
ls_cmd() {
  docker compose exec -T -e LSCMD="$1" engine bash -s <<'EOF' | tr -d '\r'
exec 3<>/dev/tcp/127.0.0.1/1234 || { echo "CONNECT_FAILED"; exit 1; }
printf '%s\n' "$LSCMD" >&3
while IFS= read -r line <&3; do
  case "$line" in END*) break ;; *) printf '%s\n' "$line" ;; esac
done
printf 'exit\n' >&3
EOF
}

# --- Bring the stack up and wait until both the control socket and the stream are live. ---
echo "Starting db, engine, icecast..."
docker compose up -d db engine icecast >/dev/null
echo -n "Waiting for engine control socket"
for _ in $(seq 1 60); do
  if ls_cmd "uptime" | grep -qv CONNECT_FAILED; then break; fi
  echo -n "."; sleep 1
done; echo

# --- Push the divergent pair. Order A then B so the crossfade goes quiet->loud. ---
#     Gain is sent in the ReplayGain string convention "X.XX dB".
echo "Pushing A and B..."
RID_A=$(ls_cmd "q.push annotate:replay_gain=\"${GAIN_A} dB\",title=\"smokeA\":${PATH_A}" | tail -1)
RID_B=$(ls_cmd "q.push annotate:replay_gain=\"${GAIN_B} dB\",title=\"smokeB\":${PATH_B}" | tail -1)
echo "  RID_A=${RID_A} RID_B=${RID_B}"

# --- Wait for A to ACTUALLY go on air, then record from t≈0 of track A. ---
#     Liquidsoap 2.4 has no request.on_air; the output exposes its current metadata, which carries our
#     annotated title. If A never airs within the timeout it almost certainly failed to resolve (e.g.
#     its path doesn't exist under MEDIA_DIR), so fail loudly instead of recording the safe loop.
echo -n "Waiting for A on air"
ON_AIR=0
for _ in $(seq 1 30); do
  if ls_cmd "output.icecast.metadata" | grep -q 'title="smokeA"'; then ON_AIR=1; break; fi
  echo -n "."; sleep 1
done; echo
if [ "$ON_AIR" != "1" ]; then
  echo "FAIL: track A never reached the stream — the engine is still on the safe loop."
  echo "  Its path almost certainly does not resolve inside the engine. Verify that:"
  echo "    * MEDIA_DIR matches the library you ran find_smoke_candidates against, and"
  echo "    * the MediaPath in '$CANDIDATES' exists under that mount, e.g.:"
  echo "        docker compose exec engine ls \"$PATH_A\""
  exit 1
fi

# Analysis windows (seconds), relative to recording start ≈ A on-air.
#   A steady (A alone) | transition (A+B overlap) | B steady (B alone)
# A short clip of a track with a quiet intro does NOT equal its integrated loudness, so measure LONG
# steady spans that converge to it: most of A (before the crossfade) and a 30s chunk of B (after it).
A_START=8
A_LEN=$(awk "BEGIN{l=$DUR_A - $CROSSFADE - $A_START - 2; print (l<10)?10:l}")
TRANS_START=$(awk "BEGIN{print $DUR_A - $CROSSFADE + 1}")
B_START=$(awk "BEGIN{print $DUR_A + $CROSSFADE + 3}")
B_LEN=60
TOTAL=$(awk "BEGIN{print $B_START + $B_LEN + 3}")

echo "Recording ${TOTAL}s of ${STREAM_URL}..."
ffmpeg -nostats -hide_banner -loglevel error -y -i "$STREAM_URL" -t "$TOTAL" "$REC"

# --- Measure integrated loudness of a window of the recording. ---
measure() { # start len -> integrated LUFS (last/Summary value)
  ffmpeg -nostats -hide_banner -ss "$1" -t "$2" -i "$REC" \
         -filter_complex ebur128=peak=true -f null - 2>&1 \
    | sed -n 's/.*I:[[:space:]]*\(-\{0,1\}[0-9.]*\)[[:space:]]*LUFS.*/\1/p' | tail -1
}

near_target() { awk -v m="$1" -v t="$TARGET" -v tol="$TOL_LU" \
  'BEGIN{d=m-t; if(d<0)d=-d; exit !(d<=tol)}'; }

A_I=$(measure "$A_START" "$A_LEN")
B_I=$(measure "$B_START" "$B_LEN")
T_I=$(measure "$TRANS_START" "$CROSSFADE")

echo
echo "=== Results (target ${TARGET} LUFS, tolerance ±${TOL_LU} LU) ==="

# Level matching: both tracks, despite different source loudness, land near target.
near_target "$A_I"            && echo "  PASS  A level-matched (${A_I})"        || { echo "  FAIL  A level-matched (${A_I})"; FAIL=1; }
near_target "$B_I"            && echo "  PASS  B level-matched (${B_I})"        || { echo "  FAIL  B level-matched (${B_I})"; FAIL=1; }
# Continuity: the overlap window is not silent (no dead-air gap at the crossfade).
awk -v m="$T_I" -v f="$SILENCE_FLOOR" 'BEGIN{exit !(m>f)}' \
                             && echo "  PASS  crossfade continuous (${T_I})"    || { echo "  FAIL  crossfade gap/silence (${T_I})"; FAIL=1; }

echo
if [ "$FAIL" = "0" ]; then
  echo "SMOKE TEST PASSED"
else
  echo "SMOKE TEST FAILED — likely causes:"
  echo "  * level FAIL  -> replay_gain annotation format (try bare number vs \"X dB\"), or amplify override not applied"
  echo "  * gap   FAIL  -> crossfade not overlapping (check crossfade operator/duration in genwave.liq)"
fi
exit "$FAIL"
