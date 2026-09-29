---
description: Generate executable BDD specs from STORIES.md via the bdd-specs skill. Stories-first, idempotent, pending by default.
argument-hint: [scope or story id, optional]
model: sonnet
---

# spec

Turn `docs/STORIES.md` into executable spec files using the **`bdd-specs`**
skill. `/plan` already does this for every story it writes. Run `/spec` on
its own when stories were added or edited afterward and need specs.

## Scope

- IN: generating spec files from finalized stories; helping author stories
  when none exist yet; reporting on existing specs.
- OUT: writing implementation code (`/build-loop`); changing requirements
  (`/sprint`); ordering tasks (`/plan`).

## Preflight (refuse early)

1. **Require something to spec against.** Either of these will do:
   - `docs/BRIEF.md`, filled in and approved (from `/sprint`), or
   - `docs/PROJECT.md` plus `docs/SPEC.md`, filled in (from `/explore`
     and `/design`).

   If neither is there, or they're stubs full of `TODO`, **refuse**:
   `Nothing to spec against. Run /sprint first.`
2. **Stories check.** If `docs/STORIES.md` is missing or empty, do NOT refuse —
   instead, **help create them**: invoke the `user-stories` skill to author
   `docs/STORIES.md` from SPEC.md before continuing.

## Behavior

1. Read `docs/BRIEF.md` (or `docs/PROJECT.md` and `docs/SPEC.md`),
   `docs/STORIES.md`, `docs/ARCHITECTURE.md` (for context), and any
   existing spec files
   (e.g. `**/*.Tests/**/*.cs`, `tests/**/*.spec.ts`, `specs/**/*.ts`,
   `**/__tests__/**`).
2. **If specs already exist for a story, LEAVE THEM ALONE.** Do not
   overwrite, edit, rename, or reorganize. Report what was found and tell the
   user you can overwrite on explicit request.
3. For stories with **no corresponding spec file**, delegate to the
   `bdd-specs` skill to generate them. Pass the story id(s) so output aligns
   1:1 with `docs/STORIES.md` (Story → Feature, AC → Scenario, each Then →
   one Specification).
4. **Every newly generated spec is pending by default** — use the runner's
   skip / todo / pending marker (`[Fact(Skip = "pending — not yet implemented")]`
   for xUnit; `it.todo` / `it.skip` / `test.todo` for Jest). They must not
   accidentally pass; they must be visibly *pending* until a builder
   activates them at the start of its task.
5. Specs must align to stories, but **names should read naturally**:
   - **File name:** a readable slug from the story title — no
     `story-NNN` prefix. e.g. `atomic-create-user-order-entitlements.spec.ts`
     (TS) or `AtomicCreateUserOrderEntitlements.cs` (C#).
   - **Outer block (describe / Feature class):** the human-readable
     feature, no id. e.g.
     `describe("Feature: atomic create of user, order, entitlements", …)`.
   - **Traceability:** put a single comment at the top of the file with the
     story id, e.g. `// STORY-007 — Atomic create of user, order, entitlements`.
   That comment is the only place the id appears.
6. Never invent acceptance criteria. If a story is underspecified, stop and
   send the user back to refine it via the `user-stories` skill.

## Produce

- **New spec files** for stories without existing coverage, all in a pending
  state. Location follows project convention (the `*.Tests` project for C#;
  `tests/`, `specs/`, or `__tests__/` for TS); ask once if ambiguous.
- **No edits** to specs that already exist. Period.
- **docs/MEMORY.md**: append a single dated entry summarizing what was
  generated this run (story ids covered) and what was skipped (story ids
  already had specs).

## Hand off

Report:
- Stories with **specs created** (count + ids).
- Stories **skipped** because specs already exist (count + ids + file paths).
- Stories **not specced** because they were underspecified (count + ids).
- One line: `Existing specs were not modified. Ask explicitly if you want
  them overwritten.`

Then:
`Suggested next: /build-loop docs/PLAN.md — specs are pending and ready to turn green.`
