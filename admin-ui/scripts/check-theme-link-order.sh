#!/usr/bin/env bash
#
# Theme stylesheet link-order guard (PLAN T170; the T162/T168 carry-forward).
#
# The admin root layout renders <link rel="stylesheet" href="/api/theme.css" precedence="theme">.
# React 19 places a NEWLY-seen precedence value as a LATER group in <head>, so the composed theme
# sheet must land AFTER Next's own stylesheet group (data-precedence="next", carrying globals.css)
# for it to override the static default tokens. If a future Next/React upgrade reordered those
# groups — or Next adopted a precedence value that sorts after "theme" — admin would SILENTLY lose
# light-mode theming (the majority case, least likely to be noticed in a dark-themed dev session).
#
# Jest cannot reproduce Next's runtime CSS-precedence injection (theme-stylesheet-link.spec.ts only
# pins that the `precedence` prop is PRESENT, not that the served order is correct), so this asserts
# the real served <head> order against a production `next start`. Verified by hand at T162/T170.
#
# Run from admin-ui/:  bash scripts/check-theme-link-order.sh
set -euo pipefail
cd "$(dirname "$0")/.."

PORT="${THEME_LINK_ORDER_PORT:-3987}"
LOG="$(mktemp)"

# Reuse an existing production build (CI runs `npm run build` just before this); build only if none.
[ -d .next ] || npm run build

npx next start -p "$PORT" >"$LOG" 2>&1 &
NEXT_PID=$!
cleanup() { kill "$NEXT_PID" 2>/dev/null || true; }
trap cleanup EXIT

# Wait for readiness without a shell `sleep` (curl retries the refused connection itself), then
# fetch the unauthenticated /login page — it is wrapped by the same root layout, so it carries the
# theme <link> without needing a session or a live backend (a 404 on /api/theme.css does not change
# the <head> markup this guard inspects).
HTML="$(curl -s --retry 60 --retry-delay 1 --retry-connrefused --retry-all-errors "http://127.0.0.1:${PORT}/login" || true)"

next_pos="$(printf '%s' "$HTML" | grep -boE 'data-precedence="next"'  | head -1 | cut -d: -f1)"
theme_pos="$(printf '%s' "$HTML" | grep -boE 'data-precedence="theme"' | head -1 | cut -d: -f1)"

if [ -z "$next_pos" ] || [ -z "$theme_pos" ]; then
  echo "FAIL: precedence markers not found in the served <head> (next='${next_pos}' theme='${theme_pos}')."
  echo "      The theme stylesheet may no longer be precedence-managed — inspect app/layout.tsx's"
  echo "      <link href=\"/api/theme.css\" precedence=\"theme\">. Server log:"
  sed 's/^/        /' "$LOG" | tail -20
  exit 1
fi

if [ "$next_pos" -ge "$theme_pos" ]; then
  echo "FAIL: /api/theme.css (data-precedence=\"theme\" @byte ${theme_pos}) does NOT land after the"
  echo "      Next stylesheet group (data-precedence=\"next\" @byte ${next_pos}). Admin would render"
  echo "      globals.css's DEFAULT tokens instead of the active theme in LIGHT mode. A Next/React"
  echo "      upgrade likely changed precedence-group ordering (PLAN T168 carry-forward, T162 review)."
  exit 1
fi

echo "PASS: /api/theme.css (@byte ${theme_pos}) lands after the Next group (@byte ${next_pos}) — the"
echo "      composed theme overrides globals.css; admin light-mode theming is intact."
