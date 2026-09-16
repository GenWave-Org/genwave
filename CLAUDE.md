# GenWave

Self-hosted internet radio station for a small private community. A C# .NET 10 control plane orchestrates [Liquidsoap](https://www.liquidsoap.info/) (real-time mixing, crossfade, encode) and [Icecast](https://icecast.org/) (fan-out), backed by Postgres. The point: **loudness-matched, crossfaded broadcast that never stops** end-to-end on Docker.

## Rules

- DO NOT REPORT SOMETHING IS FIXED IF YOU HAVEN'T COMPILED THE APP
- DO NOT SEARCH node_modules for answers. GO ONLINE.
- Use emoji for markdown documents for readability.
- Get to the point, be terse, do not over explain. Tokens are water, we're in the desert.
- Never install a package by editing the manifest — always use `dotnet add package`.
- Never add `Claude-Session:` trailers, `Co-Authored-By` lines, or claude.ai/code links to commits, PRs, issues, or releases.

## Stack

| Layer | Technology |
|---|---|
| Language | C# / .NET 10 |
| Runtime host | `GenWave.Host` (ASP.NET Core minimal API + hosted services) |
| Core domain | `GenWave.Core` (no framework deps) |
| Media library | `GenWave.MediaLibrary` (scan, enrich, catalog) |
| Audio engine | Liquidsoap (controlled via TCP socket) |
| Streaming | Icecast |
| Database | PostgreSQL |
| Container | Docker Compose (`compose.yaml`) |
| Tests | xUnit (9 test projects under `tests/`) |

## Commands

```bash
# Build
dotnet build GenWave.sln

# Test — PR tier (no Docker, ffmpeg on PATH) / full suite (Docker + ffmpeg)
dotnet test GenWave.sln --filter "Category!=Integration"
dotnet test GenWave.sln
# Host suite alone on the dev box: append `-- xUnit.MaxParallelThreads=3`

# Run locally (Docker) — migrates, then composes; a raw `docker compose up` skips migrations
./launch.sh

# Build solution + run tests + build images (SKIP_TESTS=1 skips the tests)
./build.sh
```

## Layout

```
src/
  GenWave.Abstractions/  # MIT contract surface (published as the GenWave.Abstractions NuGet)
  GenWave.Core/          # domain types, abstractions (no infra deps)
  GenWave.Context/       # external-context providers (weather, history): pipeline + fact sanitizer
  GenWave.Host/          # ASP.NET Core host: API, engine control, playout feeder
  GenWave.MediaLibrary/  # media scan, loudness enrichment, Postgres catalog
  GenWave.Loudness/      # Ffmpeg{Loudness,Cue,Energy}Analyzer + AubioBpmAnalyzer
  GenWave.Tts/           # Kokoro client, render→measure→cache (ITtsSegmentSource)
  GenWave.Orchestration/ # Orchestrator (INextItemProvider): music + TTS patter interleave
  GenWave.Ads/           # ad briefs, sponsors, script validation, AdSpotWorker
  GenWave.Plugins/       # plugin door: SPI load context, whole-plugin skip on failure
tests/
  GenWave.Ads.Tests/
  GenWave.Architecture.Tests/  # the fitness laws (see CONTRIBUTING.md + SEAMS.md)
  GenWave.Context.Tests/
  GenWave.Core.Tests/
  GenWave.Host.Tests/
  GenWave.MediaLibrary.Tests/
  GenWave.Orchestration.Tests/
  GenWave.Plugins.Tests/
  GenWave.Tts.Tests/
engine/genwave.liq           # Liquidsoap script
icecast/                     # Icecast Dockerfile + config template
db/                          # Postgres init scripts
```

## Phase commands

| Command | Owns | Purpose |
|---|---|---|
| `/explore` | `docs/PROJECT.md` | Define problem, users, goals, scope |
| `/design` | `docs/ARCHITECTURE.md`, `docs/SPEC.md` | Architecture + feature spec |
| `/plan` | `docs/STORIES.md`, `docs/PLAN.md` | Stories + ordered task DAG |
| `/spec` | pending `Story*` spec files under `tests/` | BDD specs from stories (`bdd-specs`) |
| `/build-loop` | `docs/PLAN.md` checkboxes, one commit per task | Build each task through review + smoke |
| `/document` | `README.md`, `DEPLOYMENT.md`, `docs/MEMORY.md` | Reconcile docs with reality |

Each doc has one owner command — don't write another command's file. `docs/` is gitignored: these are local working docs, absent from a fresh clone.
