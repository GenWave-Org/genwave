---
description: Get a PR ready to merge into main — checks, sync, conflicts — then hand Dean the exact merge command. Never merges itself.
argument-hint: [PR number or branch (defaults to the current branch's PR)]
model: sonnet
---

# git-merge

Bring a PR to the edge of `main` and stop there. Merging, tagging,
releasing, and pushing `main` are Dean's, per action (`git-workflow` law
4). The merge-guard hook (`.claude/hooks/merge-guard.sh`) blocks the
merge command anyway, so this command prepares it and hands it over.

## Preflight

1. `gh auth status`. Refuse if not logged in.
2. **Resolve the PR.** `$ARGUMENTS` as a number or branch; else the
   current branch's PR (`gh pr view --json number,state,headRefName,baseRefName`).
   No PR → stop and suggest `/git-pr`.
3. **Still open?** `state` must be `OPEN`. Merged or closed → say so and
   stop. (Branches auto-delete on merge; never push to a merged branch.)
4. **Clean tree.** `git status -s`. Uncommitted changes on the PR branch →
   stop and suggest `/git-commit`. Never stash or discard on your own.

## Readiness checks

Run in parallel and report each:

1. **Checks:** `gh pr checks <N>`. Any red → stop and report which. A lone
   red matching a known flake in memory may be rerun with
   `gh run rerun <id> --failed`; say that's what you did.
2. **Mergeable:** `gh pr view <N> --json mergeable,mergeStateStatus`.
   `CONFLICTING` → rebase the branch onto `origin/main`, resolve, re-run
   the tests, `git push --force-with-lease`, then re-check. Never
   `--force` without `-with-lease`.
3. **Behind main:** `BEHIND` → same rebase path as above.
4. **Scope:** `gh pr diff <N> --name-only`. Flag anything that looks
   swept in (scratch, `docs/`, generated `next-env.d.ts`).

## Hand off

Report the PR URL, check status, and anything flagged. Then ask on its own
line, unmistakably:

> ⚠️ **Permission to merge PR #N to main?** Run it yourself:
> `! gh pr merge <N> --merge --admin --delete-branch`

Do **not** run the merge, even if Dean says yes in chat. The hook refuses
it; hand him the command.

## Rules

- Never merge, tag, release, or push `main`.
- Never force-push `main`. `--force-with-lease` on the PR branch only,
  after a rebase.
- Never delete a branch yourself; `--delete-branch` in Dean's command
  handles it.
