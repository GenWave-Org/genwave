# GenWave — Broadcast Audio Streaming Service

[![CI](https://github.com/GenWave-Org/genwave/actions/workflows/ci.yml/badge.svg)](https://github.com/GenWave-Org/genwave/actions/workflows/ci.yml)
[![Release](https://badgen.net/github/release/GenWave-Org/genwave)](https://github.com/GenWave-Org/genwave/releases)
[![NuGet](https://badgen.net/nuget/v/GenWave.Abstractions)](https://www.nuget.org/packages/GenWave.Abstractions)
[![License](https://badgen.net/github/license/GenWave-Org/genwave)](LICENSE)
[![Demo on-air](https://github.com/GenWave-Org/genwave/actions/workflows/demo-health.yml/badge.svg)](https://demo.genwaveradio.com/)

A self-hosted internet radio station: one shared broadcast stream, **equal-power crossfades**, and **loudness level-matching** so quiet and loud tracks play back at a consistent volume. It never emits dead air. Deployed entirely via Docker.

No hand-built audio engine. A C# / .NET 10 control plane orchestrates [Liquidsoap](https://www.liquidsoap.info/) (real-time mix, crossfade, encode) and [Icecast](https://icecast.org/) (fan-out). Selection is criteria-based — the feeder pulls through `INextItemProvider` over a media library catalog; there is no ordered playlist table.

This is **GenWave Home**, the AGPL edition — see [License](#license).

🎧 **Hear it live:** [demo.genwaveradio.com](https://demo.genwaveradio.com/) — the public demo station, running the [reference appliance topology](DEPLOYMENT.md): watch what's on the air and tune in!


## Quickstart

You need Docker (with Compose v2.24+) and a music library of `.mp3`/`.flac` files — see
[HARDWARE.md](HARDWARE.md) for what GenWave runs on and how to size a box.

```bash
cp .env.example .env
# edit .env: set POSTGRES_PASSWORD, LIBRARY_DB_PASSWORD, STATION_DB_PASSWORD,
#            ICECAST_SOURCE_PASSWORD, ICECAST_ADMIN_PASSWORD,
#            MEDIA_DIR (absolute path to your library),
#            and ADMIN_PASSWORD (admin UI login; empty = the admin plane is
#            locked entirely — fail-closed, the stream still runs)

./build.sh
./launch.sh
```

Both scripts preflight the machine before touching anything (Docker running, compose
plugin, .NET SDK, `.env` secrets) and every failure exit says how to proceed; a launch
that fails part-way rolls the stack back down rather than leaving half of it running.
`SKIP_PREFLIGHT=1` bypasses the checks on unusual setups.

Seven services start: `db`, `icecast`, `engine`, `api`, `kokoro` (TTS synthesizer), `piper` (CPU-only fallback TTS), and `admin_ui` (operator console). Two optional services ride compose profiles: a Cloudflare tunnel with health/metrics observability (`tunnel`) and a Grafana Alloy log shipper (`logging`) — `./launch.sh --with logging,tunnel` activates them; see [DEPLOYMENT.md](DEPLOYMENT.md) and [`observability/`](observability/).

- **Stream:** `http://localhost:8000/stream` — open it in any audio player
- **Admin UI:** `http://localhost:3000` — log in with the password set in `ADMIN_PASSWORD`
- **API:** `http://localhost:8080` — anonymous hot path (`GET /media/random`, `GET /media/{id}`, `GET /health`) plus the cookie-auth admin surface under `/api/*`

On first boot the library scans `MEDIA_DIR`, enriches each file (loudness + cue + energy + BPM + tags, plus a high-confidence MusicBrainz release-year lookup when the tags carry none — disable-able live via `Library:YearLookup:Enabled`), and the feeder begins pulling ready tracks. Until the first tracks are ready, the engine plays the safe-rotation source — a curated library scope (`Station:SafeScope:LibraryIds`) pulled via `GET /internal/safe-track`. On a fresh deploy, a one-shot boot seed creates a `safe` library, renders a branded TTS announcement ("Please Stand By"), and points SafeScope at it — so drains air the announcement, not a random track; an operator-set SafeScope is never overwritten. If the scope resolves empty, `mksafe` emits silence as a logged degraded mode. The Orchestrator interleaves TTS patter (station IDs, lead-ins, back-announces, time checks) with music once Kokoro is up. When an `Llm:Endpoint` is configured (Settings page — live, no restart), lead-ins and back-announces become LLM-authored copy, optionally in an operator-authored DJ persona's voice (Personas page); with no LLM configured the template patter airs unchanged. Station identity (`STATION_NAME`, voice, scope) defaults to `GWAV 108.8` / `af_heart` / library 1 — override via env if needed.

### Resilience & operator tools

The broadcast never depends on a sick dependency. **LLM failure is a mode, not an error**: consecutive failures walk the station Normal → Soft (one real LLM attempt per cooldown window, template copy otherwise) → Hard (zero LLM calls); background health probes plus a cooldown walk it back up, and an operator can pin any mode live (`Llm:DegradationPin`). **TTS failure is inaudible**: if Kokoro is down or a render throws, the segment re-renders on the Piper fallback engine through the same loudness pipeline — kill the Kokoro container mid-broadcast and the next patter still airs. Every spoken line passes one normalization chokepoint (reasoning-block scrub, markdown strip, operator **pronunciation corrections** — editable with live preview under Settings → TTS, e.g. `MacLeod → Muh-cloud`). The **Booth log** page answers "what did the DJ do and say at 9:14" as a persistent narrative feed (track starts, patter, mode changes, 14-day retention), with an **LLM call inspector** tab showing the last ~50 calls (prompt, response, timing, mode — in-memory, never persisted). MusicBrainz lookups are throttled to 1 req/s with a version-stamped User-Agent, and misses are stamped so they're never re-asked.

## Repository layout

```
.
├─ compose.yaml            # 7-service topology: db, icecast, engine, api, kokoro, piper, admin_ui
│                          #   (+ optional cloudflared [tunnel] and alloy [logging] profiles)
├─ .env.example            # secrets template → copy to .env
├─ engine/
│  └─ genwave.liq          # Liquidsoap playout script
├─ db/
│  ├─ 01-library.sh        # library schema + library_svc role (canonical fresh install)
│  └─ 02..22-*-migration.sh # idempotent in-place upgrades, one per shipped feature —
│                          #   each header says what it adds; ./migrate.sh applies them all
├─ icecast/
│  ├─ Dockerfile           # self-owned Icecast2 image
│  ├─ entrypoint.sh        # renders passwords from env, runs Icecast
│  └─ icecast.xml.tmpl     # hardened single-mount config
├─ admin-ui/               # Next.js (App Router) operator console (`:3000`)
├─ observability/          # the observability contract: Alloy config, label conventions, Grafana dashboards as code (F78)
├─ tools/
│  ├─ find_smoke_candidates.cs   # picks a divergent-gain track pair for the smoke test
│  ├─ smoke_test.sh              # manual pre-release regression gate (no human listening required)
│  ├─ onair_gate.sh              # §0 on-air acceptance gate (live engine)
│  ├─ test-pronunciation.sh      # hear how TTS says a name; iterate spellings, then add a speech correction (gh-#37)
│  ├─ preflight.sh               # shared machine/env checks sourced by build.sh + launch.sh (gh-#19)
│  ├─ check-compose-publish.sh   # CI guard: 0.0.0.0 host publishes allowed only for the front proxy (F67.1)
│  ├─ check-compose-socket.sh    # CI guard: docker.sock read-only + alloy-only, every profile combo (F78.2)
│  └─ check-doc-drift.sh         # CI guard: DEPLOYMENT.md/HARDWARE.md values match the compose files (gh-#77)
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

### Versions

GenWave releases follow a semantic versioning as follows:

```
<major_version>.<minor_version>.<bugfix_version>
```

Where:

- `major_version` is bumped when there are major changes, i.e. major implementation change etc. Versions with different major versions **are** incompatible
- `minor_version` is bumped when there are minor changes, i.e. new features, renaming, new modules etc. Versions with different minor versions **may be** incompatible
- `bugfix_version` is bumped when a new bugfix version is published. Versions with only bugfix version changes **should be** compatible


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

- **Per-track album art in players** (ICY `StreamUrl`) — spike complete, viable ([gh-#105](https://github.com/GenWave-Org/genwave/issues/105)); gated on an Icecast ≥ 2.5.0 image bump (2.4.4 drops the metadata field, and EOLs 2026-12-31).
- **Deferred** — authored-file GC ([gh-#3](https://github.com/GenWave-Org/genwave/issues/3)), origin-side Access JWT validation ([gh-#75](https://github.com/GenWave-Org/genwave/issues/75)), migration-runner adoption ([gh-#12](https://github.com/GenWave-Org/genwave/issues/12)).
- **Beat-matching + set-level sequencing** — BPM/beat-aware transitions and energy-curve scheduling beyond per-pair crossfade duration. Deferred as YAGNI.

## Operational notes

- The Liquidsoap **control port (1234) is unauthenticated and never published**. To inspect it: `docker compose exec engine bash` then connect to `localhost:1234` from inside the container.
- Icecast `/admin` and `/status` share port 8000 — password-protected but reachable on the LAN. **Never publish 8000 on a public box**: the [reference public topology](DEPLOYMENT.md) fronts everything with Caddy and un-publishes it, and CI enforces the posture via `tools/check-compose-publish.sh` (0.0.0.0 publishes allowed only for the proxy).
- **Upgrading an existing deployment:** run `./migrate.sh` after pulling a new release — it applies every `db/*-migration.sh` idempotently against the running stack (`./migrate.sh -f compose.yaml -f compose.demo.yaml` on a demo/appliance box; see [DEPLOYMENT.md](DEPLOYMENT.md)). `./launch.sh` does this automatically for the dev stack; a raw `docker compose up` does **not**.
- Secrets live only in `.env` (gitignored). Promote to Docker secrets before anything public.
- If you change `duration=` in `engine/genwave.liq`, pass the matching `CROSSFADE=` to `smoke_test.sh` so its analysis windows line up.
- The `crossfade` operator behavior and `output.icecast.metadata` on-air signal are specific to Liquidsoap 2.4.x. The engine image is pinned to `v2.4.4` in `compose.yaml` — do not change the pin without re-running the smoke test.

## Built with AI assistance

GenWave is developed openly with AI as a force multiplier for the people (me) building it — not a replacement for them. Design decisions, reviews, and sign-offs are human; the `.claude/` toolkit in this repository is part of how the project is built and you're welcome to use it. The same deal applies to contributions — see [CONTRIBUTING.md](CONTRIBUTING.md).

If you want the workflows/skills I use in GenWave for your own projects, you can find them [here](https://ai.bigmachine.io/c/hello), along with a lot of other awesome AI resources. Hats off to Rob Conery for his awesome [Claude Code Toolkit](https://ai.bigmachine.io/c/free-stuff/roll-your-own-claude-code-toolkit-bc0a72)!

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). External contributions require a one-time, lightweight [CLA](CLA.md) so the Home/Business dual-license model stays viable. Please also read the [Code of Conduct](CODE_OF_CONDUCT.md) and, for anything security-shaped, [SECURITY.md](SECURITY.md).

## License

GenWave ships in two editions:

- **GenWave Home** — this repository. Licensed under the [GNU Affero General Public License v3.0](LICENSE) (`AGPL-3.0-only`). GenWave Home is AGPL and always will be.
- **GenWave Business** — a commercial edition built on the same core, licensed separately. Development of Home is funded by GenWave Business.

**One deliberate exception:** the module contract surface in [`src/GenWave.Abstractions/`](src/GenWave.Abstractions/) (published as the `GenWave.Abstractions` nuget package) is **MIT-licensed** — see [its LICENSE](src/GenWave.Abstractions/LICENSE) — so any module, open or commercial, can link the contracts freely. Everything else in this repository is AGPL-3.0-only.
