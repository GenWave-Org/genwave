# GenWave Claude Code Toolkit

The `.claude/` configuration that gives Claude Code a small product team and
a pipeline to run it for this repo. One command takes a sentence to
committed, reviewed code and an open PR, with one approval from you in the
middle.

It started life as a portable toolkit. Most of it still is; the parts that
are GenWave-specific are marked 🎙️ below. `.claude/templates/lang-template`
is the starting point for a new language skill and is not loaded as a skill.

## 🎯 Guiding principle: Design for Change

Every command, agent, and skill in this collection serves one principle:
**the goal of writing software is to be able to change it safely.** SOLID,
the GoF patterns, coupling/cohesion, BDD specs, schema conventions are
tactics in service of that goal.

Concretely, every artifact this toolkit produces should optimize for:

- Low coupling, high cohesion. One reason to change per module (SRP).
- Open to extension, closed to modification. Stable seams behind interfaces.
- Localized blast radius. A change in one place doesn't ripple.
- Composition over shared mutable state.
- Names that telegraph intent so the next person finds the seam.
- A small diff for the next change.

If a rule in here doesn't make the next change easier, it's the wrong rule.

## ⚖️ Second principle: spend tokens, not attention

Rigor that costs tokens (review gates, specs, entry-point traces, smoke
tests) stays. Rigor that costs the human's attention (interviews,
handoffs, approvals) is cut to one approval per sprint. Agents guess, write
the guess down, and let the human correct it. Merging stays Dean's, always.

## Layout

```
.claude/
├── commands/   # /sprint, the phases it runs, git helpers
├── agents/     # product-owner, architect, builder, reviewer
├── hooks/      # 🎙️ merge-guard.sh (merge/tag/release/push-main = Dean)
├── skills/     # knowledge + workflow skills, surfaced by description
└── templates/  # lang-template (not loaded as a skill)
```

## Commands

```
/init ──▶ /sprint ──▶ ✋ approve the brief ──▶ /plan ──▶ /build-loop ──▶ acceptance ──▶ PR
                                              └────────── run by /sprint ──────────┘

/quick-fix ── trivial fix, still on a branch
/document  ── run anytime; reconciles docs ↔ reality
/explore, /design, /spec ── optional, run by hand
```

### The main path

| Command | Model | Question it answers | Owns |
|---|---|---|---|
| `/init` | sonnet | What files do we need? | `CLAUDE.md`, `docs/` skeleton, `README.md` stub, `.gitignore` |
| `/sprint` | fable | What are we building, for whom, and how? | `docs/BRIEF.md` (old briefs move to `docs/briefs/`) |
| `/plan` | fable | In what order, as what tasks? | `docs/STORIES.md`, `docs/PLAN.md`, pending specs |
| `/build-loop` | inherit | Build it. | the code + git history |
| `/document` | sonnet | Do the docs still match reality? | `README.md`, `DEPLOYMENT.md` values, `docs/ARCHITECTURE.md` prose, `docs/MEMORY.md` |
| `/quick-fix` | sonnet | Trivial fix on a branch. | the code |

### Optional deep dives

| Command | Model | When to use it | Owns |
|---|---|---|---|
| `/explore` | fable | The idea is fuzzy and you want to be interviewed about it. | `docs/PROJECT.md` |
| `/design` | fable | Greenfield or risky, and you want to make each architecture call yourself. | `docs/ARCHITECTURE.md`, `docs/SPEC.md` |
| `/spec` | sonnet | Stories were added after `/plan` ran and need specs. | spec files |

If `PROJECT.md` or `SPEC.md` exist, `/sprint` reads them and builds on
them. `docs/` is gitignored 🎙️: these are local working docs.

`docs/MEMORY.md` is the project's decision log. Every command *appends*
dated entries; `/document` curates it. It is distinct from the `~/.claude`
memory system.

### Git helpers

Small commands that defer to the `git-workflow` skill. PRs and issues go
through the `gh` CLI against `GenWave-Org/genwave` 🎙️. Merging, tagging,
releasing and pushing `main` are Dean's; a PreToolUse hook enforces it. The
hook path falls back to `$PWD` when `CLAUDE_PROJECT_DIR` is unset or empty,
so the guard still fires in sessions started from the project root.

| Command | Model | Purpose |
|---|---|---|
| `/git-commit` | inherit | Stage explicitly and commit using Conventional Commits on a non-main branch. |
| `/git-issue` | sonnet | Open a GitHub issue via `gh issue create`. |
| `/git-pr` | sonnet | Open a PR with what, verification plan, risk note. Never merges. |
| `/git-merge` | sonnet | Get a PR merge-ready (checks, conflicts), then hand Dean the merge command. |
| `/git-remote` | sonnet | Stand up a GitHub remote (name, license, README, contributing, security, issue templates). Refuses when `origin` exists. |

