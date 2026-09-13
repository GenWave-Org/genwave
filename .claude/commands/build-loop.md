---
description: Drive a PLAN.md to completion task-by-task — builder builds, reviewer gates, commit only on pass.
argument-hint: [path-to-PLAN.md]
---

# build-loop

> 🎯 **Design for change.** Each task's diff should be small, local, and
> behind a stable seam. The reviewer fails work that couples modules or
> bloats blast radius — builder should pre-empt that.

Execute the build plan in `$ARGUMENTS` (default: `docs/PLAN.md`) one task at a
time. You are the orchestrator (the lead thread). You do **not** write feature
code or review it yourself — you dispatch and gate.

## Scope

- IN: build each unchecked task, get it through review, commit it.
- OUT: planning, writing PLAN.md, authoring specs, refactor/refinement passes.
  This command **consumes** a plan; it does not author one.

## Preflight

1. Resolve the plan: use the path in `$ARGUMENTS`; else `docs/PLAN.md`; else
   **stop and ask** — do not infer a plan.
2. Read it. It must be a checklist of tasks (`- [ ]` / `- [x]`). If it has no
   checkboxes, stop and report — it is not a build plan.
3. Confirm a clean git working tree. If dirty, stop and report; do not build on
   top of uncommitted changes.
4. Confirm you are on a feature branch, not `main` (`git-workflow` skill:
   there is no trunk lane). If on `main`, stop and ask for the branch.
5. **Green baseline.** Run the full solution once:
   `dotnet test GenWave.sln --filter "Category!=Integration"` (Host suite
   alone on the dev box needs `-- xUnit.MaxParallelThreads=3`), plus
   `npm test` in `admin-ui/` when the plan touches UI. Record the counts.
   A red baseline stops the loop — report it, don't build on it.

## The loop

For each task still unchecked (`- [ ]`), in order, top to bottom:

1. **Build.** Dispatch a `builder` subagent (Agent tool,
   `subagent_type: builder`, model **sonnet** — alias, tracks the current
   generation). The brief contains: the exact task text, the relevant
   section of PLAN.md, the files it owns, the skills to invoke
   (`csharp-best-practices` for C#, `typescript-best-practices` for TS,
   `postgres-dba` for schema work), and the exact test command from
   Preflight step 5. The builder **does not commit**.

   **Brief laws** (each cost a review round before it became a law):
   - **Quote, don't paraphrase.** Paste the exact SPEC/STORIES/ARCHITECTURE
     text the task implements — DDL verbatim, F-numbers, Story numbers.
     A paraphrased schema is a wrong schema.
   - **Every citation is a testable claim.** Before dispatch, open the file
     and confirm the F-number / Story / spec name you cite exists and says
     what you claim. Stale citations send the builder down the wrong path.
   - **Name the full test command.** Always the whole solution, never a
     class or namespace filter. A filtered green hid failures in other
     classes more than once.

2. **Review.** Dispatch a `reviewer` subagent (Agent tool,
   `subagent_type: reviewer`, model **inherit** — it runs on the session
   model, so it is always ≥ the builder). It invokes the matching security
   skill (`security-api` for backend code, `security-web` for UI code) and
   `simplify`, reads the builder's diff, runs the full solution, and
   returns `PASS`, `PASS-WITH-NOTES`, or `FAIL` with specific findings.

3. **Recurse, capped.**
   - `PASS` → step 4.
   - `PASS-WITH-NOTES` → the notes are comment/doc/naming-only. **You** fix
     them (the orchestrator may edit comments, doc strings, and PLAN.md —
     nothing else), then step 4. No new build round.
   - `FAIL` → send the findings to a fresh `builder` for the same task and
     go back to step 2. **Three `FAIL`s on one task → stop the loop** and
     report to Dean: the task, all three rounds' findings, and what you
     think is stuck (brief, spec, or builder). Do not keep spinning; a
     seven-round task is a briefing problem, not a builder problem.
   Nothing reaches git history before a passing verdict.

4. **Smoke the entry point.** Before commit, build/start the production
   artifact and drive **one real request through the deployed entry point**
   (one `curl` against the running Kestrel endpoint via `dotnet run`, one
   request through the containerized service, one route fetch against the
   dev server — whatever applies to the deploy target). Fake adapters (DB,
   email, storage) are fine, but the request must travel through the mapped
   endpoint/handler, not an internal function. Assert the spec's side effect
   actually occurred. If the smoke fails, recurse to step 3 with a finding —
   unit-test green is not enough to ship.

   **Smoke laws:**
   - Teardown kills the listener by **port** (`ss -ltnp` → pid → `kill`),
     never by the subshell pid — that orphans the server and the next
     smoke fights it for the port.
   - `next build` rewrites `tsconfig.json` and `next-env.d.ts`. `git
     checkout -- admin-ui/tsconfig.json admin-ui/next-env.d.ts` before
     staging.
   - Approve-style endpoints need `If-Match` (a bare PUT gets 428).

5. **Commit.** On a passing verdict + smoke green, have the builder commit
   *only this task's files* with explicit `git add <path> …` — never `-am`
   or `-A`. Message per `git-workflow`: `<type>(<scope>): T<n> <what>`,
   body says what and why, no trailers. One commit per task.

6. **Check the box.** Before ticking, `grep -rn "pending: T<n>"` across
   `tests/` — a spec still marked pending for this task means the task is
   not done. No `Assert.Fail` stubs may ship. Then edit PLAN.md:
   `- [ ]` → `- [x]`, and commit that PLAN.md change with the task commit
   or immediately after.

7. Next task.

## Finish

When every box is checked: run the full solution once more, `git worktree
prune`, then report a summary (tasks completed, commits, round count per
task, anything still red). Architect / final design check is a separate
step — not part of this loop. Merging the branch is Dean's, per action.

## Rules

- Sequential, dependency-ordered. This is a pipeline, not a parallel team — use
  the Agent tool (subagents), not Agent Teams.
- Builder owns code; reviewer owns the gate; you own sequencing and the
  checkbox state. Never collapse these roles.
- Three `FAIL`s on a task stops the loop (step 3). Report; never commit
  degraded code to get past a gate.
- Scratch hygiene: anything you rsync for a subagent excludes
  `node_modules bin obj .git` — unfiltered scratch grew ~1k files per round.
- Doc and comment claims about boot/compose/runtime behaviour are testable
  facts; the reviewer checks them and so should the brief.
