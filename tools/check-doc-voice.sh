#!/usr/bin/env bash
# check-doc-voice.sh — shipped docs must read like a human wrote them (SPEC F215.9).
#
# Forbids planning-corpus citations in prose: `SPEC F<n>`, `STORY-<n>`, `gh-#<n>`, a bare task
# id `T<nnn>`, and "since v<n>" / "as of v<n>" (any case). Fenced code blocks and `backtick
# spans` are exempt. The script only forbids; it never requires a phrase.
#
# Usage:
#   tools/check-doc-voice.sh              # lints the built-in list below, from the repo root
#   tools/check-doc-voice.sh FILE...      # lints exactly these files instead
#
# Prints one `file:line:token` per hit; exits 1 on any hit, 0 when clean.
set -euo pipefail

# One path per line, relative to the repo root. Append a line when a doc is rewritten to comply.
DOCS=(
  CONTRIBUTING.md
  SECURITY.md
)

if [ "$#" -gt 0 ]; then
  files=("$@")
else
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  files=("${DOCS[@]}")
fi

hits=0
for file in "${files[@]}"; do
  if [ ! -f "$file" ]; then
    echo "check-doc-voice: no such file: $file" >&2
    exit 2
  fi
  out=$(awk -v file="$file" '
    # Print every match of re in line as file:line:token; trim(token) strips boundary characters.
    function scan(line, re, mode,    s) {
      while (match(line, re)) {
        s = substr(line, RSTART, RLENGTH)
        if (mode == "task") { sub(/^[^T]*/, "", s); sub(/[^0-9]+$/, "", s) }
        print file ":" NR ":" s
        line = substr(line, RSTART + RLENGTH - (mode == "task" ? 1 : 0))
      }
    }
    /^[ \t]*(```|~~~)/ { fenced = !fenced; next }
    fenced { next }
    {
      line = $0
      gsub(/`[^`]*`/, "", line)
      scan(line, "SPEC F[0-9]+", "")
      scan(line, "STORY-[0-9]+", "")
      scan(line, "gh-#[0-9]+", "")
      scan(line, "(^|[^A-Za-z0-9])T[0-9][0-9][0-9]([^A-Za-z0-9]|$)", "task")
      scan(line, "[Ss][Ii][Nn][Cc][Ee] [Vv][0-9]+", "")
      scan(line, "[Aa][Ss] [Oo][Ff] [Vv][0-9]+", "")
    }
  ' "$file")
  if [ -n "$out" ]; then
    printf '%s\n' "$out"
    hits=1
  fi
done

exit "$hits"
