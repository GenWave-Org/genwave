# 📡 Deployment

How to run GenWave on the box in your house: what it needs, how to start it, what runs, how to upgrade it, and what to check when something looks wrong. One deployment is one station. For hardware sizing see [HARDWARE.md](HARDWARE.md); for plugins see [PLUGINS.md](PLUGINS.md).

---

## 📋 What you need

- Linux with Docker Engine, and Docker Compose v2.24 or newer (the compose files use the `!override` merge tag).
- x86-64 or arm64. A 4 GB box can run the low-memory layout (`--piper-only`, below).
- The .NET 10 SDK, but only if you build from source. Prebuilt images need no SDK.
- A music library of `.flac` or `.mp3` files in a folder the box can read. With no audio files the station starts silent.
- A few free ports: `8000` (stream), `8080` (admin API), `8081` (spectator page), `3000` (admin UI).

`./build.sh` needs none of the above except the SDK, Docker and Compose. It builds images with placeholder secrets, so no `.env` or music is needed to build.

---

## 🧙 First run: the wizard

```bash
./setup.sh
```

Four questions: prebuilt images or build from source, where the music lives, which layout, and whether the admin UI is on. Then it generates every secret, writes `.env` in one atomic step (a cancelled run leaves nothing behind), and starts the station with `launch.sh`. After the station is on air, it installs a starter background-music pack. `./setup.sh --offline` skips that one step.

If you skip the wizard, copy `.env.example` to `.env` and fill it in. Values must be unquoted, even with spaces in them.

The wizard writes one layout fact into `.env`, `GW_PRESET`. `launch.sh` is its only reader:

| `GW_PRESET` | Files | Shape |
|---|---|---|
| `home` | `compose.yaml` + `compose.pinned.yaml` | Published images, full stack |
| `home-piper-only` | + `compose.piper-only.yaml` | The 4 GB layout: Piper speaks, Kokoro is off |
| `dev` / `dev-piper-only` | `compose.yaml` (+ piper-only) | Build from source |

Any explicit topology flag (`--piper-only`) makes `launch.sh` ignore the preset, and `--pinned` is retired: set `GW_PRESET=home` and run plain `./launch.sh`. An unknown value exits `2` rather than guessing.

**Existing box.** Running `./setup.sh` where a `.env` already exists never writes anything. It checks the install against the machine (secrets present, migrations applied, orphaned containers, stale images, disk) and prints a drift report. Exit `0` means no drift, `5` means drift was found. `./setup.sh --repair` walks each fixable finding, prints the exact command, warns if it will restart a container, and asks y/N. Add `--yes` to accept all of them. Findings with no safe fix (a missing `.env` key, stale image ages) stay report-only.

---

## 🗺️ The three operating modes

Two flags decide what exists. Every combination is valid.

| `Admin:Enabled` | `Station:SpectatorMode` | Mode | What runs |
|---|---|---|---|
| `true` | `false` | **Operator** (default) | Admin UI and API. No spectator page |
| `true` | `true` | **Standard** | Admin UI and API, plus `:8081` for viewers on your network (a kitchen tablet, a kiosk) |
| `false` | `false` | **Headless** | The stream only. No web surface beyond `/health` |

How each is set:

- **`Admin:Enabled`** is env-only and never a live setting: set `Admin__Enabled` in the `api` service's `environment:` block. Changing it needs a container recreate, by design.
- **`Station:SpectatorMode`** is a live setting. Change it in the admin settings, or seed it at boot with `Station__SpectatorMode`. ⚠️ A value saved in the settings database **overrides** the env var. If the page won't appear although the env is set, there is a stale database row.
- **`COMPOSE_PROFILES`** decides whether the `admin_ui` container runs at all. `.env.example` ships `COMPOSE_PROFILES=admin`. A Headless box clears it.

For Standard mode, set `Station__SpectatorMode: "true"` and point a browser at `http://<host>:8081/`. `compose.yaml` already publishes `8081`.

---

## 🧩 What runs

