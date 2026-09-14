# GenWave Claude Code Toolkit

The `.claude/` configuration that turns Claude Code into a disciplined,
phased software-development pipeline for this repo: slash commands that take
an idea from a one-line pitch to committed, reviewed code, plus the skill
library the commands and agents lean on.

It started life as a portable toolkit. Most of it still is; the parts that
are GenWave-specific are marked 🎙️ below. `.claude/templates/lang-template`
is the starting point for a new language skill and is not loaded as a skill.

## 🎯 Guiding principle: Design for Change

Every command, agent, and skill in this collection serves one principle:
**the goal of writing software is to be able to change it safely.** SOLID,
the GoF patterns, coupling/cohesion, BDD specs, schema conventions —
they're not ends, they're tactics in service of that goal.

Concretely, every artifact this toolkit produces should optimize for:

- Low coupling, high cohesion — one reason to change per module (SRP).
- Open to extension, closed to modification — stable seams behind interfaces.
- Localized blast radius — a change in one place doesn't ripple.
- Composition over shared mutable state.
- Names that telegraph intent so the next person finds the seam.
- A small diff for the next change.

Read every skill, command, and review finding through this lens. If a rule
in here doesn't make the next change easier, it's the wrong rule.

## Layout

```
.claude/
├── commands/   # slash-command phases + git helpers
├── agents/     # builder + reviewer subagents (used by /build-loop)
└── skills/     # knowledge + workflow skills, surfaced by description
```

## Commands

Phase commands run in order on a fresh project; each owns exactly one set of
documents and is **re-entrant** (re-run to refine, not restart).

```
/init ──▶ /explore ──▶ /design ──▶ /plan ──▶ /spec ──▶ /build-loop
            (problem)    (solution)  (tasks)   (specs)    (code)

/document ── run anytime; reconciles docs ↔ reality (not a phase)
```

### Phase commands

| Command | Question it answers | Owns |
|---|---|---|
| `/init` | What files do we need? | `CLAUDE.md`, `docs/` skeleton, `README.md` stub, `.gitignore` |
| `/explore` | What problem, for whom, why? | `docs/PROJECT.md` |
| `/design` | How do we build it? | `docs/ARCHITECTURE.md`, `docs/SPEC.md` |
| `/plan` | In what order, as what tasks? | `docs/STORIES.md`, `docs/PLAN.md` |
| `/spec` | Pending BDD specs from stories | spec files (via `bdd-specs` skill) |
| `/build-loop` | Build it. | the code + git history |
| `/document` | Do the docs still match reality? | `README.md`, `docs/ARCHITECTURE.md` prose, `docs/MEMORY.md` |
| `/quick-fix` | Trivial fix on the current branch — no plan, no stories. | the code |

`docs/MEMORY.md` is the project's decision log — every phase *appends* dated
entries; `/document` curates it. It is distinct from the `~/.claude` memory
system.

Phases that gather requirements (`/explore`, `/design`) interview you with
batched `AskUserQuestion` rounds. Phases that transform (`/plan`, `/spec`,
`/build-loop`) consume the prior phase's output and do not re-elicit.

### Git helpers

Small commands that defer to the `git-workflow` skill — direct, detailed, no
ceremony. PRs and issues go through the `gh` CLI against `GenWave-Org/genwave`
(the skill ships body templates for both). Merging, tagging, releasing and
pushing `main` are Dean's; a PreToolUse hook enforces it. The hook path falls
back to `$PWD` when `CLAUDE_PROJECT_DIR` is unset or empty, so the guard still
fires in sessions started from the project root.

| Command | Purpose |
|---|---|
| `/git-commit` | Stage explicitly and commit using Conventional Commits on a non-main branch. |

## Agents

`builder` and `reviewer` exist only to serve `/build-loop`.

| Agent | Model | Tools | Role |
|---|---|---|---|
| `builder` | sonnet | Read, Write, Edit, Glob, Grep, Bash, Skill | Implements one PLAN.md task. Stays in its files. Never commits unless told. |
| `reviewer` | inherit | Read, Glob, Grep, Bash, Skill (**no** Write/Edit) | Read-only gate. Returns `PASS` or `FAIL` with findings. A gate that can fix itself isn't a gate. |

The reviewer is intentionally stronger than the builder.

## Orchestrating the build loop

`/build-loop [path-to-PLAN.md]` (default `docs/PLAN.md`) is the only phase that
writes code. It is an **orchestrator pattern**: the command runs in the lead
thread and dispatches subagents — it never writes feature code or reviews code
itself.

**Preflight:** resolve the plan, confirm it is a checkbox list (`- [ ]`),
confirm a clean git working tree. Stop and report if any of these fail.

**The loop** — for each unchecked task, top to bottom in dependency order:

