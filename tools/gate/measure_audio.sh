#!/usr/bin/env bash
# measure_audio.sh <wav> --target <lufs> — SPEC F178.5/F178.8(a) / STORY-445/447 / PLAN T490/T498.
#
# Two probes over one WAV, both always run and both always reported before the script exits:
#   (a) ffmpeg's silencedetect counts gaps of at least SILENCE_SECS seconds at or below
#       SILENCE_FLOOR dBFS — the "no silent gaps" check, bounded by SILENCE_MAX_SECS below
#       (defaults to today's "zero silence, any length" behaviour).
#   (b) ffmpeg's ebur128 integrated loudness (the same "I: <n> LUFS" summary line
#       tools/smoke_test.sh's measure() parses) must land
#       within TOL_LU of --target.
# This is a different gate with different thresholds than tools/smoke_test.sh, which keeps
# its own copy of the same parsing rather than sourcing this script.
#
# Usage: tools/gate/measure_audio.sh <wav> --target <lufs>
#
# Env knobs (all optional):
#   SILENCE_FLOOR       dBFS floor silencedetect treats as silence.        default -45
#   SILENCE_SECS        seconds of continuous silence to count as a gap.   default 2
#   TOL_LU               allowed |measured integrated LUFS - target| in LU. default 5
#   SILENCE_MAX_SECS     total silence tolerated across every event, in seconds, summed;
#                        empty/unset means today's rule: ok only when there are zero
#                        events, of any length.                           default unbounded
#
# Output: one line on stdout, always printed before the exit (report every measurement even
# when some alone fail) —
#   silence_events=<n> silence_total_secs=<n> silence_ok=<0|1> integrated_lufs=<n> target=<n> tol=<n>
# silence_total_secs is the sum of every silence_end event's duration (summed by awk, printed
# to 6 significant digits), 0 when there were none. silence_ok is 1 when: SILENCE_MAX_SECS is empty/unset AND
# silence_events == 0; OR SILENCE_MAX_SECS is set AND silence_total_secs <= SILENCE_MAX_SECS
# (no cap on silence_events in that branch) — else 0. The bound comparison runs through awk, not
# bash integer math, since silence_total_secs carries decimals.
# A verdict reason (if either check failed) goes to stderr.
#
# Exit: 0 = silence_ok=1 AND within tolerance; 1 = either measurement missed;
#       2 = usage error (missing wav, missing/non-numeric --target, file not found,
#       non-numeric SILENCE_FLOOR/SILENCE_SECS/TOL_LU/SILENCE_MAX_SECS, or
#       ffmpeg could not measure the file).

set -euo pipefail

is_number() {
  [[ $1 =~ ^-?[0-9]+(\.[0-9]+)?$ ]]
}

