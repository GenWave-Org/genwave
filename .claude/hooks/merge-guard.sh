#!/usr/bin/env bash
# .claude/hooks/merge-guard.sh — PreToolUse guard for Bash (gh-#751).
#
# MERGE RULE: merging, tagging, releasing, and pushing to main need Dean's
# explicit per-action consent. This hook makes that mechanical for every
# subagent. It reads the hook's stdin JSON, splits the command into
# segments (;  &&  ||  |  newline), and matches each segment's LEADING verb,
# so a grep/echo/sed that merely mentions "git tag" is not blocked.
#
# Exit 2 blocks the call and feeds stderr back to the model; exit 0 allows.
set -u
cmd=$(jq -r '.tool_input.command // empty' 2>/dev/null) || exit 0
[ -n "$cmd" ] || exit 0

refuse() {
  echo "refuse: '$1' — merge / tag / release / push to main needs Dean's per-action consent (MERGE RULE, gh-#751). Hand him the command instead." >&2
  exit 2
}

# Normalise separators to newlines, then judge each segment on its own.
printf '%s\n' "$cmd" | sed -E 's/(&&|\|\||;|\|)/\n/g' | while IFS= read -r seg; do
  # Strip leading whitespace, env assignments, and a leading `sudo`/`command`.
  seg=$(printf '%s' "$seg" | sed -E 's/^[[:space:]]+//; s/^([A-Za-z_][A-Za-z0-9_]*=[^ ]* +)*//; s/^(sudo|command) +//')
  case "$seg" in
    "gh pr merge"*)                       refuse "$seg" ;;
    "gh release create"*)                 refuse "$seg" ;;
    "git tag"*)
      case "$seg" in
        "git tag -l"*|"git tag --list"*|"git tag --contains"*|"git tag -n"*|"git tag") ;;
        *) refuse "$seg" ;;
      esac ;;
    "git push"*)
      case "$seg" in
        *" --tags"*|*" main"|*" main "*|*":main"*|*":refs/heads/main"*|*" --delete"*|*" -d "*) refuse "$seg" ;;
      esac ;;
  esac
done
# `while` runs in a subshell; propagate its exit status.
exit ${PIPESTATUS[2]:-0}