## Agents

| Agent | Model | Tools | Role |
|---|---|---|---|
| `product-owner` | fable | Read, Glob, Grep, Bash, Skill, WebSearch, WebFetch | Writes the product half of the brief. Adds extras inside the delight budget. Proposes cuts. Runs the finished work and returns `ACCEPT`, `POLISH`, or `REJECT`. |
| `architect` | fable | Read, Glob, Grep, Bash, Skill | Writes the technical half of the brief: approach, seams, data changes, decisions, task slice. |
| `builder` | sonnet | Read, Write, Edit, Glob, Grep, Bash, Skill | Implements one PLAN.md task (C#/.NET first-class; TypeScript for admin-ui). Stays in its files. Never commits unless told. |
| `reviewer` | inherit | Read, Glob, Grep, Bash, Skill (**no** Write/Edit) | Read-only gate. Returns `PASS`, `PASS-WITH-NOTES`, or `FAIL` with findings. |

Only the builder can edit. Everyone else reports. The reviewer inherits
the session model so the gate is never weaker than the builder.

## How a sprint runs

```
  /sprint "add a quiet-hours schedule"
     │
     ▼
  1. THINK    product-owner ─┐  dispatched together,
              architect ─────┘  both get your exact words
     │
     ▼
  2. MERGE    lead builds a one-page brief. Every extra is checked
              against the delight budget. Failures move to "Next".
     │
     ▼
  3. GATE ✋   "Here's what we think." You approve, strike extras,
              correct assumptions, answer up to 3 questions.
     │
     ▼
  4. WRITE    docs/BRIEF.md, ARCHITECTURE.md additions, MEMORY.md
     │
     ▼
  5. PLAN     stories, PLAN.md (extras tagged delight:), pending specs
     │
     ▼
  6. BUILD    the build loop, below
     │
     ▼
  7. ACCEPT   product-owner runs the app (./launch.sh) as a customer
     │
     ▼
  8. PR       /git-pr; merge is Dean's
```

### The delight budget

The product owner adds things you didn't ask for. The budget keeps that
small:

- one to three extras per sprint, zero allowed
- no new dependency, no schema change, no new service or route
- one task, one commit
- removable: nothing depends on it
- a one-line why, written from the customer's side
- extras are at most about 15% of the sprint's tasks

Anything over budget goes under **Next** in the brief. The full rules are
in `skills/product-owner/SKILL.md`.

## The build loop

`/build-loop [path-to-PLAN.md]` (default `docs/PLAN.md`) is the only
command that writes feature code. It runs in the lead thread and
dispatches subagents. It never writes or reviews code itself.

**Preflight:** resolve the plan, confirm it is a checkbox list (`- [ ]`),
confirm a clean tree on a feature branch, and a green full-solution
baseline (`dotnet test GenWave.sln --filter "Category!=Integration"`).

**The loop**, for each unchecked task, top to bottom in dependency order:

```
  ┌─────────────────────────────────────────────────────┐
  │  task: - [ ]                                         │
  │     │                                                │
  │     ▼                                                │
  │  1. BUILD   → builder subagent (sonnet)               │
  │              activates the task's pending specs,      │
  │              implements, runs the full solution,      │
  │              zero warnings. Does NOT commit.          │
  │     │                                                │
  │     ▼                                                │
  │  2. REVIEW  → reviewer subagent (inherit)             │
  │              read-only; security-api / security-web + │
  │              simplify + scope check.                  │
  │     │                                                │
  │     ├── FAIL ─▶ findings → fresh builder ─┐ (max 3)   │
  │     │          ◀───────────────────────────          │
  │     ▼                                                │
  │  3. SMOKE   → one real request through the deployed   │
  │              entry point (Kestrel endpoint).          │
  │     │                                                │
  │     ▼                                                │
  │  4. COMMIT  → builder commits ONLY this task's files. │
  │     │                                                │
  │     ▼                                                │
  │  5. CHECK   → flip - [ ] to - [x] in PLAN.md.         │
  └─────────────────────────────────────────────────────┘
            ▼  next task
```

**Three `FAIL`s stop the loop** and report. **Extras never block**: a
`delight:` or `polish:` task that fails review three times is reverted,
marked `- [-]`, logged, and skipped.

**Acceptance.** When every box is checked, the product owner runs the real
app and goes through it as a customer:

| Verdict | What happens |
|---|---|
| `ACCEPT` | Done. Report. |
| `POLISH` | Up to five small items go back through the loop as `polish:` tasks. One round only. |
| `REJECT` | A line in the brief didn't happen. A fix task runs, then acceptance runs once more. |

**Role separation is strict.** Builder owns code; reviewer owns the gate;
product owner owns acceptance; the loop owns sequencing and checkbox
state. Nothing reaches git before a `PASS`.

**This loop is a pipeline, not a parallel team.** Sequential,
dependency-ordered subagents via the Agent tool, *not* Agent Teams. For
parallel multi-agent work, see the `agent-teams` skill.

## Skills

Skills under `.claude/skills/` are auto-surfaced by description; commands and
agents also invoke them explicitly via the Skill tool.

### Product

- `product-owner`: assume-don't-interview, the delight budget, cutting, the acceptance pass, the `BRIEF.md` template
- `design-aesthetic` 🎙️: GenWave's "Wireless" visual identity: tokens, type, spacing, motion; auto-surfaced when generating UI

### Language & design

- `csharp-best-practices`: modern C#/.NET 10: nullable discipline (no `!`), async/cancellation, records, one type per file, zero warnings
- `aspnetcore-patterns`: endpoints, middleware order, BackgroundService, options pattern, DI lifetimes, health checks
- `typescript-best-practices`: strict TS for the admin-ui, type modeling, Result errors, validated boundaries
- `solid-principles`: the five SOLID principles (language-agnostic; TS examples)
- `design-principles`: coupling/cohesion, DRY/YAGNI/KISS, Demeter, Tell Don't Ask, CQS, fail-fast
- `gof-patterns`: all 23 Gang of Four patterns (language-agnostic; TS examples)

### Data

- `postgres-dba`: snake_case singular tables, surrogate keys, NOT NULL FKs, enums, JSONB; logic in the host
- `sqlite-dev`: Bun + Drizzle on SQLite (portable toolkit skill; unused by GenWave)

### Security

- `security-api`: ASP.NET Core API review: JWT/authz, IDOR, injection, mass assignment, SSRF, file handling, secrets
- `security-web`: XSS/CSRF/injection/SSRF/auth review for the TS/JS frontend

### Ops

- `docker-linux-ops`: multi-stage .NET images, compose, NFS media volumes, graceful shutdown, container debugging

### Process & specs

- `user-stories`: agile stories + Given/When/Then acceptance criteria → `docs/STORIES.md`
- `bdd-specs` 🎙️: executable specs from STORIES.md (Feature > Scenario > Specification; xUnit / Jest), pending until a builder activates them
- `agent-teams`: orchestrate parallel agent teammates (the multi-agent counterpart to `/build-loop`)

### Workflow

- `git-workflow` 🎙️: the one Git policy: GitHub + `gh`, branches only, explicit staging, no trailers, merge/tag/release = Dean

Each skill is a directory with a `SKILL.md` whose frontmatter `name` +
`description` control when it triggers, plus optional `references/` and
`templates/`.

## Conventions

- **One gate per sprint.** The human approves the brief. After that, an
  agent that wants to ask something makes the call, logs it in
  `docs/MEMORY.md`, and keeps going. The exceptions are anything
  destructive or expensive to undo, and merging, which is always Dean's.
- **Assume, then show.** Guesses are written as `> ASSUMPTION:` lines.
  Three questions per sprint, only for money, auth, deleting data, the
  deploy target, or anything public.
- **One owner per document.** Every stub names its owning command at the
  top. Other commands may add to a doc. They don't rewrite it.
- **Re-entrant phases.** Re-running a command refines existing output and
  preserves completed work (e.g. `/plan` never drops `- [x]` tasks).
- **Builders don't freelance.** Extras reach the code through the brief
  and the plan.
- **Stop for real blockers only.** A task that can't pass review, a smoke
  test that won't go green, a missing secret.
- **Model choice is deliberate.** Where the thinking decides the outcome
  → `fable` (`/sprint`, `/plan`, `/explore`, `/design`, product-owner,
  architect). Execution → `sonnet`. `/build-loop`, `/git-commit`, and the
  reviewer inherit the session model so the orchestrator and the gate are
  never weaker than you launched with. Aliases, not version-pinned IDs.

## Usage

From Claude Code in this repo:

```
/sprint add a quiet-hours schedule   # brief → approve → plan → build → accept → PR
/quick-fix fix the typo in the bed picker
/document                            # whenever docs drift from the code
```
