---
description: Open a GitHub issue with a clear, detailed body via `gh issue create`. Uses the `git-workflow` skill.
argument-hint: [short title or topic]
model: sonnet
---

# issue

Create a GitHub issue using the **`git-workflow`** skill's template — direct,
detailed, no filler.

## Behavior

1. **Preflight.**
   - Ensure `gh` is installed and authenticated (`gh auth status`).
   - Confirm we're in a repo with a GitHub remote (`gh repo view`).
2. **Gather context.** If the user gave a short title or topic, expand it.
   Read `docs/STORIES.md`, `docs/PLAN.md`, or `docs/SPEC.md` if the topic
   maps to one of them — link the story or task id in the body.
3. **Draft per the `git-workflow` skill issue template:**
   - **Context** — where it came from, why it matters, links.
   - **What we want** — the concrete change or behavior.
   - **Acceptance criteria** — observable, checkbox list.
   - **Notes** — anything that helps the next person.
4. **Title** — Conventional-Commit style
   (`feat(scope): …`, `fix(scope): …`, `chore: …`). Lowercase, imperative,
   no period.
5. **Show the user the draft title + body** before creating. Confirm
   labels and assignee.
6. **Create** with a HEREDOC body:
   ```bash
   gh issue create -R GenWave-Org/genwave --title "<title>" --body "$(cat <<'EOF'
   ...
   EOF
   )"
   ```
   Labels come from the existing set (`bug enhancement documentation P0
   P1 P2 P3 demo`); add `--assignee` / `--milestone` only when known.
7. **File it into the matching GitHub Project** (#3–#9) at triage.

## Rules

- Never invent acceptance criteria — pull them from the source story/spec
  or ask the user.
- Never include secrets, paths to local-only files, or claude.ai/code links.
- If `gh` isn't authenticated, stop and tell the user to run
  `gh auth login`.

## Hand off

Report the issue URL returned by `gh`.
