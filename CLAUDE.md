# GenWave

Self-hosted internet radio station for a small private community. A C# .NET 10 control plane orchestrates [Liquidsoap](https://www.liquidsoap.info/) (real-time mixing, crossfade, encode) and [Icecast](https://icecast.org/) (fan-out), backed by Postgres. The point: **loudness-matched, crossfaded, never-silent broadcast** end-to-end on Docker.

## Rules

- DO NOT REPORT SOMETHING IS FIXED IF YOU HAVEN'T COMPILED THE APP
- DO NOT SEARCH node_modules for answers. GO ONLINE.
- Use emoji for markdown documents for readability.
- Get to the point, be terse, do not over explain. Tokens are water, we're in the desert.
- Never install a package by editing the manifest — always use `dotnet add package`.

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
| Tests | xUnit (5 test projects under `tests/`) |

## Commands

```bash
# Build
dotnet build GenWave.sln

# Test
dotnet test GenWave.sln

# Run locally (Docker)
./launch.sh          # or: docker compose up

# Build Docker image only
./build.sh
```

## Layout

```
src/
  GenWave.Core/          # domain types, abstractions (no infra deps)
  GenWave.Host/          # ASP.NET Core host: API, engine control, playout feeder
  GenWave.MediaLibrary/  # media scan, loudness enrichment, Postgres catalog
  GenWave.Loudness/      # Ffmpeg{Loudness,Cue,Energy}Analyzer + AubioBpmAnalyzer
  GenWave.Tts/           # Kokoro client, render→measure→cache (ITtsSegmentSource)
  GenWave.Orchestration/ # Orchestrator (INextItemProvider): music + TTS patter interleave
tests/
  GenWave.Core.Tests/
  GenWave.Host.Tests/
  GenWave.MediaLibrary.Tests/
  GenWave.Orchestration.Tests/
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
| `/document` | `README.md`, `docs/MEMORY.md` | Reconcile docs with reality |

Each doc has one owner command — don't write another command's file.
