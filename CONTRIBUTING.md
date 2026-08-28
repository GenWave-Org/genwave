# Contributing to GenWave

Thanks for wanting to make GenWave better. Bug reports, fixes, and features are all welcome — a short issue describing what you want to change is the best first step for anything bigger than a small fix.

## 🏛️ Architecture governance

Ten laws, eleven ids (L4 has two halves), enforced as fitness tests in `tests/GenWave.Architecture.Tests` (they run inside the normal `dotnet test GenWave.sln` — no separate CI lane). Full rationale for each: the per-law XML doc comments in that project (start at `Support/LawId.cs`) — the maintainer's own design notes cover the same ground (gh-#398) but live outside this shipped repo (`docs/` is gitignored).

| Law | Rule | Why |
|---|---|---|
| `L1` | Inner projects (`Core`, `Orchestration`, `Tts`, `Loudness`, `Context`) reference no ASP.NET, Npgsql, or Dapper | Keeps the hexagon's inner logic host-agnostic and unit-testable without infrastructure |
| `L2` | Npgsql/Dapper appear only in `MediaLibrary`'s repository layer — composition-root construction is the *designed* exemption, and since the gh-#406 burn-down it is the baseline's only kind of entry | One place owns SQL; connection-per-query safety stays auditable |
| `L3` | `HttpClient` (and the handler family it can be built from) is constructed **or acquired** (injected, factory-resolved) only at designated client seams | SSRF surface control — every outbound origin is enumerable |
| `L4-references` | `GenWave.Abstractions` references nothing beyond the BCL | The MIT NuGet contract is a product boundary; accidental deps become semver pain |
| `L4-immutability` | Every public type in `GenWave.Abstractions` carries no mutable public state (`init`-only properties and `readonly`/`const` fields are fine) | Same contract boundary — a settable property is an accidental behavior promise |
| `L5` | `GenWave.Host` contains no namespace from the reserved/graduated subsystem list | The Host graduation rule's tripwire (gh-#399) — a forgotten graduation is a red test, not a review catch |
| `L6` | `GenWave.Abstractions` never references `GenWave.Core` | Misplaced seams are accidental API commitments |
| `L7` | No production type outside the two named relays (`NormalizingTtsSynthesizer`, `FallbackTtsSynthesizer`) references `ITtsSynthesizer`'s context-less `SynthesizeAsync(string, string, CancellationToken)` overload directly | Every other caller must carry kind/rules/pace through `TtsRenderContext`, never silently drop them |
| `L8` | Outside `GenWave.Tts`, no production code calls `PronunciationRuleSet.Merge`/`MergeWithProvenance` or `PronunciationRuleProvider.BuildMerged` directly — `PronunciationsController`'s own `MergeWithProvenance` call (its display-only rules-table projection, never a render) is the one *designed* exemption | `PronunciationRuleResolver.ResolveForRender` is the one resolve seam for air and audition — parity is structural, not a coincidence two call sites agree on today |
| `L9` | Outside `AnnouncementsController` and `AnnouncementNowPlayingController`, no production type names `AnnounceTokenAuthenticationDefaults.SchemeName` inside an `[Authorize(AuthenticationSchemes = ...)]` list | A widened schemes list elsewhere would silently promote the HA announce token to full admin authority, with every other test still green |
| `L10` | No dependency cycles among `GenWave.*` namespaces (`Gh445_NamespaceCycleFreedom`) | A cycle is an accidental module merge |

Adoption is honest: violations that predate a law are named and dated in the suite's exemption baseline and the laws fail on NEW violations only — never add a baseline entry to make your own change green. (The five debt entries that shipped with adoption were burned down via gh-#406 on 2026-08-13; only designed exemptions remain.)

**Seam placement** (gh-#400): "Does a third-party module need to implement or consume this? → `GenWave.Abstractions`. Else → `GenWave.Core/Abstractions`." Need means *demonstrated* need — a plausible future module is not a demonstrated need. Promotion on demonstrated need is cheap; demotion is a breaking change.

L3's designated seams are a named constant, not a list here: `HttpClientSeams.DesignatedSeams`. Likewise L7's relays and L8's exemption: `TtsSynthesizeContextSeam.DesignatedRelays` and `PronunciationResolveSeam.DesignatedExemptions`.

**Check `SEAMS.md`** before adding a new seam — extend or decorate an existing port over minting a near-duplicate; regenerate with `dotnet run --project tools/SeamIndexGenerator --configuration Release` (CI byte-diffs it against committed, SPEC F105.6 — Release, same configuration the CI drift check builds, gh-#413).

**Nothing on the feeder push path may throw on artwork resolution.** `ArtworkUrlResolver.ResolveAsync` composes the ICY `url=` annotation for every track/segment push; a thrown exception there is a dead-air bug, not merely a bad annotation. The discipline is now three-component: the resolver itself, `PersonaAvatarTokenCache` (dj faces), and `StationImageCache` (the station image) — each of the two caches degrades a store fault to an honest "nothing to offer" (no face / no customization) rather than propagating, the same never-throws contract `DurationRehydrator` (Playout) carries for its own catalog read. Extend this list, don't grow a fourth independent throw-suppressing wrapper, if a future artwork source joins the push path.

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
cd admin-ui && npx tsc --noEmit && npm run typecheck:specs && npm test && npm run build  # admin UI checks
```

See the [README](README.md) for prerequisites and how to run the full stack.

## ✅ Pull requests

- One concern per PR; conventional-commit style messages (`feat:`, `fix:`, `docs:`, `chore:`).
- Build and tests green, zero compiler warnings.
- Match the surrounding code's conventions — nullable reference types, one type per file, no `!` null-forgiving operator in production code.
- Behavior changes need a test that fails without them.

## 🔐 Security issues

Please do not open public issues for suspected vulnerabilities — report them privately to the maintainer.