```
  ┌─────────────────────────────────────────────────────┐
  │  task: - [ ]                                         │
  │     │                                                │
  │     ▼                                                │
  │  1. BUILD   → builder subagent (sonnet)               │
  │              implements the task, runs the tests,     │
  │              reports a diff. Does NOT commit.         │
  │     │                                                │
  │     ▼                                                │
  │  2. REVIEW  → reviewer subagent (inherit)               │
  │              read-only; runs the matching security    │
  │              skill (security-api / security-web) +    │
  │              simplify; returns PASS or FAIL.          │
  │     │                                                │
  │     ├── FAIL ─▶ findings → fresh builder ─┐ (recurse, │
  │     │          ◀───────────────────────────  no cap) │
  │     ▼                                                │
  │  3. PASS    → builder commits ONLY this task's diff   │
  │              with a message naming the task.          │
  │     │                                                │
  │     ▼                                                │
  │  4. CHECK   → flip - [ ] to - [x] in PLAN.md, commit. │
  └─────────────────────────────────────────────────────┘
            ▼  next task
```

When every box is checked: run the full suite once more and report a summary.

**Role separation is strict.** Builder owns code; reviewer owns the gate (no
edit tools by design); the loop owns sequencing and checkbox state. Nothing
reaches git before a `PASS`. If a task can't pass after repeated attempts, the
loop stops and reports rather than committing degraded code.

**This loop is a pipeline, not a parallel team** — sequential, dependency-
ordered subagents via the Agent tool, *not* Agent Teams. For parallel
multi-agent work, see the `agent-teams` skill.

## Skills

Skills under `.claude/skills/` are auto-surfaced by description; commands and
agents also invoke them explicitly via the Skill tool.

### Language & design

- `csharp-best-practices` — modern C#/.NET 10: nullable discipline (no `!`), async/cancellation, records, one type per file, zero warnings
- `aspnetcore-patterns` — endpoints, middleware order, BackgroundService, options pattern, DI lifetimes, health checks
- `typescript-best-practices` — strict TS for the Admin UI, type modeling, Result errors, validated boundaries
- `solid-principles` — the five SOLID principles (language-agnostic; TS examples)
- `design-principles` — coupling/cohesion, DRY/YAGNI/KISS, Demeter, Tell Don't Ask, CQS, fail-fast
- `gof-patterns` — all 23 Gang of Four patterns (language-agnostic; TS examples)

### Data

- `postgres-dba` — snake_case singular tables, surrogate keys, NOT NULL FKs, enums, JSONB; logic in the host

### Security

- `security-api` — ASP.NET Core API review: JWT/authz, IDOR, injection, mass assignment, SSRF, file handling, secrets
- `security-web` — XSS/CSRF/injection/SSRF/auth review for the TS/JS frontend

### Ops

- `docker-linux-ops` — multi-stage .NET images, compose, NFS media volumes, graceful shutdown, container debugging

### Product

- `design-aesthetic` 🎙️ — GenWave's "Wireless" visual identity: tokens, type, spacing, motion; auto-surfaced when generating UI

### Process & specs

- `user-stories` — agile stories + Given/When/Then acceptance criteria → `docs/STORIES.md`
- `bdd-specs` 🎙️ — executable specs from STORIES.md (Feature > Scenario > Specification; xUnit / Jest; the entry-point scenario rule is GenWave's)
- `agent-teams` — orchestrate parallel agent teammates (the multi-agent counterpart to `/build-loop`)

### Workflow

- `git-workflow` — the one Git policy: GitHub + `gh`, branches only, explicit staging, no trailers, merge/tag/release = Dean

Each SKILL.md skill is a directory with frontmatter `name` + `description` that
controls when it triggers, plus optional `references/` and `templates/`.

## Conventions

- **One owner per document.** Every stub names its owning command at the top.
  If a command needs information another doc owns, it reads it — it does not
  rewrite it.
- **Re-entrant phases.** Re-running a command refines existing output and
  preserves completed work (e.g. `/plan` never drops `- [x]` tasks).
- **Stop, don't guess.** A phase missing its input (no PLAN.md, a SPEC full of
  `TODO`) stops and points back to the owning command instead of inventing.
- **Commands inherit the session model.** No command pins a model, so the
  phases run on whatever you launched Claude Code with. The `builder` agent
  is pinned to the `sonnet` alias (execution); the `reviewer` inherits the
  session model so the gate is never weaker than the session.

## Usage

Copy `.claude/` into a project, then from Claude Code in that project:

```
/init my-project         # scaffold CLAUDE.md + docs/
/explore                 # define the problem  → docs/PROJECT.md
/design                  # decide the solution → docs/ARCHITECTURE.md, SPEC.md
/plan                    # slice into tasks    → docs/STORIES.md, PLAN.md
/spec                    # pending BDD specs from stories
/build-loop docs/PLAN.md # build, review, commit task-by-task
/document                # whenever docs drift from the code
```
