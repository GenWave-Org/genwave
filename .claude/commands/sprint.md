---
description: The front door. One prompt in, one approval, then plan, build, review, and a product-owner acceptance pass. Owns docs/BRIEF.md.
argument-hint: <what you want built>
model: fable
---

# sprint

Take the request in `$ARGUMENTS` from a sentence to shipped code with **one
approval** from the human. You are the lead thread. You dispatch, merge,
and write the docs. You do not interview, and you do not write feature
code.

> 🎯 **Two kinds of rigor.** Rigor that costs tokens (review, specs, smoke
> tests) stays. Rigor that costs the human's attention (interviews,
> handoffs) goes. Guess well, show your work, and let them correct you.

## Scope

- IN: the brief, the one gate, then running `/plan` and `/build-loop`.
- OUT: long discovery. If the human wants to think a problem through
  before committing to anything, that's `/explore` and `/design`. They
  still work and nothing here requires them.

## Preflight

1. **Request.** If `$ARGUMENTS` is empty, ask what they want built. That's
   the only question you ask before the brief.
2. **Ground.** If `CLAUDE.md` or `docs/` is missing, run `/init` (Skill
   tool) and carry on. Don't send the human off to do it.
3. **Git.** If this isn't a git repo, run `git init`. If the working tree
   is dirty, stop and say so. The build loop won't run on top of
   uncommitted changes. On `main`, branch first (`feat/<slug>`, per
   `git-workflow`). There is no trunk lane.
4. **Size.** If the request is a typo or a one-spot fix, say so and
   suggest `/quick-fix`.
5. **Unfinished sprint.** If `docs/BRIEF.md` is approved and
   `docs/PLAN.md` still has unchecked tasks:
   - no new request: report where it stopped and resume `/build-loop`.
   - a new request: ask one question, finish the current sprint first or
     replace it.

## 1. Think (in parallel)

Dispatch both agents in a single message so they run at the same time.
Give each the request **in the human's exact words**.

- `product-owner` in **brief mode**: who it's for, what they'll be able to
  do, assumptions, extras, cuts.
- `architect`: approach, seams, data changes, decisions, risks, task
  slice.

If `docs/PROJECT.md` or `docs/SPEC.md` exist (from `/explore` or
`/design`), tell both agents to read them first. Those are decisions the
human already made. The agents build on them and don't reopen them.

## 2. Merge

Build the brief from `product-owner/templates/BRIEF.md`.

- The product owner decides **what** and **for whom**. The architect
  decides **how**. Where they disagree, put both positions in the brief in
  one line each and recommend one.
- **Check every extra against the delight budget** using the architect's
  report: no new dependency, no schema change, no new service or route,
  one task, removable. An extra that fails moves to **Next**. If you can't
  tell, it moves to **Next**.
- Merge the questions. Three at most. Drop any question whose answer you
  can find in the code or the docs.

## 3. The gate (the only one)

Show the brief in chat, opening with "Here's what we think." Keep it to a
page. Then ask for the approval:

- Approve as written.
- Strike any extra or accept any cut.
- Correct any assumption.
- Answer the questions, if there are any.

Use `AskUserQuestion` for the questions so "yes to all" is one click.
Apply what they say and do not ask again. After this, the human's next
job is to look at finished work.

## 4. Write it down

- **docs/BRIEF.md** (owned here). If one exists from an earlier sprint,
  move it to `docs/briefs/YYYY-MM-DD-<slug>.md` first.
- **docs/ARCHITECTURE.md**: add this sprint's decisions and data changes.
  Update only. Don't rewrite what's there.
- **docs/MEMORY.md**: append a dated line for each decision, with the why.
  Include struck extras and rejected cuts, so they don't come back.

## 5. Plan

Run `/plan` (Skill tool) against `docs/BRIEF.md`. Tell it this is a
sprint run so it skips its confirmation interview. It writes
`docs/STORIES.md`, `docs/PLAN.md`, and pending specs. Approved extras come
out as `delight:` tasks.

Then commit the specs so the tree is clean (`docs/` is gitignored; the
brief, stories, and plan stay local):
`test: pending specs, <sprint name>`

## 6. Build

Run `/build-loop docs/PLAN.md` (Skill tool). It builds and reviews task by
task, then hands the finished work to the product owner for the
acceptance pass.

## 7. Report

Push the branch and open the PR (`/git-pr`). Then report, short: what
shipped, what the extras were, what the product owner sent back for
polish, what's under **Next**, the command to run it (`./launch.sh`), and
the PR URL. Merging is Dean's; hand it over with `/git-merge`.

## Rules

- **Running other commands.** Use the Skill tool for `/init`, `/plan`, and
  `/build-loop`. If it can't run one, read `.claude/commands/<name>.md`
  and follow it yourself.
- **One gate.** If you're about to ask the human something after the
  gate, make the call yourself, write it in `docs/MEMORY.md`, and keep
  going. The exception is anything destructive or expensive to undo.
- **Builders don't freelance.** Extras reach the code through the brief
  and the plan. A builder that adds something on its own fails review.
- **Stop for real blockers only:** a task that can't pass review, a smoke
  test that won't go green, a missing secret or service. Report the task
  and the findings.