if [ $# -lt 1 ]; then
  echo "Usage: measure_audio.sh <wav> --target <lufs>" >&2
  exit 2
fi

WAV="$1"
shift

TARGET=""
while [ $# -gt 0 ]; do
  case "$1" in
    --target)
      if [ $# -lt 2 ]; then
        echo "measure_audio.sh: --target requires a value" >&2
        exit 2
      fi
      TARGET="$2"
      shift 2
      ;;
    *)
      echo "measure_audio.sh: Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [ -z "$TARGET" ]; then
  echo "Usage: measure_audio.sh <wav> --target <lufs>" >&2
  exit 2
fi
if ! is_number "$TARGET"; then
  echo "measure_audio.sh: --target must be numeric, got: $TARGET" >&2
  exit 2
fi
if [ ! -f "$WAV" ]; then
  echo "measure_audio.sh: No such file: $WAV" >&2
  exit 2
fi

SILENCE_FLOOR="${SILENCE_FLOOR:--45}"
SILENCE_SECS="${SILENCE_SECS:-2}"
TOL_LU="${TOL_LU:-5}"
SILENCE_MAX_SECS="${SILENCE_MAX_SECS:-}"

if ! is_number "$SILENCE_FLOOR"; then
  echo "measure_audio.sh: SILENCE_FLOOR must be numeric, got: $SILENCE_FLOOR" >&2
  exit 2
fi
if ! is_number "$SILENCE_SECS"; then
  echo "measure_audio.sh: SILENCE_SECS must be numeric, got: $SILENCE_SECS" >&2
  exit 2
fi
if ! is_number "$TOL_LU"; then
  echo "measure_audio.sh: TOL_LU must be numeric, got: $TOL_LU" >&2
  exit 2
fi
if [ -n "$SILENCE_MAX_SECS" ] && ! is_number "$SILENCE_MAX_SECS"; then
  echo "measure_audio.sh: SILENCE_MAX_SECS must be numeric, got: $SILENCE_MAX_SECS" >&2
  exit 2
fi

# --- (a) silencedetect: count completed silence_end events, and their total duration. ---
# `set -e` would otherwise abort inside this assignment on a corrupt/unreadable input, with
# nothing reported — catch the failure explicitly and surface ffmpeg's own diagnostic.
if ! silence_log=$(ffmpeg -nostats -hide_banner -i "$WAV" \
  -af "silencedetect=noise=${SILENCE_FLOOR}dB:d=${SILENCE_SECS}" -f null - 2>&1); then
  printf '%s\n' "$silence_log" | tail -5 >&2
  echo "measure_audio.sh: Ffmpeg could not measure $WAV" >&2
  exit 2
fi
# grep -c exits 1 on zero matches (the continuous-tone case) — `|| true` keeps that from
# tripping `set -e`/pipefail; the printed count ("0") is what we want either way.
# On ffmpeg >= 5, silencedetect flushes a pending silence_end at EOF, so a trailing gap that
# never resolves mid-stream still counts here.
silence_events=$(printf '%s\n' "$silence_log" | grep -c 'silence_end' || true)

# The sum of every `silence_duration:` value across every silence_end line, summed by awk and
# printed to 6 significant digits (e.g. "3.02067" + "1.5" -> "4.52067") — 0 when there were none.
silence_total_secs=$(printf '%s\n' "$silence_log" \
  | sed -n 's/.*silence_duration:[[:space:]]*\([0-9.]*\).*/\1/p' | awk '{sum += $1} END {print sum + 0}')

# --- (b) ebur128: the last "I: <n> LUFS" summary line, lifted from measure() in tools/smoke_test.sh. ---
if ! ebur_log=$(ffmpeg -nostats -hide_banner -i "$WAV" -filter_complex ebur128=peak=true -f null - 2>&1); then
  printf '%s\n' "$ebur_log" | tail -5 >&2
  echo "measure_audio.sh: Ffmpeg could not measure $WAV" >&2
  exit 2
fi
integrated_lufs=$(printf '%s\n' "$ebur_log" \
  | sed -n 's/.*I:[[:space:]]*\(-\{0,1\}[0-9.]*\)[[:space:]]*LUFS.*/\1/p' | tail -1)

level_ok=$(awk -v m="$integrated_lufs" -v t="$TARGET" -v tol="$TOL_LU" \
  'BEGIN { d = m - t; if (d < 0) d = -d; print (d <= tol) ? 1 : 0 }')

if [ -z "$SILENCE_MAX_SECS" ]; then
  if [ "$silence_events" -eq 0 ]; then silence_ok=1; else silence_ok=0; fi
else
  silence_ok=$(awk -v total="$silence_total_secs" -v bound="$SILENCE_MAX_SECS" \
    'BEGIN { print (total <= bound) ? 1 : 0 }')
fi

printf 'silence_events=%s silence_total_secs=%s silence_ok=%s integrated_lufs=%s target=%s tol=%s\n' \
  "$silence_events" "$silence_total_secs" "$silence_ok" "$integrated_lufs" "$TARGET" "$TOL_LU"

exit_code=0
if [ "$silence_ok" -ne 1 ]; then
  bound_desc="zero silence tolerated"
  [ -n "$SILENCE_MAX_SECS" ] && bound_desc="SILENCE_MAX_SECS=$SILENCE_MAX_SECS"
  echo "measure_audio.sh: $silence_events silence event(s) totalling ${silence_total_secs}s of at least ${SILENCE_SECS}s at or below ${SILENCE_FLOOR}dB exceeds the bound (${bound_desc})" >&2
  exit_code=1
fi
if [ "$level_ok" -ne 1 ]; then
  echo "measure_audio.sh: Integrated ${integrated_lufs} LUFS is more than ${TOL_LU} LU from target ${TARGET}" >&2
  exit_code=1
fi

exit "$exit_code"
