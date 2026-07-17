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

- **v1 broadcast playout** — loudness-matched, crossfaded, never-silent MP3 stream on Docker.
- **Phase 1 — TTS as a source** — station IDs, lead-ins, back-announces, time/date patter interleaved with music via Kokoro (CPU).
- **Epic F — Cue-point trim** — per-track `cue_in_sec`/`cue_out_sec` at enrichment, `liq_cue_in`/`liq_cue_out` stamped at push. Liquidsoap 2.4 honors natively; zero engine-script change. Tightens dead-air gap at music→voice and voice→music transitions; `blank.eat` stays as backstop for un-enriched rows.
- **Admin UI (read-only)** — Next.js operator console (`localhost:3000`): catalog browse, now-playing, play-history. Single-station deployment; config-password auth; no reverse proxy. A second station = a second deployment with its own `.env`.
- **Epic H — Energy-aware transitions** — per-track intro/outro energy measured at enrichment (`intro_energy`/`outro_energy`), stamped as `gw_*_energy` annotations, mapped by the engine's `gw_transition` to a crossfade duration in `[GW_XFADE_MIN, GW_XFADE_MAX]` (default 2–8s): hotter pairs wash shorter, mellow pairs longer; fixed 3s/3s fallback when energy is absent — never silence.
- **Epic I — Catalog writes + station settings** — per-track tag edits and rotation eligibility (incl. bulk by-filter curation for 9000+ tracks), and live non-secret station settings via the Admin UI. `PATCH /api/media/{id}` (`If-Match` optimistic concurrency), `GET/PUT /api/settings`. DB-backed settings overlay (`station.settings` + `station_svc` role) live-applies api-side knobs (loudness target, cadence, crossfade range) without an `api` restart; engine-side knobs (`GW_XFADE_*`) apply on engine restart with a UI warning.
- **Epic J — Library management + re-enrichment** — create/rename/delete libraries, reassign tracks between them (single `PATCH /api/media/{id}` with `libraryId` + bulk `POST /api/media/bulk/reassign`), and operator-triggered re-enrichment (`POST /api/media/{id}/reenrich?fields=…` + `POST /api/media/bulk/reenrich`). Re-enrichment is a sentinel reset — the existing enricher worker reclaims at the 50/tick cap; no new background service. Cross-scope reassigns succeed and signal with `X-Out-Of-Scope: true` + UI confirm. This is what makes `Station:Scope:LibraryIds` a real curation knob.
- **Epic K — Safe-rotation as a curated library scope** — the hand-authored safe file is retired; the engine's `safe` source is a `request.dynamic` pulling annotated tracks from `Station:SafeScope:LibraryIds` via anonymous `GET /internal/safe-track` (core network only). Safe tracks are first-class pushes: loudness matching, cue trim, and energy transitions apply identically. Empty scope → `204` → `mksafe` silence (logged degraded mode).
- **Epic M — Operational hardening** — safe prefetch capped at 1 (`prefetch=1, retry_delay=5.` — scope edits lag at most one stale track), the named-library escape hatch for parked-row recovery (`?library-id=` browse + bulk filters swap effective scope, `X-Out-Of-Scope: true`), `Station:Scope:LibraryIds` live-editable, and full now-playing metadata for engine-initiated safe plays (parsed from the output-metadata poll — no extra telnet, no per-tick DB).
- **Epic N — SafeScope-empty legalization** — an empty `Station:SafeScope:LibraryIds` is legal at boot and via `PUT /api/settings` (WARN + degrade to `mksafe` on drain, per SPEC F4.4); distinct WARN logs separate empty-scope from empty-catalog drains; the settings page badges "Silent on drain." Drain-state display overrides were built, shipped, and reverted after live verification — branding belongs in the authored content's tags, not the transport path.
- **Epic P — Safe-loop authoring** — TTS-generated "we'll be back" safe segments authored from inside the system: sync `POST /api/safe-segments` + an Admin UI "Safe content" page, optional station-jingle bed mixed offline via ffmpeg (cue-trimmed, looped, ducked), artifacts on the `authored` volume as first-class catalog rows branded `artist: <Station Name>` / `title: "Please Stand By"` (embedded in the file too), plus an idempotent boot seed so fresh deploys drain into the announcement. Closes gitea-#149/gitea-#172.
- **Epic Q — Admin UI redesign "Wireless"** — the console's committed identity: warm retro radio (cream/rust/brass), Fraunces + Source Sans 3 vendored via `next/font/local`, light + dark ("walnut & brass") with a persisted cookie toggle, Tailwind v4 + shadcn/ui, token-first theming (a future named theme = one token file — canonical tokens in `.claude/skills/design-aesthetic/SKILL.md`). Persistent sidebar shell + breadcrumbs, a real dashboard (now-playing card, `GET /api/status` tiles, recent plays), Libraries as a Catalog tab, selection-model bulk toolbar, toasts + modal confirms, 5 s polling paused on hidden tabs, responsive to 390 px. Closes gitea-#174.
- **Epic R — Hardening sweep (SPEC F29–F32)** — closes the Epic P/Q operator-testing backlog (gitea-#179–gitea-#186, gitea-#192, gitea-#193): `{StationName}` expansion at the author seam, a distinct boot-seed title, Kokoro voices via `GET /api/voices` + a dropdown with free-text fallback, a configurable engine-side gap between safe tracks (`GW_SAFE_GAP_SECONDS`, default 7 s, `0` disables), guaranteed `artist` on engine-initiated plays (the feeder re-extracts metadata on every advance for engine-owned ids), main scope live via `IOptionsMonitor` (`StationContext` keeps identity only), `ETag` on every PATCH success + one shared row-PATCH hook with real failure toasts, depleted-SafeScope warnings on settings + dashboard, a favicon, and the smoke test formally documented as a **manual pre-release gate**, not CI. Post-ship live fixes on `main`: the favicon is now the operator's GenWave logo (`app/icon.png`), TTS patter carries `artist` = station name, and the feeder always refills at boot (SPEC F7.5 amended — a single-segment safe rotation could deadlock a fresh boot).
- **Epic S — Track rating (SPEC F33)** — an operator taste signal on the Live page: thumbs up/down adjust a per-track score (default 50, clamped 0–100), an X toggles a standalone never-play flag that immediately suppresses every selection path (main, safe, `/media/random`). State lives in a 1:1 `library.media_rating` extension table so votes never disturb edit `ETag`s and bulk curation is structurally incapable of touching ratings. Score is a ledger until the scheduling phase consumes it. Catalog shows score, a never-play badge, a `?never-play=true` filter, and a restore control (no one-way doors). Vote/never-play endpoints are deliberately not scope-gated. Closes gitea-#188 (score-weighted selection deferred to the scheduling phase).
- **Epic T — DJ intelligence (SPEC F34–F36)** — patter graduates from fixed templates to LLM-authored lead-ins/back-announces: an OpenAI-compatible chat-completions endpoint (Ollama-native) behind the new `ISegmentCopyWriter` seam, with the shipped templates as the terminal fallback — any LLM failure means one WARN and yesterday's patter, never a stall. Operator-authored DJ personas (`station.persona`, one live-active via `Station:Persona:ActiveId`) flavor the copy and pick the TTS voice, authored on a Personas page with honest previews (real LLM copy + playable wav — no silent fallback). `Tts:Endpoint`/`Llm:Endpoint` are location-agnostic, live-editable URLs — compose ships unchanged; running either service in-stack or on the LAN is the operator's own deployment choice. Blurb audio is GC'd by `Tts:BlurbRetentionHours`; the dashboard gains an LLM status tile. Closes gitea-#175/gitea-#176/gitea-#178 — plus the live-found gitea-#211 (cadence toggles now apply live via `ICadenceProvider`).
- **Epic U — On-air metadata fidelity (SPEC F37–F40)** — safe plays gain level-matching (the safe branch never had its own `amplify` — audio-real, measured pre/post on a recorded drain) and real `gainDb` telemetry via the encoder metadata-export list (gitea-#200); annotations always stamp `artist` (explicitly empty when the row has none — grounded in a source-verified Liquidsoap v2.4.4 pass; the gitea-#199 bleed itself never reproduced, contingency recorded); all four patter kinds air attributed to the active persona (gitea-#212), and ICY collapses the redundant `"Station | DJ - Station"`; the dashboard SafeScope tile labels tracks vs libraries — no number without a noun (gitea-#214).
- **Epic V — Rotation quality + console fidelity (SPEC F41–F45)** — music selection becomes a tiered preference query (`GetRotationCandidateAsync`): not-recent → artist-not-in-last-N → not-most-recent → random, relaxing with WARNs instead of draining a small catalog to safe (gitea-#210, gitea-#213), with live `Station:Rotation:RecentWindow`/`ArtistSeparation` knobs; `StationIdEveryNUnits` skips unit 0 and `0` disables (gitea-#216); the single-row out-of-scope 403 is repealed — scope is curation, not trust; `X-Out-Of-Scope: true` signals instead (gitea-#203); the settings allowlist completes (every non-secret tunable, four named exclusions; `StationContext` retired for a live `IStationIdentityProvider`; the shell wordmark is the live station name) (gitea-#195–gitea-#197); catalog conflict-retry fixed client-side (gitea-#201/gitea-#202).
- **Epic X — Enrichment 2.0 (SPEC F46–F51)** — BPM analysis via `aubio` in the api image (gitea-#190); whole-track energy as a STORED generated column over the already-measured LUFS — instant catalog-wide backfill, auto-consistent through re-enrichment (gitea-#190); a MusicBrainz release-year lookup for rows whose tags lack one — high-confidence-only (score ≥ 90 + artist match), live kill switch, never overwrites, the first deliberate carve-out from "embedded-tag metadata only" (gitea-#208); Year/BPM/Energy catalog columns behind a visibility toggle + `?year=`/`?decade=`/`?year-missing=` filters; track duration on the now-playing card (elapsed/total + progress) and history lists (gitea-#218); scheduled-enrichment gitea-#191 closed by documentation — the scan interval is already live-editable and claim predicates already touch only new/incomplete rows.
- **Epic Y — Curation & settings fidelity (SPEC F52–F56)** — precise eligibility curation: `GET /api/media/facets` (distinct artist/album/genre values + counts) feeds exact-match filters (`artist-exact`/`album-exact`/multi `genre-exact`) on browse and all bulk operations through one shared WHERE builder, so "take this artist out of rotation" never sweeps a substring lookalike (gitea-#189); inclusive ceilings on every live numeric setting, boot deliberately untouched (gitea-#221); voice + persona dropdowns via a per-key settings control registry (gitea-#224/gitea-#225); ten initializer-equal defaults seeded so no settings field renders a lying blank, help text at full allowlist coverage with a three-way parity guard (gitea-#230/gitea-#231); the ArtistSeparation/RecentWindow coupling documented with a live inline notice (gitea-#227); plus a root-cause fix for boolean settings rendering unchecked while on (the JSON config provider's `"True"` vs a case-sensitive compare).
- **Epic Z — Ranking & robustness (SPEC F57–F64)** — artist/album ranking via bulk vote/never-play on the Catalog toolbar (gitea-#233) with flyover help on every icon control (gitea-#234); feeder-ring integrity: metadata lifetime decoupled from the anti-repeat ring + remember-at-push (gitea-#219/gitea-#220/gitea-#229); scan availability grace via consecutive-miss threshold (gitea-#223); enrichment worker-pool pruning (gitea-#222); year lookup prefers the oldest qualifying recording (gitea-#228); compose-env test drift guard (gitea-#235); LUFS gate recalibrated to target − program offset (gitea-#204).

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
