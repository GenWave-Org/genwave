# GenWave — Broadcast Audio Streaming Service

[![CI](https://github.com/GenWave-Org/genwave/actions/workflows/ci.yml/badge.svg)](https://github.com/GenWave-Org/genwave/actions/workflows/ci.yml)
[![Release](https://badgen.net/github/release/GenWave-Org/genwave)](https://github.com/GenWave-Org/genwave/releases)
[![NuGet](https://badgen.net/nuget/v/GenWave.Abstractions)](https://www.nuget.org/packages/GenWave.Abstractions)
[![License](https://badgen.net/github/license/GenWave-Org/genwave)](LICENSE)
[![Demo on-air](https://github.com/GenWave-Org/genwave/actions/workflows/demo-health.yml/badge.svg)](https://demo.genwaveradio.com/)

A self-hosted internet radio station: one shared broadcast stream, **equal-power crossfades**, and **loudness level-matching** so quiet and loud tracks play back at a consistent volume. It never emits dead air. Deployed entirely via Docker.

No hand-built audio engine. A C# / .NET 10 control plane orchestrates [Liquidsoap](https://www.liquidsoap.info/) (real-time mix, crossfade, encode) and [Icecast](https://icecast.org/) (fan-out). Selection is criteria-based — the feeder pulls through `INextItemProvider` over a media library catalog; there is no ordered playlist table.

This is **GenWave Home**, the AGPL edition — see [License](#license).

🎧 **Hear it live:** [demo.genwaveradio.com](https://demo.genwaveradio.com/) — the public demo station, running the [reference appliance topology](DEPLOYMENT.md): hear & see what's on the air, tune in, even request a song!

🎙️ **On the air fast:** [download.genwaveradio.com](https://download.genwaveradio.com/) launches the setup wizard — no sudo, anywhere in the path. Measured cold start: **on air in 1:03** on a Raspberry Pi 5 (4 GB, 9,000-track NFS library) — 1:36 to the first note. 1:10 on a fresh VM (CCX23 same as demo.genwaveradio.com). (2026-08-19, wizard-measured.)


## Why GenWave

Twenty-plus years ago, in my first job as a software engineer, we pulled a lot of all-nighters, fueled by music that IT graciously let us keep on one of their servers (the Exchange server, if memory serves). We'd bring in CDs, IT would rip them, and when the last non-engineer left for the night, someone would fire up a playlist and start the stream.

It was great, until it wasn't. The problem with playlists is that they're dumb: same music, same order, every time. Being engineers, we solved it! Sort of. I wrote a playlist generator that jumbled the order on every run. Problem solved! Except now we were hearing B-sides and artists nobody recognized, with no good way to find out what was playing. It felt more like real radio, and it left us with a thought that stuck, with me at least: *wouldn't it be cool if we had DJs to announce the music?* There was no way to build that back then, so we coerced our QA lead into recording a few sound bites, played one every X tracks, and christened the result FLAP Radio (because reasons).

The itch never went away. Over the years I built a home version of FLAP Radio. Still, honestly, a playlist randomizer, and I was never truly happy with it. Then, a couple of years ago, LLMs and TTS started making a serious splash, and the itch came back in earnest: the technology had finally caught up with the idea. This time GenWave was born for real: a station that never goes silent, knows what it's playing, and has a DJ who tells you about it.

Itch scratched. 📻


## Quickstart

You need Docker (with Compose v2.24+) and a music library of `.mp3`/`.flac` files — see
[HARDWARE.md](HARDWARE.md) for what GenWave runs on and how to size a box.

Published images are **multi-arch (`amd64` + `arm64`)**, and a 4GB Raspberry Pi 5 is a
verified deployment — playout plus on-box TTS, no LLM. See HARDWARE.md's Raspberry Pi
section for the prep that topology needs.

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

Both scripts preflight the machine before touching anything (Docker running, compose plugin, .NET SDK, `.env` secrets) and every failure exit says how to proceed; on the dev flow, a launch that fails part-way rolls the stack back down rather than leaving half of it running. `--pinned` deliberately does **not** roll back — whatever is still broadcasting keeps broadcasting, and the failure report says how to converge (see [DEPLOYMENT.md](DEPLOYMENT.md)). `SKIP_PREFLIGHT=1` bypasses the checks on unusual setups.

Seven services start: `db`, `icecast`, `engine`, `api`, `kokoro` (TTS synthesizer), `admin_ui` (operator console — rides the `admin` compose profile, on by default via `.env.example`), and `dockerproxy` (a read-only, allowlisted docker-stats sidecar feeding the admin Health page — internal network only, no ports). Three more services ride opt-in compose profiles: `piper`, a CPU-only fallback TTS (`fallback` — off by default since v5.1.0's voice contract; see Resilience below), a Cloudflare tunnel with health/metrics observability (`tunnel`), and a Grafana Alloy log shipper (`logging`) — `./launch.sh --with fallback,logging,tunnel` activates any of them; see [DEPLOYMENT.md](DEPLOYMENT.md) and [`observability/`](observability/).

`launch.sh` has three other presets worth knowing: `--pinned` (run published GHCR images instead of building — the appliance/upgrade path), `--piper-only` (drop `kokoro` *and* the `ollama` pair for a 4GB-class box, run Piper as the station's **primary** TTS engine, and halve enrichment concurrency), and `--dry-run` (print the exact command plan, touch nothing). After a successful launch the file stack is recorded as `COMPOSE_FILE` in `.env`, so a plain `docker compose down`/`ps`/`logs` targets the same stack you launched.

- **Stream:** `http://localhost:8000/stream` — open it in any audio player
- **Admin UI:** `http://localhost:3000` — log in with the password set in `ADMIN_PASSWORD`
- **API:** `http://localhost:8080` — anonymous hot path (`GET /media/random`, `GET /media/{id}`, `GET /health`) plus the cookie-auth admin surface under `/api/*`
- **Spectator page:** `http://localhost:8081` — the station's read-only public face (now playing, history, stats, an optional anonymous song-request line with free-text wishes plus genre/mood pickers). Off by default: flip the live `Station:SpectatorMode` setting to enable it; [DEPLOYMENT.md](DEPLOYMENT.md) covers the four operating modes and the public topology. Metadata-aware players also get **per-track album art** via ICY `StreamUrl` once `Station:PublicBaseUrl` is set.

On first boot the library scans `MEDIA_DIR`, enriches each file (loudness + cue + energy + BPM + tags, plus a high-confidence MusicBrainz release-year lookup when the tags carry none — disable-able live via `Library:YearLookup:Enabled`), and the feeder begins pulling ready tracks. Until the first tracks are ready, the engine plays the safe-rotation source — a curated library scope (`Station:SafeScope:LibraryIds`) pulled via `GET /internal/safe-track`. On a fresh deploy, a one-shot boot seed creates a `safe` library, renders a branded TTS announcement ("Please Stand By"), and points SafeScope at it — so drains air the announcement, not a random track; an operator-set SafeScope is never overwritten. If the scope resolves empty, `mksafe` emits silence as a logged degraded mode. The Orchestrator interleaves TTS patter (station IDs, lead-ins, back-announces — and, opt-in, top-of-hour time checks plus weather and this-day-in-history segments) with music once Kokoro is up. When an `Llm:Endpoint` is configured (Settings page — live, no restart), lead-ins and back-announces become LLM-authored copy, optionally in an operator-authored DJ persona's voice (Personas page) — or hire a ready-made DJ from the community [Community Catalog](https://github.com/GenWave-Org/genwave-catalog) (CC0 persona cards, browsed and adopted one-click from the Admin UI after a full-card review); with no LLM configured the template patter airs unchanged. A weekly **format clock** (Schedule page — a drag-paint 7×48 grid) decides who's on the air when, with audible DJ-to-DJ handoffs at the boundaries; since v3.4.0 the hours have names too: **shows** (a name, a tagline, and a flavor line that colors the DJ's patter) assign to whole runs in one click, sign-ons welcome you to the named show, top-of-hour idents brand it, dated **specials** shadow the weekly grid for one-off broadcasts, and the spectator card tells listeners what's on now and up next. Since v5.1.0 the DJs can even talk to each other: **crosstalk** — short two-voice banter exchanges written and rendered ahead of air, dropped in at a mid-show break seam, each aired exactly once. Opt-in per show via `Crosstalk:Shows` (empty = off; nothing changes on upgrade until you enable it). Station identity (`STATION_NAME`, voice, scope) defaults to `GWAV 108.8` / `af_heart` / library 1 — override via env if needed.

The station's look is themeable too (v3.0.0–v3.2.0). A theme is one JSON manifest — colour tokens for light and dark plus curated fonts — composed live into CSS for both the Admin UI and the spectator page; pick one via Settings or the switcher on either surface (a cookie-remembered visitor choice outranks the station default). The Community Catalog generalized from a persona-only shelf into a multi-kind one — personas, themes, shows (v3.4.0), Dean-curated font packs, and since v5.2.0 **avatar packs and icon packs** — each kind on its own shelf tab, all adopted through the same one-click review flow. Installed packs list on the **Wardrobe** page (fonts v3.1.0; avatars and icons v5.2.0), which is also where a pack is uninstalled — font packs refused while a saved theme still references one of their faces, avatar packs freely (worn faces are copies and survive); the **theme editor** (`/editor`, v3.2.0) mixes any theme's palette with a vendored-or-installed face and saves the remix as your own station theme.

And since v5.2.0 the station has faces (gh-#206, gh-#297, gh-#15). **Every DJ can wear an avatar**: install an avatar pack from the catalog and apply faces per persona (suggested matches offered, bulk-apply behind one confirm — nothing auto-writes), or upload your own image per DJ (server-side normalized to a clean 512×512 PNG, metadata stripped structurally). Faces show on the admin Personas page, on the spectator page's **DJ card**, and — for metadata-aware players — as the **stream artwork while that DJ talks**, under a strict *right face or no face* rule: at show boundaries the station shows a placeholder rather than ever pairing a name with the wrong face (two-voice crosstalk credits the station, not one DJ). **Icon packs** swap the admin console's entire icon set live via `Station:IconPack` — a pack is one JSON document whose schema structurally cannot express scripts, links, or literal colors, rendered with per-name fallback to the built-in set so no pack ever breaks a newer page. And a single uploaded **station image** replaces the shipped logo everywhere it appears: stream art for idents, the spectator logo and favicon, and the authed admin tab icon — delete it and the shipped look returns byte-identically.

### Resilience & operator tools

The broadcast never depends on a sick dependency. **LLM failure is a mode, not an error**: consecutive failures walk the station Normal → Soft (one real LLM attempt per cooldown window, template copy otherwise) → Hard (zero LLM calls); background health probes plus a cooldown walk it back up, and an operator can pin any mode live (`Llm:DegradationPin`). **A DJ never speaks in someone else's voice** (the v5.1.0 voice contract): if a DJ's own voice can't be produced, the break simply doesn't air — the music continues uninterrupted, and the Voice tile on the health surface tells you the engine is down versus the DJ having nothing to say. Voice **failover is opt-in**: enable the `fallback` compose profile to run the Piper sidecar and set `Tts:Fallback:Endpoint` (live, no restart) to accept a substitute voice instead of silence — the previous always-on behavior, now a choice ([DEPLOYMENT.md](DEPLOYMENT.md) has both halves). Every spoken line passes one normalization chokepoint (reasoning-block scrub, markdown strip, operator **pronunciation corrections** — editable with live preview under Settings → TTS, e.g. `MacLeod → Muh-cloud`). The **Booth log** page answers "what did the DJ do and say at 9:14" as a persistent narrative feed (track starts, patter, mode changes, 14-day retention), with an **LLM call inspector** tab showing the last ~50 calls (prompt, response, persona, timing, mode — in-memory, never persisted). The **Health** page is a container-level view of the running stack — state, CPU, memory per service — fed by a read-only, endpoint-allowlisted docker-stats sidecar (the API never touches the docker socket). **Station Imaging** (the operator-authored always-airable segments: liners, station IDs, jingles, promos) is authored in the Admin UI and rendered through the same TTS + loudness pipeline as everything else. Dependency health probes **debounce** — a verdict flips unhealthy only after consecutive probe failures (a slow TTS render is not an outage), with interval, timeout, and threshold all live-editable settings. MusicBrainz lookups are throttled to 1 req/s with a version-stamped User-Agent, and misses are stamped so they're never re-asked.

## Repository layout

```
.
├─ compose.yaml            # core topology: db, icecast, engine, api, kokoro, dockerproxy
│                          #   (+ profiles: admin_ui [admin, default-on], piper [fallback],
│                          #      cloudflared [tunnel], alloy [logging])
├─ .env.example            # secrets template → copy to .env
├─ engine/
│  └─ genwave.liq          # Liquidsoap playout script
├─ db/
│  ├─ 01-library.sh        # library schema + library_svc role (canonical fresh install)
│  └─ 02..37-*-migration.sh # idempotent in-place upgrades, one per shipped feature —
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
│  ├─ check-doc-drift.sh         # CI guard: DEPLOYMENT.md/HARDWARE.md values match the compose files (gh-#77)
│  ├─ check-seam-index.sh        # CI guard: SEAMS.md matches a fresh generation byte-for-byte (F105.6)
│  └─ SeamIndexGenerator/        # writes SEAMS.md from the live DI registrations — never hand-edit the map
├─ SEAMS.md                # generated seam index: port → adapter → binding site (see CONTRIBUTING before adding a seam)
└─ src/                    # C# solution (.NET 10)
   ├─ GenWave.Abstractions/  #   the SDK contract surface: selection, catalog read, events, TTS seams
   ├─ GenWave.Core/          #   domain + engine-facing abstractions; zero I/O
   ├─ GenWave.Context/       #   external-context providers (weather, this-day-in-history): one pipeline,
   │                         #   fetch-once-per-slot, fact sanitizer — skip, never silence
   ├─ GenWave.MediaLibrary/  #   scan, enrich, catalog (Postgres)
   ├─ GenWave.Loudness/      #   Ffmpeg{Loudness,Cue,Energy}Analyzer + AubioBpmAnalyzer; shared by MediaLibrary + Tts
   ├─ GenWave.Tts/           #   Kokoro client, LLM copy writer (ISegmentCopyWriter), render→measure→cache
   ├─ GenWave.Orchestration/ #   Orchestrator (INextItemProvider): music + TTS patter interleave
   └─ GenWave.Host/          #   composition root, API (controllers + minimal API), engine control, feeder,
                             #   theme composition (Theming/ — manifests → served CSS, both surfaces)
```

## Tests

```bash
# Core, Orchestration, Tts unit tests (no Docker needed):
dotnet test GenWave.sln --filter "Category!=Integration"

# Full suite including library + Kokoro integration tests (need Docker + ffmpeg):
dotnet test GenWave.sln

# §0 on-air acceptance gate (live engine required):
./tools/onair_gate.sh

# Admin UI (from admin-ui/): type-check (app, then the spec suite's own project), unit tests,
# production build — what CI runs:
npx tsc --noEmit && npm run typecheck:specs && npm test && npm run build
```

Seven test projects: `Core.Tests`, `Context.Tests`, `Host.Tests`, `MediaLibrary.Tests`, `Orchestration.Tests`, `Tts.Tests`, and `Architecture.Tests` — the last enforces the architecture laws (dependency direction, Postgres/HttpClient confinement, contract immutability, the Host graduation tripwire) as ordinary red-green tests; the laws themselves are summarized front-and-center in [CONTRIBUTING.md](CONTRIBUTING.md). The full suite plus the on-air gate are required before anything merges to `main`.

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

GenWave's epic-by-epic history — from v1 broadcast playout through v5.2.x's "faces on
the wall" (the DJs get avatars, the chrome gets icon packs, the station gets its own
image) — lives in [CHANGELOG.md](CHANGELOG.md).

## Roadmap

- **Deferred** — authored-file GC ([gh-#3](https://github.com/GenWave-Org/genwave/issues/3)), origin-side Access JWT validation ([gh-#75](https://github.com/GenWave-Org/genwave/issues/75)), migration-runner adoption ([gh-#12](https://github.com/GenWave-Org/genwave/issues/12)).
- **Beat-matching + set-level sequencing** — BPM/beat-aware transitions and energy-curve scheduling beyond per-pair crossfade duration. Deferred as YAGNI.

## Operational notes

- The Liquidsoap **control port (1234) is unauthenticated and never published**. To inspect it: `docker compose exec engine bash` then connect to `localhost:1234` from inside the container.
- Icecast `/admin` and `/status` share port 8000 — password-protected but reachable on the LAN. **Never publish 8000 on a public box**: the [reference public topology](DEPLOYMENT.md) fronts everything with Caddy and un-publishes it, and CI enforces the posture via `tools/check-compose-publish.sh` (0.0.0.0 publishes allowed only for the proxy).
- **Upgrading an existing deployment:** run `./migrate.sh` after pulling a new release — it applies every `db/*-migration.sh` idempotently against the running stack (`./migrate.sh -f compose.yaml -f compose.pinned.yaml -f compose.demo.yaml` on a demo/appliance box; see [DEPLOYMENT.md](DEPLOYMENT.md)). `./launch.sh` does this automatically for the dev stack; a raw `docker compose up` does **not**.
- Secrets live only in `.env` (gitignored). Promote to Docker secrets before anything public.
- If you change `duration=` in `engine/genwave.liq`, pass the matching `CROSSFADE=` to `smoke_test.sh` so its analysis windows line up.
- The `crossfade` operator behavior and `output.icecast.metadata` on-air signal are specific to Liquidsoap 2.4.x. The engine image is pinned to `v2.4.5` in `engine/Dockerfile` (`FROM savonet/liquidsoap:v2.4.5`) — `compose.yaml` only echoes the pin in a comment — do not change it without re-running the smoke test.

## Built with AI assistance

GenWave is developed openly with AI as a force multiplier for the people (me) building it — not a replacement for them. Design decisions, reviews, and sign-offs are human; the `.claude/` toolkit in this repository is part of how the project is built and you're welcome to use it. The same deal applies to contributions — see [CONTRIBUTING.md](CONTRIBUTING.md).

If you want the workflows/skills I use in GenWave for your own projects, you can find them [here](https://ai.bigmachine.io/c/hello), along with a lot of other awesome AI resources. Hats off to Rob Conery for his awesome [Claude Code Toolkit](https://ai.bigmachine.io/c/free-stuff/roll-your-own-claude-code-toolkit-bc0a72)!

## Standing on

GenWave is a control plane, not a from-scratch audio engine — it's wired around real infrastructure that did the hard parts first:

- **[Liquidsoap](https://www.liquidsoap.info/)** — the real-time mixing, crossfade, and encode engine underneath the whole broadcast.
- **[Icecast](https://icecast.org/)** — fans the mixed stream out to every listener.
- **[Kokoro](https://github.com/hexgrad/kokoro)** — the primary DJ voice, an open-weight TTS model rendered locally.
- **[Piper](https://github.com/rhasspy/piper)** — the CPU-only fallback voice that keeps a DJ talking on modest hardware.
- **[Ollama](https://ollama.com/)** — runs the local LLM that writes lead-ins and back-announces when you point GenWave at one — the station airs template patter without it.
- **[ffmpeg](https://ffmpeg.org/)** — loudness, cue points, energy analysis: the numbers every crossfade decision rests on.
- **[aubio](https://aubio.org/)** — the BPM analysis behind every track's tempo.
- **[PostgreSQL](https://www.postgresql.org/)** — the catalog of record for every track, measurement, and scheduled show.

🙏 to the maintainers of all eight — none of this plays a note without them.

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). External contributions require a one-time, lightweight [CLA](CLA.md) so the Home/Business dual-license model stays viable. Please also read the [Code of Conduct](CODE_OF_CONDUCT.md) and, for anything security-shaped, [SECURITY.md](SECURITY.md).

## License

GenWave ships in two editions:

- **GenWave Home** — this repository. Licensed under the [GNU Affero General Public License v3.0](LICENSE) (`AGPL-3.0-only`). GenWave Home is AGPL and always will be.
- **GenWave Business** — a commercial edition built on the same core, licensed separately. Development of Home is funded by GenWave Business.

**One deliberate exception:** the module contract surface in [`src/GenWave.Abstractions/`](src/GenWave.Abstractions/) (published as the `GenWave.Abstractions` nuget package) is **MIT-licensed** — see [its LICENSE](src/GenWave.Abstractions/LICENSE) — so any module, open or commercial, can link the contracts freely. Everything else in this repository is AGPL-3.0-only.
