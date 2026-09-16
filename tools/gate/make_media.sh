#!/usr/bin/env bash
# make_media.sh <outdir> — SPEC F178.3 / STORY-445 / PLAN T489.
#
# Renders the gate leg's whole synthesised library: two 45-second tracks, sine + shaped (pink)
# noise, at integrated loudness -12 LUFS (tone-loud.mp3) and -30 LUFS (tone-quiet.mp3), tagged
# genre=music with title/artist. Each track goes through ffmpeg's loudnorm filter in linear
# (two-pass) mode — a first pass measures the source's own input_i/input_tp/input_lra/input_thresh,
# the second pass feeds those measured_* values back in for an accurate single correction — then a
# measure-and-verify pass re-measures the FINAL MP3 with ebur128 (libmp3lame's own encode can shift
# loudness slightly off what the lossless pass hit) and fails loudly if the result drifted outside
# +-1 LU of target.
#
# Also copies the committed CC0 clips from tools/gate/media/ (see SOURCES.md) beside the tones into
# <outdir> — together they are the leg's whole media library (SPEC F178.3).
#
# Usage: tools/gate/make_media.sh <outdir>
# Exit:  0 = both tracks rendered and verified; 2 = usage; 1 = a rendered track missed its target.
#        An ffmpeg/ffprobe failure along the way propagates that tool's own non-zero exit code
#        (set -e), rather than being mapped to one of the above.

set -euo pipefail

if [ $# -lt 1 ]; then
  echo "usage: make_media.sh <outdir>" >&2
  exit 2
fi

OUT_DIR="$1"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MEDIA_DIR="$SCRIPT_DIR/media"
TOL_LU="1"

mkdir -p "$OUT_DIR"

# The shared sine+pink-noise source graph both tracks mix down from before loudnorm; -shortest
# trims the mix to the tone's 45s (anoisesrc has no fixed duration of its own).
SRC_GRAPH="sine=frequency=440:duration=45[tone];anoisesrc=color=pink:duration=45[noise];[tone][noise]amix=inputs=2:duration=shortest"

# measured_field <json> <key> — pulls one numeric value ffmpeg's loudnorm print_format=json prints
# to stderr, e.g. '"input_i" : "-19.93",' -> -19.93.
measured_field() {
  local json="$1" key="$2"
  printf '%s\n' "$json" | grep "\"${key}\"" | grep -oE -- '-?[0-9.]+' | head -1
}

# integrated_lufs <file> — the FINAL "I: <n> LUFS" line ffmpeg's ebur128 filter prints (its summary
# block repeats the label; the last match is always the summary, matching how the spec measures it).
integrated_lufs() {
  local file="$1"
  ffmpeg -nostats -hide_banner -i "$file" -filter_complex ebur128=peak=true -f null - 2>&1 \
    | grep -oE 'I:[[:space:]]*-?[0-9.]+ LUFS' | tail -1 | grep -oE -- '-?[0-9.]+'
}

# render_track <target_lufs> <title> <out_file>
render_track() {
  local target="$1" title="$2" out="$3"
  local measure_json in_i in_tp in_lra in_thresh measured

  measure_json=$(ffmpeg -nostats -hide_banner -f lavfi -i \
    "${SRC_GRAPH},loudnorm=I=${target}:TP=-1:LRA=7:print_format=json" \
    -ar 44100 -ac 2 -f null - 2>&1 1>/dev/null) || {
    echo "make_media.sh: loudnorm measure pass failed for target ${target} LUFS:" >&2
    echo "$measure_json" >&2
    exit 1
  }
  in_i=$(measured_field "$measure_json" input_i)
  in_tp=$(measured_field "$measure_json" input_tp)
  in_lra=$(measured_field "$measure_json" input_lra)
  in_thresh=$(measured_field "$measure_json" input_thresh)

  ffmpeg -nostats -hide_banner -loglevel error -y -f lavfi -i \
    "${SRC_GRAPH},loudnorm=I=${target}:TP=-1:LRA=7:measured_I=${in_i}:measured_TP=${in_tp}:measured_LRA=${in_lra}:measured_thresh=${in_thresh}:linear=true" \
    -metadata genre=music -metadata title="$title" -metadata artist="GenWave Gate" \
    -ar 44100 -ac 2 -codec:a libmp3lame -q:a 2 "$out"

  measured=$(integrated_lufs "$out")
  if ! awk -v m="$measured" -v t="$target" -v tol="$TOL_LU" \
    'BEGIN { d = m - t; if (d < 0) d = -d; exit !(d <= tol) }'; then
    echo "make_media.sh: $out measured ${measured} LUFS, outside +-${TOL_LU} LU of target ${target}" >&2
    exit 1
  fi
}

render_track -12 "Gate Tone Loud" "$OUT_DIR/tone-loud.mp3"
render_track -30 "Gate Tone Quiet" "$OUT_DIR/tone-quiet.mp3"

# The committed CC0 clips join the tones as the leg's whole library.
if [ -d "$MEDIA_DIR" ]; then
  find "$MEDIA_DIR" -maxdepth 1 -type f ! -name '*.md' -exec cp {} "$OUT_DIR/" \;
fi
