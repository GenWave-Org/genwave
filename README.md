# GenWave — Broadcast Audio Streaming Service

A self-hosted internet radio station: one shared broadcast stream, **equal-power crossfades**, and **loudness level-matching** so quiet and loud tracks play back at a consistent volume. It never emits dead air. Deployed entirely via Docker.

No hand-built audio engine. A C# / .NET 10 control plane orchestrates [Liquidsoap](https://www.liquidsoap.info/) (real-time mix, crossfade, encode) and [Icecast](https://icecast.org/) (fan-out). Selection is criteria-based — the feeder pulls through `INextItemProvider` over a media library catalog; there is no ordered playlist table.

This is **GenWave Home**, the AGPL edition — see [License](#license).

## Quickstart

You need Docker (with Compose) and a music library of `.mp3`/`.flac` files.

```bash
cp .env.example .env
# edit .env: set POSTGRES_PASSWORD, LIBRARY_DB_PASSWORD, STATION_DB_PASSWORD,
#            ICECAST_SOURCE_PASSWORD, ICECAST_ADMIN_PASSWORD,
#            MEDIA_DIR (absolute path to your library),
#            and ADMIN_PASSWORD (admin UI login; leave blank for open API in local dev)

docker compose up -d --build
```

Six services start: `db`, `icecast`, `engine`, `api`, `kokoro` (TTS synthesizer), and `admin_ui` (operator console).

- **Stream:** `http://localhost:8000/stream` — open it in any audio player
- **Admin UI:** `http://localhost:3000` — log in with the password set in `ADMIN_PASSWORD`
- **API:** `http://localhost:8080` — anonymous hot path (`GET /media/random`, `GET /media/{id}`, `GET /health`) plus the cookie-auth admin surface under `/api/*`

On first boot the library scans `MEDIA_DIR`, enriches each file (loudness + cue + energy + BPM + tags, plus a high-confidence MusicBrainz release-year lookup when the tags carry none — disable-able live via `Library:YearLookup:Enabled`), and the feeder begins pulling ready tracks. Until the first tracks are ready, the engine plays the safe-rotation source — a curated library scope (`Station:SafeScope:LibraryIds`) pulled via `GET /internal/safe-track`. On a fresh deploy, a one-shot boot seed creates a `safe` library, renders a branded TTS announcement ("Please Stand By"), and points SafeScope at it — so drains air the announcement, not a random track; an operator-set SafeScope is never overwritten. If the scope resolves empty, `mksafe` emits silence as a logged degraded mode. The Orchestrator interleaves TTS patter (station IDs, lead-ins, back-announces, time checks) with music once Kokoro is up. When an `Llm:Endpoint` is configured (Settings page — live, no restart), lead-ins and back-announces become LLM-authored copy, optionally in an operator-authored DJ persona's voice (Personas page); with no LLM configured — or on any LLM failure — the template patter airs unchanged. Station identity (`STATION_NAME`, voice, scope) defaults to `GWAV 108.8` / `af_heart` / library 1 — override via env if needed.

## Repository layout

```
.
├─ compose.yaml            # 6-service topology: db, icecast, engine, api, kokoro, admin_ui
├─ .env.example            # secrets template → copy to .env
├─ engine/
│  └─ genwave.liq          # Liquidsoap playout script
├─ db/
│  ├─ 01-library.sh                   # library schema + library_svc role (canonical fresh install)
│  ├─ 02-library-id-migration.sh      # idempotent: adds library.library + library_id FK
│  ├─ 03-cue-points-migration.sh      # idempotent: adds cue_in_sec, cue_out_sec, cue_analyzed_at
│  ├─ 04-energy-migration.sh          # idempotent: adds intro_energy, outro_energy, energy_analyzed_at
│  ├─ 05-catalog-writes-migration.sh  # idempotent: adds eligible + tags_edited_at on library.media
│  ├─ 06-station-settings-migration.sh # idempotent: station schema + station_svc role + station.settings
│  ├─ 07-library-management-migration.sh # idempotent: adds UNIQUE(name) on library.library (F20)
│  ├─ 08-rating-migration.sh          # idempotent: library.media_rating 1:1 extension table (F33)
│  ├─ 09-persona-migration.sh         # idempotent: station.persona table (F35)
│  └─ 10-enrichment2-migration.sh     # idempotent: bpm, year_lookup_at, track_energy generated column, media_year index (F46–F48)
├─ icecast/
│  ├─ Dockerfile           # self-owned Icecast2 image
│  ├─ entrypoint.sh        # renders passwords from env, runs Icecast
│  └─ icecast.xml.tmpl     # hardened single-mount config
├─ admin-ui/               # Next.js (App Router) operator console (`:3000`)
├─ tools/
│  ├─ find_smoke_candidates.cs   # picks a divergent-gain track pair for the smoke test
│  ├─ smoke_test.sh              # manual pre-release regression gate (no human listening required)
│  └─ onair_gate.sh              # §0 on-air acceptance gate (live engine)
└─ src/                    # C# solution (.NET 10)
   ├─ GenWave.Abstractions/  #   the SDK contract surface: selection, catalog read, events, TTS seams
   ├─ GenWave.Core/          #   domain + engine-facing abstractions; zero I/O
   ├─ GenWave.MediaLibrary/  #   scan, enrich, catalog (Postgres)
   ├─ GenWave.Loudness/      #   Ffmpeg{Loudness,Cue,Energy}Analyzer + AubioBpmAnalyzer; shared by MediaLibrary + Tts
   ├─ GenWave.Tts/           #   Kokoro client, LLM copy writer (ISegmentCopyWriter), render→measure→cache
   ├─ GenWave.Orchestration/ #   Orchestrator (INextItemProvider): music + TTS patter interleave
   └─ GenWave.Host/          #   composition root, API (controllers + minimal API), engine control, feeder
```

## Tests

```bash
# Core, Orchestration, Tts unit tests (no Docker needed):
dotnet test GenWave.sln --filter "Category!=Integration"

# Full suite including library + Kokoro integration tests (need Docker + ffmpeg):
dotnet test GenWave.sln

# §0 on-air acceptance gate (live engine required):
./tools/onair_gate.sh

# Admin UI (from admin-ui/): type-check, unit tests, production build — what CI runs:
npx tsc --noEmit && npm test && npm run build
```

Five test projects: `Core.Tests`, `Host.Tests`, `MediaLibrary.Tests`, `Orchestration.Tests`, `Tts.Tests`. The full suite plus the on-air gate are required before anything merges to `main`.

## Optional — prove the audio spine with the smoke test

Validates the riskiest third-party behavior (annotation format, Icecast password, crossfade overlap) with none of your own configuration in the way. Needs `ffmpeg`/`ffprobe`, `jq`, and the .NET 10 SDK on the host.

```bash
# Load MEDIA_DIR from .env into the shell (paths must resolve under the engine's /media mount)
set -a; . ./.env; set +a

# 1. Pick the most divergent (quiet vs. loud) track pair from your library
cd tools
dotnet run find_smoke_candidates.cs -- "$MEDIA_DIR"
cd ..

# 2. Run the automated smoke test. Brings up db+engine+icecast, pushes the pair, records the
#    stream, asserts output LUFS ≈ target for both with no silent gap at the crossfade.
#    Exits non-zero on failure. (SMOKE_DOWN=1 to tear down after.)
cp tools/smoke-candidates.json .
./tools/smoke_test.sh
```

> ⚠️ The smoke test is a **manual pre-release gate** — CI does not run it. It uses the default
> compose project and pushes test tracks onto whatever engine it targets: run it only against a
> scratch stack (fresh checkout or isolated `-p` project), never a live station's deployment.

If level checks fail by a consistent offset, the `replay_gain` annotation format is wrong (bare number vs `"X.XX dB"`) — the test's failure message points at this.

## Shipped phases

GenWave's epic-by-epic history — v1 broadcast playout through Ranking & robustness — lives in
[CHANGELOG.md](CHANGELOG.md).

## Roadmap

- **Deferred** — authored-file GC (gitea-#205), legacy contract cleanup (gitea-#206).
- **Beat-matching + set-level sequencing** — BPM/beat-aware transitions and energy-curve scheduling beyond per-pair crossfade duration. Deferred as YAGNI.

## Operational notes

- The Liquidsoap **control port (1234) is unauthenticated and never published**. To inspect it: `docker compose exec engine bash` then connect to `localhost:1234` from inside the container.
- Icecast `/admin` and `/status` share the public port 8000 — password-protected but reachable. Add a TLS reverse proxy before exposing this publicly.
- Secrets live only in `.env` (gitignored). Promote to Docker secrets before anything public.
- If you change `duration=` in `engine/genwave.liq`, pass the matching `CROSSFADE=` to `smoke_test.sh` so its analysis windows line up.
- The `crossfade` operator behavior and `output.icecast.metadata` on-air signal are specific to Liquidsoap 2.4.x. The engine image is pinned to `v2.4.4` in `compose.yaml` — do not change the pin without re-running the smoke test.

## Built with AI assistance

GenWave is developed openly with AI as a force multiplier for the people building it — not a replacement for them. Design decisions, reviews, and sign-offs are human; the `.claude/` toolkit in this repository is part of how the project is built and you're welcome to use it. The same deal applies to contributions — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). External contributions require a one-time, lightweight [CLA](CLA.md) so the Home/Business dual-license model stays viable. Please also read the [Code of Conduct](CODE_OF_CONDUCT.md) and, for anything security-shaped, [SECURITY.md](SECURITY.md).

## License

GenWave ships in two editions:

- **GenWave Home** — this repository. Licensed under the [GNU Affero General Public License v3.0](LICENSE) (`AGPL-3.0-only`). GenWave Home is AGPL and always will be.
- **GenWave Business** — a commercial edition built on the same core, licensed separately. Development of Home is funded by GenWave Business.

**One deliberate exception:** the module contract surface in [`src/GenWave.Abstractions/`](src/GenWave.Abstractions/) (published as the `GenWave.Abstractions` nuget package) is **MIT-licensed** — see [its LICENSE](src/GenWave.Abstractions/LICENSE) — so any module, open or commercial, can link the contracts freely. Everything else in this repository is AGPL-3.0-only.
