# Contributing to GenWave

Thanks for wanting to make GenWave better. Bug reports, fixes, and features are all welcome — a short issue describing what you want to change is the best first step for anything bigger than a small fix.

## 🏛️ Architecture governance

Six laws, seven ids (L4 has two halves), enforced as fitness tests in `tests/GenWave.Architecture.Tests` (they run inside the normal `dotnet test GenWave.sln` — no separate CI lane). Full rationale for each: the per-law XML doc comments in that project (start at `Support/LawId.cs`) — the maintainer's own design notes cover the same ground (gh-#398) but live outside this shipped repo (`docs/` is gitignored).

| Law | Rule | Why |
|---|---|---|
| `L1` | Inner projects (`Core`, `Orchestration`, `Tts`, `Loudness`, `Context`) reference no ASP.NET, Npgsql, or Dapper | Keeps the hexagon's inner logic host-agnostic and unit-testable without infrastructure |
| `L2` | Npgsql/Dapper appear only in `MediaLibrary`'s repository layer — composition-root construction is the *designed* exemption; a handful of pre-existing Host query sites are baselined (gh-#406) | One place owns SQL; connection-per-query safety stays auditable |
| `L3` | `HttpClient` (and the handler family it can be built from) is constructed **or acquired** (injected, factory-resolved) only at designated client seams | SSRF surface control — every outbound origin is enumerable |
| `L4-references` | `GenWave.Abstractions` references nothing beyond the BCL | The MIT NuGet contract is a product boundary; accidental deps become semver pain |
| `L4-immutability` | Every public type in `GenWave.Abstractions` carries no mutable public state (`init`-only properties and `readonly`/`const` fields are fine) | Same contract boundary — a settable property is an accidental behavior promise |
| `L5` | `GenWave.Host` contains no namespace from the reserved/graduated subsystem list | The Host graduation rule's tripwire (gh-#399) — a forgotten graduation is a red test, not a review catch |
| `L6` | `GenWave.Abstractions` never references `GenWave.Core` | Misplaced seams are accidental API commitments |

Adoption is honest: violations that predate a law are named and dated in the suite's exemption baseline (gh-#406) and the laws fail on NEW violations only — never add a baseline entry to make your own change green.

**Seam placement** (gh-#400): "Does a third-party module need to implement or consume this? → `GenWave.Abstractions`. Else → `GenWave.Core/Abstractions`." Need means *demonstrated* need — a plausible future module is not a demonstrated need. Promotion on demonstrated need is cheap; demotion is a breaking change.

L3's designated seams are a named constant, not a list here: `HttpClientSeams.DesignatedSeams`.

**Check `SEAMS.md`** before adding a new seam — extend or decorate an existing port over minting a near-duplicate; regenerate with `dotnet run --project tools/SeamIndexGenerator` (CI byte-diffs it against committed, SPEC F105.6).

## 📜 Contributor License Agreement (required)

GenWave Home is AGPL-3.0-only and always will be. Its development is funded by GenWave Business, a commercially licensed edition built on the same core. That model only works if the maintainer holds sufficient rights in every line of the core — so **every external contribution requires agreeing to the [Contributor License Agreement](CLA.md) before it can be merged**.

Signing is lightweight and one-time: a bot prompts on your first pull request and records your agreement in a comment. No paperwork. You keep full rights to use your own contributions for any other purpose.

## 🤖 AI-assisted development

GenWave is built openly with AI assistance — as a force multiplier for the people building it, not a replacement for them. Design decisions, reviews, and sign-offs are human; the repository's `.claude/` toolkit is part of the codebase and you're welcome to use it. You may use AI tools in your own contributions too, with the same deal we hold ourselves to: **you are responsible for what you submit.** It must meet the review bar, and you must have the right to contribute it under the CLA.

## 🛠️ Development

```bash
dotnet build GenWave.sln                                  # build
dotnet test GenWave.sln --filter "Category!=Integration"  # unit tests (no Docker)
dotnet test GenWave.sln                                   # full suite (Docker + ffmpeg)
cd admin-ui && npx tsc --noEmit && npm test && npm run build  # admin UI checks
```

See the [README](README.md) for prerequisites and how to run the full stack.

## ✅ Pull requests

- One concern per PR; conventional-commit style messages (`feat:`, `fix:`, `docs:`, `chore:`).
- Build and tests green, zero compiler warnings.
- Match the surrounding code's conventions — nullable reference types, one type per file, no `!` null-forgiving operator in production code.
- Behavior changes need a test that fails without them.

## 🔐 Security issues

Please do not open public issues for suspected vulnerabilities — report them privately to the maintainer.