| Service | In one line |
|---|---|
| `db` | Postgres: library catalog, station settings, history. Only the api can see it |
| `icecast` | Fans the stream out to listeners on `:8000` |
| `engine` | Liquidsoap: mixing, crossfade, loudness, encoding. Its control port is never published |
| `api` | The control plane: scanning, picking, patter, settings, health. `:8080` is the admin API, `:8081` is the spectator page |
| `admin_ui` | The Admin UI on `:3000`. Runs only under the `admin` profile |
| `kokoro` | The main voice. Voice files come from the `voices` volume |
| `voice-seed` | One-shot: copies Kokoro's stock voices into the `voices` volume on first start |
| `piper` | Backup or (in `--piper-only`) primary voice. Runs under the `fallback` profile or `--piper-only` |
| `dockerproxy` | Read-only view of container stats for the Health page. Never published |
| `alloy` | Optional log shipper under the `logging` profile; needs `LOKI_PUSH_URL` |

Named volumes: `pgdata` (database), `tts` (rendered speech), `authored` (generated segments and jingle packs), `voices` (voice files), `piper_models`, `dp_keys` (keeps your admin login across restarts), `alloy_data`. Your music is bound read-only at `/media`.

The language model is any OpenAI-compatible endpoint you set live as `Llm:Endpoint` and `Llm:Model`. Without one the station still talks, using template patter. `Llm:TimeoutSeconds` defaults to `10`; raise it for a slow model. `Llm:ReasoningEffort` defaults to `none`, which stops thinking-style models from spending the whole budget on reasoning and returning empty lines. Use `omit` for a backend that rejects the field.

---

## 🚀 Starting, upgrading, stopping

```bash
./launch.sh                     # teardown, db up, migrate, full up
./launch.sh --piper-only        # from-source low-memory layout (see below)
./launch.sh --with logging      # also turn on profiles for this launch
./launch.sh --dry-run           # print the exact plan and touch nothing
BUILD=1 ./launch.sh             # force a rebuild (from-source flow only)
```

