#!/usr/bin/env bash
# tools/check-seam-index.sh — SPEC F105.6 / STORY-294 / PLAN T217 guard.
#
# SEAMS.md's own header says "Generated. Never hand-edit." — this is the enforcement half: rebuild
# it from the composition root's LIVE DI registrations and byte-diff the result against what's
# actually on disk (the catalog `index.json` convention: rebuild the generated artifact, diff
# against committed, fail on drift). A new or changed seam shipped without a regenerated index is
# a red check, not a review catch.
#
# Read-only: the generator is given an explicit output-path argument (STORY-294 review, F2 —
# tools/SeamIndexGenerator/Program.cs's optional `args[0]`) pointing at a scratch file OUTSIDE the
# repo, never at the tracked SEAMS.md. Only that scratch copy is diffed against the tracked file.
# The tracked file is never opened for writing on ANY path through this script — success, failure,
# or a mid-run SIGTERM (the EXIT trap only ever removes the scratch file).
#
# CRITICAL — same-job constraint (T216 reviewer carry-forward): the generator's content-root
# discovery goes through WebApplicationFactory<Program>'s MvcTestingAppManifest.json, which the
# BUILD bakes with the build machine's absolute source paths (tools/SeamIndexGenerator/Program.cs,
# tests/GenWave.Host.Tests/Support/SeamCompositionSnapshot.cs explain the mechanism). This script
# must therefore be built and run in the SAME job, SAME workspace checkout that built it — a
# cached-artifact restore or a second CI job (fresh checkout, different runner) is not guaranteed
# to resolve the content root. ci.yml runs this between the Build and Test steps of the build-test
# job — before Test, specifically, because Story294's own byte-diff fact (no Category trait) runs
# inside the suite too and would abort the job on a stale SEAMS.md before this step ever got a
# chance to run.
#
# Usage: tools/check-seam-index.sh
# Exit:  0 — SEAMS.md already matches a fresh generation.
#        1 — SEAMS.md is stale (or missing); the diff and the fix command are printed.
set -euo pipefail
cd "$(dirname "$0")/.."

seams="SEAMS.md"

if [[ ! -f "$seams" ]]; then
  echo "❌ $seams is missing from the repo root." >&2
  echo "Fix: dotnet run --project tools/SeamIndexGenerator --configuration Release" >&2
  exit 1
fi

fresh=$(mktemp)
trap 'rm -f "$fresh"' EXIT

dotnet run --project tools/SeamIndexGenerator --configuration Release -- "$fresh" >/dev/null

if diff -q "$seams" "$fresh" >/dev/null; then
  echo "✅ $seams matches a fresh generation."
  exit 0
fi

echo "❌ $seams is stale — it does not match a fresh generation. Diff:" >&2
diff -u --label "SEAMS.md (committed)" --label "SEAMS.md (freshly generated)" "$seams" "$fresh" >&2 || true
echo "" >&2
echo "Fix: dotnet run --project tools/SeamIndexGenerator --configuration Release, then commit SEAMS.md. (SPEC F105.6)" >&2

exit 1
