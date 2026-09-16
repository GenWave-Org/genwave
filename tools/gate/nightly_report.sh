#!/usr/bin/env bash
# nightly_report.sh integration=<result> chaos=<result> [run=<url>] — SPEC F180.4 / STORY-449 /
# PLAN T499.
#
# Turns the nightly workflow's two job results into exactly one open `nightly-red` issue, kept
# in sync run over run. Called from nightly.yml's own `report` job (needs: [integration, chaos],
# if: always()) as:
#   tools/gate/nightly_report.sh integration="$INTEGRATION_RESULT" chaos="$CHAOS_RESULT" run="$RUN_URL"
# (the job passes needs.*.result and the run URL through `env:`, never inlined into the shell line).
# `run=` is optional — when omitted (as Story449's own specs do), the run URL is built from
# GITHUB_REPOSITORY + GITHUB_RUN_ID instead (the same two env vars every Actions job already
# carries). A result counts as RED when it is anything other than `success` (Actions job results
# are success/failure/cancelled/skipped — a cancelled or skipped job is still not a clean night).
#
# Four outcomes:
#   1. First red, nothing open  -> gh issue create --title "Nightly red since <date>"
#                                   --label nightly-red --body <failing jobs + run url>
#   2. Red again, already open  -> gh issue comment <n> --body <failing jobs + run url>
#   3. Green, an issue is open  -> gh issue comment <n> --body "Nightly green — <url>", then
#                                   gh issue close <n> (comment BEFORE close, so the closing
#                                   comment is the last thing anyone reads on the thread)
#   4. Green, nothing open      -> no `gh issue` call beyond the lookup — quiet by design, this
#                                   is the common case and should never notify anyone
#
# The nightly-red label is created on demand (`gh label create ... --force`), immediately before
# the one call that needs it (outcome 1) — a fork or a fresh repo needs no manual label-setup
# step, and the green/quiet path (outcome 4) makes no `gh issue`/`gh label` call beyond the lookup.
#
# Exercised by Story449 (tests/GenWave.Host.Tests/Specs/Story449_NightlyWorkflow.cs) against a
# scripted `gh` (GhStub) that logs every argv line and answers `issue list` from
# GATE_STUB_OPEN_ISSUE — never the real GitHub API. Requires `gh` (authenticated via GH_TOKEN,
# same as every other step in the report job) and `jq`.

set -euo pipefail

# Parameter expansion, not `basename` — Story449's scripted PATH (ScriptProcess.MakeBinDir)
# carries only what each spec actually needs; this needs nothing extra.
prog="${0##*/}"

usage_error() {
  echo "$prog: $1" >&2
  echo "usage: $prog integration=<result> chaos=<result> [run=<url>]" >&2
  exit 2
}

INTEGRATION=""
CHAOS=""
RUN_URL=""

for arg in "$@"; do
  case "$arg" in
    integration=*) INTEGRATION="${arg#integration=}" ;;
    chaos=*) CHAOS="${arg#chaos=}" ;;
    run=*) RUN_URL="${arg#run=}" ;;
    *) usage_error "unknown argument: $arg" ;;
  esac
done

[ -n "$INTEGRATION" ] || usage_error "missing integration=<result>"
[ -n "$CHAOS" ] || usage_error "missing chaos=<result>"

# Build the run URL ourselves only when the caller didn't hand us one — GITHUB_REPOSITORY/
# GITHUB_RUN_ID are only read here, so a caller that always passes run= (the real workflow does)
# never needs either set.
if [ -z "$RUN_URL" ]; then
  RUN_URL="https://github.com/$GITHUB_REPOSITORY/actions/runs/$GITHUB_RUN_ID"
fi

FAILED_JOBS=()
[ "$INTEGRATION" = "success" ] || FAILED_JOBS+=("integration")
[ "$CHAOS" = "success" ] || FAILED_JOBS+=("chaos")

# Find the (at most one) open nightly-red issue. Deliberately NOT `gh`'s own -q filter here: this
# script also runs against Story449's scripted `gh`, which answers the raw JSON array regardless
# of -q, so the number is pulled out with jq ourselves — the one path that behaves the same way
# against the real CLI and the stub.
OPEN_ISSUE_JSON="$(gh issue list --label nightly-red --state open --limit 1 --json number)"
OPEN_ISSUE_NUMBER="$(printf '%s' "$OPEN_ISSUE_JSON" | jq -r '.[0].number // empty')"

if [ "${#FAILED_JOBS[@]}" -eq 0 ]; then
  # Green.
  if [ -n "$OPEN_ISSUE_NUMBER" ]; then
    gh issue comment "$OPEN_ISSUE_NUMBER" --body "Nightly green — $RUN_URL"
    gh issue close "$OPEN_ISSUE_NUMBER"
  else
    echo "$prog: green, nothing open — quiet"
  fi
  exit 0
fi

# Red.
printf -v failed_jobs_str '%s, ' "${FAILED_JOBS[@]}"
failed_jobs_str="${failed_jobs_str%, }"
body="Nightly red: $failed_jobs_str failed — $RUN_URL"

if [ -n "$OPEN_ISSUE_NUMBER" ]; then
  gh issue comment "$OPEN_ISSUE_NUMBER" --body "$body"
else
  # The nightly-red label is created here, on demand, the first time it's needed — a fork (or a
  # fresh repo) needs no manual label-setup step. --force makes the create idempotent: on every
  # later red it just re-applies the same description/color rather than failing on "already
  # exists".
  gh label create nightly-red --description "The nightly workflow is red" --color B60205 --force
  gh issue create --title "Nightly red since $(date -u +%F)" --label nightly-red --body "$body"
fi