With `GW_PRESET=home` in `.env` (the wizard's prebuilt choice), `./launch.sh` uses published images and never builds. That is also the upgrade path. Without a preset it builds from source. `--piper-only` on its own is the from-source low-memory layout; a prebuilt-image box uses `GW_PRESET=home-piper-only` instead.

**What a published-image run does (`GW_PRESET=home*`).** It is staged so the station gets on air first:

1. Pull the core images (`db`, `icecast`, `engine`, `api`).
2. Start `db` without recreating it, and wait until it is healthy.
3. Apply migrations with `./migrate.sh`.
4. Start the core. The station is on air.
5. Pull everything else and converge it.

A box on a fresh install and a box being upgraded take the same path. A running `db` is never restarted before migrations pass. After a successful run it prunes images older than about a week, so old release tags don't fill the disk.

**Exit codes.** `0` fully converged. `2` bad invocation. `3` preflight or stage-1 failure; on the published-image flow the running stack is left exactly as it was. `4` the core is on air but the second stage failed; the printed summary names the catch-up command. Whatever is broadcasting keeps broadcasting.

**Preflight.** Before touching anything, `launch.sh` checks that Docker is running, the compose plugin exists, and `.env` holds real (non-placeholder) secrets. Every failure says how to proceed. `SKIP_PREFLIGHT=1` bypasses the checks in `launch.sh` and `build.sh`. `SKIP_TESTS=1` is separate and skips the tests in `build.sh`.

**Migrations.** Every `db/*-migration.sh` is an idempotent in-place upgrade, so running with nothing new is a safe no-op. `./migrate.sh --dry-run` lists them; `--keep-going` applies the rest after one fails. Back up the database before an upgrade that rewrites existing data; the sponsors migration drops and renames columns on the ad tables.

**Stopping.** After a successful launch, `launch.sh` records the file set it used as `COMPOSE_FILE` in `.env`, so a bare `docker compose down`, `ps` or `logs` targets what you actually launched. Profiles are not recorded. A bare `down` can leave profile containers (`admin_ui`, `piper`, `alloy`) running, so add `--remove-orphans` or set a standing `COMPOSE_PROFILES` in `.env`.

⚠️ **Dropping a profile leaves its container running.** Removing `fallback` from `--with` (or switching `--piper-only` on or off) does not stop the old container, because Compose only removes services that are no longer defined. Stop it once by hand: `docker compose rm -fs piper` (or `kokoro`).

### 🪶 Low-memory layout: `--piper-only`

For a 4 GB box (a Raspberry Pi, a small VPS):

```bash
GW_PRESET=home-piper-only   # in .env, or answer the wizard's layout question
./launch.sh
```

On a from-source box, `./launch.sh --piper-only` gives the same layout but builds locally.

It removes `kokoro` and its `voice-seed`, and sends every spoken break to Piper. It also halves `Library:EnrichmentConcurrency` to `2`. Override on any layout in `.env`:

```bash
LIBRARY_ENRICHMENT_CONCURRENCY=1   # lower still, or raise to get the library built sooner
```

Measured on a 4 GB Pi 5: about 800 tracks an hour at concurrency 4 and about 700 at 2, so 9,000 tracks take roughly 11 and 13 hours. The station is on air the whole time. [HARDWARE.md](HARDWARE.md) has the Raspberry Pi specifics.

⚠️ **Expected on every piper-only box, not a broken install.** The api logs `kokoro health probe failed (Name or service not known (kokoro:8880))` once, then goes quiet. `Tts:Endpoint` deliberately keeps pointing at the absent Kokoro host, and `GET /api/voices` returns a 502 for the same reason. Piper is reached directly through `Tts:PiperPrimaryEndpoint`, and the broadcast never depends on the dead hostname.

### 🔁 Backup voice: opt-in

By default there is no backup voice: if Kokoro fails, that break is dropped rather than read in a different voice. To opt in, start Piper and tell the api where it is:

```bash
./launch.sh --with fallback        # or COMPOSE_PROFILES=fallback in .env
```

Then set `Tts:Fallback:Endpoint` to `http://piper:5000` in the admin settings. No restart is needed.

---

## ⚙️ Settings that live only in the environment

Most settings are live: change them in the admin UI and they apply on the next use. The ones below are read once at boot, so they cannot be changed from the UI.

⚠️ **The root `.env` file does not reach containers by itself.** `compose.yaml` passes through only the variables it names. Set a key below in the `api` service's `environment:` block, usually in a small `compose.local.yaml` you write and stack last (`-f compose.yaml -f compose.pinned.yaml -f compose.local.yaml`). It does not ship.

| Key | Default | What it does |
|---|:---:|---|
| `Admin__Enabled` | `true` | The admin plane on or off (see the modes above) |
| `Station__SpectatorMode` | `false` | Seeds the spectator page at boot. A saved database value wins |
| `Station__Theme` | blank | Seeds the look by theme slug. A saved value wins, and a visitor's own `genwave-theme` cookie beats both. An unknown slug falls through to the next level, ending at the shipped default |
| `Plugins__Enabled` | `false` | Turns on plugin loading. Must be exactly `true` or `false` |
| `Plugins__Root` | `/plugins` | Where plugins are read from |
| `Announcements__MessageMaxChars` | 280 | A longer announcement is a 400 |
| `Announcements__AcceptedPerMinute` | 6 | Station-wide announcement rate |
| `Announcements__PendingDepthCap` | 12 | Pending announcements held; deeper is a 429 |
| `Gardener__IntervalMinutes` | 60 | How often the library Gardener runs |
| `Gardener__FileActions__Enabled` | `false` | Lets the Gardener retag, rename and move files (below) |
| `Ads__WorkerIntervalMinutes` | 10 | How often the ad worker renders |
| `Ads__RetiredAudioRetentionDays` | 30 | How long retired ad audio is kept before the hourly sweep deletes it |
| `Packs__JingleRoot` | `/authored/jingle-packs` | Where jingle packs install |
| `Packs__VoicesRoot` | `/voices` | Where voice packs install |
| `Packs__PreviewMaxBytes` | 153600 | Largest accepted voice-pack preview clip |
| `Packs__JingleAssetMaxBytes` | 5242880 | Largest accepted jingle asset |
| `Llm__ApiKey` | blank | Bearer token for the language-model endpoint. Never stored in or returned by the settings API; `compose.yaml` doesn't pass it through, so set it in your `compose.local.yaml` |
| `LIBRARY_ENRICHMENT_CONCURRENCY` | 4 (2 piper-only) | Analyzer workers; this one is read from `.env` |

The remaining `Gardener__*` and `Ads__*` tuning keys are boot-validated: an out-of-range value stops the api from starting, and the error names the key.

**`Plugins__Enabled` crashes the api on any value but `true` or `false`.** That includes an empty string, and a map entry interpolated from an unset variable also becomes an empty string. A bare valueless entry (`Plugins__Enabled:` with nothing after the colon) is dropped and defaults to `false`. Plugins load once at boot, so adding or updating one is a restart. Turn them on with `docker compose -f compose.yaml -f compose.plugins.yaml up`; see [PLUGINS.md](PLUGINS.md).

**File actions are the only feature that writes to your music folder.** Retag, rename and move exist; there is no delete. They are off by default and need two things:

```bash
# 1. Gardener__FileActions__Enabled: "true" in the api environment: block
# 2. Make the music bind writable with the shipped overlay:
docker compose -f compose.yaml -f compose.pinned.yaml -f compose.local.yaml -f compose.fileactions.yaml up -d
```

`launch.sh` takes no extra compose files, and it rewrites the `COMPOSE_FILE=` line in `.env` on every launch, so re-append `:compose.fileactions.yaml` after each run. Symptom of a missing half: with the folder still read-only a confirmed action fails at execution; with the overlay but the switch off, the endpoints 404. Writes stay inside `Library:MediaRoot`, never overwrite an existing path, and a retag swaps in behind a `.gwbak` backup that is deleted on success. If a revert itself fails the `.gwbak` stays beside the file and blocks further retags of it. Restore with `mv 'track.mp3.<suffix>.gwbak' 'track.mp3'`, or delete the `.gwbak` once the current file is the one you want.

**Library scan.** A catalog row whose path lies outside the current `Library:MediaRoot` is taken out of rotation after `Library:Scan:MissThreshold` missed scans (default 2), and comes back if the root moves back. `Library:Scan:QuarantineExemptRoots` (default `/authored`) lists roots exempt from this. If you relocate the `authored` volume, update it and `Station:Safe:AuthoredRoot` together.

**Announcements and Home Assistant.** An announcement token lets a home-automation system speak on air without the admin cookie. Mint it on the Announcements page (shown once; only its hash is stored), rotate by minting again, and revoke by deleting; a revoked token fails on the next request. It works as a `Bearer` on `/api/announcements` only. Point integrations at the api port `:8080`, never `:8081`. With `Admin__Enabled: "false"`, submitting 404s and only the token-authed now-playing read still answers, so mint the token while admin is on. The token travels over plain HTTP on your network, so keep it on a trusted network. While spectator mode is on, submissions are refused with a 403. The companion [`genwave-homeassistant`](https://github.com/GenWave-Org/genwave-homeassistant) integration provides the `genwave.announce` service and a now-playing sensor.

---

## 🎙️ Voices and packs

Kokoro's voice files live in the named `voices` volume, not in the image. On the first `up`, `voice-seed` copies Kokoro's stock voices in, so a new install can speak with no extra step. It only ever adds files it doesn't find, so a newer Kokoro image never overwrites or removes anything, including a voice pack you installed. The api does not wait for the seed, and a failed seed can't stop the api starting.

The api writes voice-pack files into the same volume at `/voices`. Kokoro reads it read-only and rescans on every request, so a new voice pack is live with no restart. A `--piper-only` box skips all of this.

Jingle packs install under the `authored` volume. Both roots are in the table above.

---

## 🔒 What the fail-safe guarantees

- **The admin plane can be removed, not just locked.** With `Admin__Enabled: "false"` the login form itself is gone: a stray `/api/auth/login` returns 404, never a password prompt. An empty `ADMIN_PASSWORD` fails closed too: every admin endpoint denies and login can never succeed.
- **`:8081` serves the spectator page and `/health` only.** Everything else on it, including admin routes, `/media/*` and `/internal/*`, returns a bare 404 that is indistinguishable from an unmapped route, whatever the flags say.
- **The engine's control port is never published.** It has no password, and Docker's network isolation is its only firewall. To debug it, `docker compose exec engine bash` and talk to `localhost:1234` from inside.
- **The database and the Docker socket have no route from outside.** Postgres is on a private network shared with the api alone. The api sees container stats only through `dockerproxy`, read-only and limited to containers.
- **Spectator responses are hardened.** They carry a strict Content-Security-Policy, `X-Frame-Options: DENY`, `nosniff` and a strict referrer policy.
- **The stream does not stop for software trouble.** If music, speech or the language model fails, the engine falls back to safe-loop segments and keeps broadcasting. A failed migration stops a launch before the new code runs, and a failed upgrade leaves the on-air core alone.

If you put your own reverse proxy in front of the api, set `Proxy:TrustedNetworks` to the proxy's network so the api sees real client addresses. Left empty, every visitor looks like one address and per-visitor rate limits (requests, thumbs) become one shared budget.

---

## 🧯 Troubleshooting

Start here. Run both from the repo folder; `launch.sh` records the compose files it used, so the bare commands match your stack.

```bash
docker compose ps          # Every service should say "healthy" (or "running" if it has no check).
                           # A missing service, or one that says "Restarting", is your suspect.
docker compose logs api    # Start with the API. Read the last error first.
                           # If another service is the suspect, read its log: docker compose logs <service>
```

Then look for your symptom below.

- **"Schema migration failed".** `launch.sh` stops when a migration fails, and the application is not started on a half-built schema. On the published-image flow the message adds that the stack was not restarted onto the new images. The `db` container is left up. Read why with `docker compose logs db`, fix the cause, then run `./migrate.sh` or just `./launch.sh` again. Migrations are idempotent, so repeating them is safe.
- **The box has drifted from what `.env` and the images say.** Run `./setup.sh` for the drift report, then `./setup.sh --repair` to fix what can be fixed, one confirmed item at a time. `launch.sh` has no repair flag of its own.
- **Preflight failed (exit `3`).** Read the printed reason; each one says how to proceed. Common causes are Docker not running, a placeholder secret still in `.env`, or a port already taken. `SKIP_PREFLIGHT=1` is the override. It also helps on a from-source relaunch where the port check fails to recognise the stack's own published ports.
- **Exit `4` after launching.** The core is on air, but a non-core service or profile extra did not converge. Run the catch-up command the summary printed.
- **A setting won't change.** A saved database value outranks the env var. `GET /api/settings` reports `source: "override"` when a stored row is winning and `"default"` when it is not.
- **The spectator page won't appear.** Check `Admin__Enabled`, `Station:SpectatorMode` and that `:8081` is published. See the stale-row note under the modes.
- **Silence at first start.** `MEDIA_DIR` has no `.flac` or `.mp3` files (the extension match is case-insensitive), or the first scan is still running. The station starts silent until music is found.
- **The first catalog build is slow.** Expected on a small box. See the figures under `--piper-only`.
- **Kokoro health probe errors on a `--piper-only` box.** Expected; see above.
- **A container from a dropped profile is still running.** `docker compose rm -fs <service>`.
- **Plugins won't load or the api won't boot after editing `environment:`.** Check `Plugins__Enabled` is exactly `true` or `false`, with no empty value.
