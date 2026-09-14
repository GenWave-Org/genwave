---
name: git-workflow
description: >-
  The one Git policy for this repo: GitHub + gh CLI, short-lived branches
  only (never main), explicit staging, Conventional Commits with gh-#N
  refs, no attribution trailers, and merge/tag/release/push-main reserved
  for Dean. Use whenever you commit, branch, push, open a PR or issue, or
  are asked how to structure any of those. Other commands and agents
  reference this file instead of restating it.
---

# Git workflow (GitHub + `gh`)

🎯 Commits and branches should read clearly weeks later. Direct, specific,
no filler. Remote work (PRs, issues, checks) goes through the `gh` CLI
against `GenWave-Org/genwave`; branch protection requires a review, so
nothing lands on `main` without a PR.

## 🔒 The five laws

1. **Never commit to `main`.** No trunk lane, not even for one-liners.
   On `main`? Stop. `git checkout -b <type>/<slug>`. Then work.
2. **Stage explicitly.** `git add <path> <path>`; never `git commit -am`
   or `git add -A`. A commit contains one task's files, nothing swept in
   (scratch, PLAN.md ticks, generated `next-env.d.ts`).
3. **No attribution trailers.** No `Co-Authored-By`, no `Claude-Session`,
   no claude.ai/code links — in commits, PR bodies, issues, or releases.
   The repo is public.
4. **Merge, tag, release, push-main are Dean's.** Open the PR, report the
   URL, stop. A PreToolUse hook (`.claude/hooks/merge-guard.sh`) enforces
   this for every subagent; the hook path falls back to `$PWD` when
   `CLAUDE_PROJECT_DIR` is unset or empty. If the guard blocks you, hand Dean
   the command instead.
5. **No `--force` to shared branches, no `--no-verify`, no `--amend` after
   push** unless Dean asks. `--force-with-lease` on your own PR branch
   after a rebase is fine.

## 🌿 Branches

`<type>/<short-slug>` — type ∈ `feat fix chore docs refactor test ci`.
Suffix the issue number when there is one.

```
feat/sponsors-wizard-714
fix/bed-duck-relative-746
chore/toolkit-model-pins-749
```

Push early: `git push -u origin HEAD`. Open the PR early, draft is fine.

## ✍️ Commits — Conventional Commits, kept honest

```
<type>(<scope>): <imperative summary, lower-case, no period>

<body — what changed and why, wrapped ~72 cols>

Refs gh-#123          # or: Closes #123 (auto-closes on merge)
```

- Summary ≤ 72 chars, imperative (`add`, not `added`), scope when obvious
  (`fix(ads): …`, `feat(admin-ui): …`, `fix(toolkit): …`).
- Body whenever the diff doesn't explain itself: what + why, trade-offs,
  alternatives rejected. Never a line-by-line restatement.
- Build-loop tasks: `<type>(<scope>): T<n> <what>` so the PLAN maps to
  history.
- `BREAKING CHANGE: <what breaks, how to migrate>` footer for breakages.

Bad: `fix stuff`, `WIP`, `Update Streamer.cs`, `Added some changes`.

## 🔀 Pull requests

```
gh pr create -R GenWave-Org/genwave -t "<conventional title>" -F body.md
```

Body shape (emoji headings, terse):

```markdown
## 🔧 What
- headline change
- next
- caveats / non-changes

## 🧪 How to verify
- [ ] concrete check, ideally a command

## ⚠️ Risk / rollback
one line; "low, git revert" when true

Closes #<n>
```

After opening: report the URL and stop. Merge conflicts on your PR:
rebase onto `origin/main`, resolve, `git push --force-with-lease`.

## 🐛 Issues

```
gh issue create -R GenWave-Org/genwave -t "<title>" -l "<labels>" -F body.md
```

Labels from the existing set: `bug enhancement documentation P0 P1 P2 P3
demo`. Body: **problem** (what's wrong, evidence), **fix** (concrete),
**source** (spec section, memory, PR). File into the matching GitHub
Project at triage.

## 🚦 Sanity

- Re-check the diff for secrets (`.env*`, tokens, keys, real connection
  strings) before every commit.
- Never commit build output or deps; `.gitignore` covers `bin/ obj/
  node_modules/ dist/ docs/`.
- Unexpected untracked files in `git status` = someone's WIP. Ask before
  staging or reverting.
- Clean tree before starting a build loop; never build on uncommitted
  changes.
