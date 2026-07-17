---
name: git-workflow
description: >-
  Git workflow conventions for a Gitea-hosted repo using plain git (no
  gh/tea CLI): writing clear conventional commits, choosing trunk-based
  vs short-lived branch flow based on the size of the change, and
  preparing pushes for PRs/issues that are opened in the Gitea web UI.
  Use whenever the user wants to commit, branch, push, or asks how to
  structure any of those. Keep it light — direct, detailed messages,
  no ceremony.
---

# Git workflow (plain git + Gitea)

The point: write commits and branches that read clearly weeks later.
Be direct, be specific, skip the filler. All remote collaboration
(PRs, issues, reviews) happens in the **Gitea web UI** — the CLI side
is plain git only: branch, commit, push.

## Branching — when to branch, when not

**Trunk-based (commit straight to `main`)** when:
- Never, this is prohibited

**Short-lived branch** when:
- Always, we live on short-lived branches exclusively

**If you're working on main. Stop. Switch. Now.**

Branch naming: `<type>/<short-slug>` — type is one of
`task`, `feat`, `fix`, `chore`, `docs`, `refactor`, `test`. Examples:

```
feat/order-fulfillment-tx
fix/signature-verify-header-case
chore/bump-npgsql
```

Keep branches short-lived. Push early (`git push -u origin HEAD`) and
open the PR in Gitea early, even as a draft/WIP.

## Commits — Conventional Commits, kept honest

Format:

```
<type>(<optional scope>): <imperative summary, lower-case, no period>

<body — what changed and why, wrapped at ~72 cols>

<footer — refs, breaking changes>
```

**Types:** `task`, `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `perf`,
`build`, `ci`, `revert`.

**Summary line rules:**
- 50 chars or fewer when possible, hard cap 72.
- Imperative mood (`add`, not `added`/`adds`).
- No trailing period.
- Scope is optional; use it when there's an obvious area
  (`feat(cadence): …`, `fix(streaming): …`).

**Body rules:**
- Always include one if the change isn't self-evident from the diff.
- Explain **what** changed and **why** — never restate the diff line by
  line. Mention trade-offs or alternatives rejected if relevant.
- One blank line between summary and body.
- Reference issues with `Refs gitea-#123` or `Closes gitea-#123` — Gitea links and
  auto-closes these just like GitHub does.

**Footers:**
- `Closes #N` to auto-close a Gitea issue on merge.
- `BREAKING CHANGE: <what breaks, how to migrate>` for breakages.
- `Co-Authored-By:` lines when pairing.

**Example — good:**

```
feat(streaming): reconnect to icecast with capped backoff

A dropped Icecast connection previously killed the output until a
manual restart. The streamer now:

  1. detects the dropped socket on push failure
  2. retries with exponential backoff capped at 30s
  3. logs each attempt with the mount point and attempt count

Silence is pushed to the buffer during reconnect so listeners hear
dead air no longer than one buffer length.

Refs STORY-014
```

**Example — bad (don't):**

- `fix stuff`
- `WIP`
- `Update Streamer.cs` (says nothing the diff doesn't)
- `Added some changes and refactored a couple of things` (vague +
  past tense)

## Issues — drafted here, opened in Gitea

A useful issue answers: **what's wrong / what's wanted, what's the
context, what does done look like.** Draft the body in this shape and
paste it into the Gitea new-issue form:

```markdown
## Context
<one paragraph: where this came from, why it matters, link to story id
or spec section if relevant>

## What we want
<the concrete change or behavior — bulleted is fine>

## Acceptance criteria
- [ ] <observable outcome 1>
- [ ] <observable outcome 2>

## Notes
<optional — links, screenshots, related PRs, anything that helps the
person picking this up>
```

Add labels/assignee in the Gitea form if the repo uses them.

**For the GenWave project, all issues _must_ use the `genwave-2.0` tag or they will not be picked up and addressed.**

## Pull requests — pushed here, opened in Gitea

A useful PR answers: **what changed, why, how to verify, what could
go wrong.**

1. Make sure the branch is pushed: `git push -u origin HEAD`.
2. Open the PR in the Gitea UI (Gitea also prints a direct
   create-PR link in the `git push` output — use it).
3. Title follows Conventional Commits, same as a commit summary.
4. Body in this shape:

```markdown
## Summary
- <bullet 1 — the headline change>
- <bullet 2 — the next>
- <bullet 3 — caveats / non-changes>

## Why
<one paragraph: the motivation. Link the story or issue.>

## How to verify
- [ ] <step 1 — a concrete check, ideally a command, e.g. `dotnet test`>
- [ ] <step 2>
- [ ] <step 3>

## Risk / rollback
<what could go wrong, how to revert. "low risk, revert via git revert"
is fine when true.>

Closes #<issue-number, if any>
```

## Sanity rules

- Don't push to `main` with `--force`. Ever. In fact, don't push to `main`. Ever.
- Don't `--no-verify` to skip hooks unless explicitly asked.
- Never commit secrets — re-check the diff for `.env`, tokens, keys,
  connection strings with real passwords.
- Never commit build output or dependencies — confirm `.gitignore`
  covers `bin/`, `obj/`, `node_modules/`, `dist/`.
- If `git status` shows untracked files you didn't expect, investigate
  before staging — could be the user's WIP.
