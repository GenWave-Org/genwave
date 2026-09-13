---
description: Stage the current changes explicitly and commit with a Conventional Commit message on a non-main branch. Defers to the `git-workflow` skill.
argument-hint: [optional summary or scope hint]
---

# commit

Create one clean, well-described commit for the current working changes.
All policy lives in the **`git-workflow`** skill — read it first; this
command only sequences it.

## Behavior

1. **Inspect first.** In parallel: `git status`, `git diff` (staged +
   unstaged), `git log -n 10 --oneline` for the house style, and
   `git branch --show-current`.
2. **Never on `main`.** If the current branch is `main`, create
   `<type>/<slug>` from the diff (e.g. `fix/bed-picker-empty-718`) and
   switch to it before anything else. There is no trunk lane.
3. **Size check.** If the diff spans more than one concern, say so and
   propose splitting into separate commits; wait for the user.
4. **Secrets scan.** Re-read the diff for `.env*`, `*.pem`,
   `*credentials*`, tokens, real connection strings. Any hit: refuse,
   name the file, stop.
5. **Stage explicitly.** `git add <path> …` for exactly the files that
   belong to this change. Never `-am`, never `-A`. Leave out generated
   files (`next-env.d.ts`, tsconfig rewrites from `next build`) and
   anything that is someone else's WIP.
6. **Write the message** per `git-workflow`: Conventional Commit summary,
   a body that says what and why, `Refs gh-#N` / `Closes #N` footer.
   **No trailers** — no `Co-Authored-By`, no `Claude-Session`.
7. **Commit via HEREDOC** (`git commit -F - <<'EOF' … EOF`) so formatting
   survives. If a hook fails, fix the cause and make a NEW commit — never
   `--amend` unless asked.
8. **Verify** with `git status` and `git log -1`.

## Rules

- Never push as part of `/git-commit` unless the user asks.
- Never `--no-verify`, `--no-gpg-sign`, or force-push.
- Clean tree → say so and stop; no empty commits.

## Hand off

Report: branch, files staged, summary line, new commit SHA.
