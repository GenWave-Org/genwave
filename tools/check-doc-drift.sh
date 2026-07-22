#!/usr/bin/env bash
# tools/check-doc-drift.sh — gh-#77: fail when DEPLOYMENT.md drifts from compose.demo.yaml.
#
# compose.demo.yaml is the source of truth for the demo box's concrete values; DEPLOYMENT.md
# restates them in prose and pin-bump commits touch only the compose file, so the doc drifts
# silently (the 3072M→6144M fence bump shipped two releases before the doc caught up).
#
# The checked pairs are an EXPLICIT allowlist (one block per value below) — a short list that
# names each value beats a clever generic differ. When DEPLOYMENT.md starts stating a new
# compose value, add a block for it here in the same PR.
set -euo pipefail
cd "$(dirname "$0")/.."

fail=0
drift() { echo "❌ doc-drift: $*" >&2; fail=1; }
ok()    { echo "✅ $*"; }

# --- Pair 1: ollama resource fence → "N CPU / MGB" in the DJ-brain section. --------------
# compose expresses the fence as cpus: "1.0" / memory: 6144M; the doc says "1 CPU / 6GB".
cpus_raw=$(grep -E '^[[:space:]]+cpus:' compose.demo.yaml | head -1 | grep -oE '[0-9]+(\.[0-9]+)?')
mem_mb=$(grep -E '^[[:space:]]+memory:' compose.demo.yaml | head -1 | grep -oE '[0-9]+')
cpus_int=${cpus_raw%%.*}
mem_gb=$((mem_mb / 1024))
expected_fence="${cpus_int} CPU / ${mem_gb}GB"
if grep -qF "$expected_fence" DEPLOYMENT.md; then
  ok "fence: DEPLOYMENT.md states \"$expected_fence\" (compose: cpus=$cpus_raw, memory=${mem_mb}M)"
else
  doc_fence=$(grep -oE '[0-9]+ CPU / [0-9]+GB' DEPLOYMENT.md | head -1 || true)
  drift "fence: doc says \"${doc_fence:-<not found>}\", compose says \"$expected_fence\" (cpus=$cpus_raw, memory=${mem_mb}M)"
fi

# --- Pair 2: ollama-init model name → stated verbatim in the doc. ------------------------
model=$(sed -n 's/.*"ollama", "pull", "\([^"]*\)".*/\1/p' compose.demo.yaml | head -1)
if [[ -z "$model" ]]; then
  drift "model: could not extract the ollama-init pull model from compose.demo.yaml — update this script's extraction"
elif grep -qF "\`$model\`" DEPLOYMENT.md; then
  ok "model: DEPLOYMENT.md states \`$model\` (compose ollama-init pull)"
else
  drift "model: doc never states \`$model\`, compose ollama-init pulls \"$model\""
fi

# --- Pair 3: the two home-v* image pins agree with each other. ---------------------------
# Not a doc pair, but the same drift class: a half-bumped release (api bumped, icecast not)
# has no other guard. All home-v tags in compose.demo.yaml must be identical.
mapfile -t pins < <(grep -oE 'home-v[0-9]+\.[0-9]+\.[0-9]+' compose.demo.yaml | sort -u)
if [[ ${#pins[@]} -eq 0 ]]; then
  drift "pins: no home-v* image tags found in compose.demo.yaml — update this script's extraction"
elif [[ ${#pins[@]} -gt 1 ]]; then
  drift "pins: compose.demo.yaml carries mixed image pins: ${pins[*]} — a half-bumped release"
else
  ok "pins: all compose.demo.yaml images pinned to ${pins[0]}"
fi

if [[ $fail -ne 0 ]]; then
  echo "" >&2
  echo "DEPLOYMENT.md restates compose.demo.yaml values in prose — fix the doc (or the compose" >&2
  echo "file) so they agree, in the same PR as the value change. See gh-#77." >&2
fi
exit $fail
