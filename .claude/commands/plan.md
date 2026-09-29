---
description: Slice the brief (or SPEC) into stories, an ordered task list, and pending specs. Owns STORIES.md + PLAN.md.
argument-hint: [scope to plan, optional]
model: fable
---

# plan

> 🎯 **Design for change.** Slice tasks so each one touches one seam.
> A task that edits five unrelated files is a coupling smell. Re-slice
> before you ship it to `/build-loop`.

Turn the design into something the build process can execute. This command
**transforms** its input. It does not re-elicit requirements.

## Scope

- IN: derive user stories, order tasks into a dependency-aware checklist,
  mark what can run concurrently, then generate pending specs from the
  finalized stories.
- OUT: gathering requirements (`/sprint`, or `/explore` and `/design`),
  building (`/build-loop`).

## Preflight

1. **Find the input.** Use the first of these that is filled in:
   - `docs/BRIEF.md` (from `/sprint`). "What they'll be able to do" is the
     behavioral spec. "How we'll build it" is the design.
   - `docs/SPEC.md` plus `docs/PROJECT.md` (from `/design` and `/explore`).

   If neither exists, or what's there is a stub full of `TODO`, **stop**.
   There's nothing to plan. Point the human at `/sprint`.
2. Read `docs/ARCHITECTURE.md` and `docs/MEMORY.md`.
3. Read existing `docs/STORIES.md` / `docs/PLAN.md`. Re-entrant: reconcile
   and extend. Never silently drop or renumber completed (`- [x]`) tasks.

## Stories

Delegate to the **`user-stories`** skill to produce or refine
`docs/STORIES.md`. It formats Story→Feature with Given/When/Then so
`bdd-specs` can consume it. Don't hand-roll the format, and tell it to
draft with `> ASSUMPTION:` lines in place of interviewing.

## Extras

Every item the human approved under "What you didn't ask for" in the brief
becomes its own task, tagged `delight:`.

- One extra, one task, one commit.
- It comes **after** the task it decorates.
- **Nothing depends on a `delight:` task.** That's what makes it safe to
  strike later.
- Extras the human struck at the gate do not appear. Don't add new ones
  here. That's the product owner's call, made in the brief.

## Specs (always, no asking)

Once `docs/STORIES.md` and `docs/PLAN.md` are written, delegate to the
**`bdd-specs`** skill to generate spec files from the stories.

**Every generated spec is pending** (`[Fact(Skip = "pending — …")]` for
xUnit; `it.todo` / `it.skip` / `test.todo` for Jest). The builder activates the specs for its task
when it starts that task, watches them fail, then makes them pass. That
keeps the suite green between tasks, so a red test always means something
broke.

`/spec` runs this same step on its own, for stories added later.

## Interview

- **Called from `/sprint`:** none. The human already approved the brief.
  Make the slicing calls yourself and log anything non-obvious in
  `docs/MEMORY.md`.
- **Run directly:** light confirmation, 3 to 5 questions, batched:
  1. Here's how I sliced it into stories. Anything mis-cut or missing?
  2. Priority and sequence right? What's the first shippable slice?
  3. Any task I marked parallel that shares state?
  4. Hard external dependencies that gate ordering?

## Produce

- **docs/STORIES.md** (owned, via `user-stories` skill).
- **docs/PLAN.md** (owned): a checklist `build-loop` can execute.
  - every task is `- [ ]`, top-to-bottom in dependency order;
  - each task line carries: a short id, the story it implements
    (`story:`), explicit `depends-on:` ids, and a `parallel-group:` tag for
    tasks with no ordering between them;
  - extras carry `delight:` and the one-line why from the brief;
  - one task = one reviewable, committable unit of work;
  - no task references a file or decision that isn't in the brief,
    ARCHITECTURE.md, or SPEC.md.
- **Wire tasks are first-class.** For every feature that touches a deployed
  entry point (ASP.NET controller action / minimal API endpoint, a
  `BackgroundService`, an exported handler or route file), include an explicit `wire: <feature> into <entry>` task whose acceptance is
  a real request through the production binary producing the spec's side
  effect. A task whose only acceptance criterion is "unit tests pass" is not
  allowed for code on a production path. That's how stubs ship.
- **Pending specs** (via `bdd-specs` skill). Not optional.
- **docs/MEMORY.md**: append dated entries for sequencing decisions that
  weren't obvious (why X blocks Y, why a slice was deferred).

## Hand off

Summarize: N stories, M tasks (D of them `delight:`), S spec files
generated, the first parallel group, then:
`Suggested next: /build-loop docs/PLAN.md (specs are in place and pending).`
