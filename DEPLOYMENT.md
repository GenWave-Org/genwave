# 📡 GenWave Deployment — Reference Public-Station Topology

> ℹ️ **Since v5.3:** the GHCR image pins moved out of `compose.demo.yaml` into their own
> overlay, `compose.pinned.yaml` (SPEC F136.5). Every pinned/appliance command below is
> now **three** `-f` files — `compose.yaml` + `compose.pinned.yaml` + `compose.demo.yaml`,
> in that order. The old two-file `compose.yaml` + `compose.demo.yaml` form still runs,
> but it silently **source-builds** instead of pulling published images — don't use it.

One deployment == one station (`compose.yaml`'s own rule). This doc covers the four
operating modes a single deployment can run in, and the reference topology for the
"public station" case: a demo/appliance box reachable from the open internet. The
topology itself ships as **`compose.demo.yaml`** + **`Caddyfile`** in this repo, stacked
on the **`compose.pinned.yaml`** image-pin overlay (SPEC F136.5):

```bash
# .env: set PUBLIC_HOST=radio.example.com (plus the usual secrets), clear COMPOSE_PROFILES
docker compose -f compose.yaml -f compose.pinned.yaml -f compose.demo.yaml up -d
```

---

## 🗺️ The four operating modes

Two independent flags decide what exists; every combination is a valid, supported mode.

| `Admin:Enabled` | `Station:SpectatorMode` | Mode | What runs |
|---|---|---|---|
| `true` | `false` | **Operator** (default) | Admin UI + API; no public spectator surface |
| `true` | `true` | **Standard** | Admin UI + API, *plus* `:8081` for LAN/kiosk viewers |
| `false` | `true` | **Appliance** | Public spectator surface only; admin plane 404s everywhere |
| `false` | `false` | **Headless** | Stream only; zero web surface beyond `/health` |

How each flag is set:

- **`Admin:Enabled`** — env/compose-only, never a live setting (no API can read or write
  it). Set via `Admin__Enabled` in the `api` service's `environment:` block. Flipping it
  requires a container recreate — by specification, not limitation.
- **`Station:SpectatorMode`** — a *live* allowlisted setting: `PUT` it through the admin
  settings API/UI while `Admin:Enabled=true`, or seed it at boot via the
  `Station__SpectatorMode` env var (useful when Admin will be disabled before anyone can
  PUT anything). ⚠️ A value saved in the settings DB **overrides** the env var — if the
  surface won't come up despite the env being set, check for a stale DB row.
- **`COMPOSE_PROFILES`** — decides whether the `admin_ui` Next.js container runs at all
  (`admin_ui` carries `profiles: ["admin"]`). `.env.example` defaults to
  `COMPOSE_PROFILES=admin` (Operator/Standard); appliance/headless boxes clear it. The
  api-side 404 (`Admin:Enabled=false`) is the fail-safe even if `admin_ui` runs anyway.

---

## 🎨 Station theme (`Station:Theme`)

A *live* allowlisted setting naming the station's visual theme by **slug**. Set it through
Settings in the Admin UI (a closed dropdown — a typo cannot produce an unresolvable slug),
or seed it at boot via the `Station__Theme` env var.

⚠️ **The env-seeded path is the only one on an appliance box.** With `Admin:Enabled=false`,
`PUT /api/settings` is closed, so `Station__Theme` in the `api` service's `environment:` block
is how a pinned box gets its look — exactly like `Station__SpectatorMode`.

⚠️ **Same DB-overrides-env trap.** A value saved in the settings DB **outranks** the env var
forever. A box whose theme "won't change" despite the env being set has a stale DB row, not a
bad env — `GET /api/settings` reports `source: "override"` when a row is winning and
`"default"` when it is not, which is how you tell the two apart.

**Blank is a legitimate value, and it is the default.** There is deliberately no seed in
`appsettings.json`: the precedence chain terminates at the shipped default structurally, so
seeding a literal would duplicate a value nothing enforces. Resolution order, highest first:

1. the visitor's own `genwave-theme` cookie (their personal choice — set via the switcher on
   either surface: the admin `ThemeSwitcher` control, the spectator `switcher.js`, both shipped
   v3.0.0)
2. the `Station:Theme` settings row
3. the `Station:Theme` env default
4. the shipped default

An unrecognised slug at **any** level falls through to the next rather than erroring, so a
bad value degrades to the shipped default rather than an unstyled page.

> ℹ️ **The dropdown is no longer one option.** GenWave embeds two themes (*Cat's Whisker* +
> *Test Pattern* — the offline floor that resolves with no DB and no catalog), the Community
> Catalog adds more to install, and an owner can mix-and-save their own remix in the theme
> editor (`/editor`) — so setting `Station:Theme` visibly changes the look, on both the admin
> console and the spectator page.

⚠️ Not to be confused with the **`genwave-mode`** cookie, which carries light/dark. Theme and
mode are independent axes: a visitor who chose dark keeps dark when the station's theme
changes under them.

---

## 📡 Reference public topology

One public hostname, fronted by Caddy, that can only ever reach two things — the
spectator surface and the stream:

```
                         ${PUBLIC_HOST} (TLS, Caddy)
                                    │
                 ┌──────────────────┴──────────────────┐
                 │                                      │
            /stream* ─────────────────────────►  icecast:8000/stream
                 │
                /*  ───────────────────────────►  api:8081  (SpectatorSurface + /health ONLY)
                                                       │
                                          (SurfaceGateMiddleware 404s everything
                                           else on :8081, regardless of flags)

  api:8080 (admin API) ── 127.0.0.1 only (ports: !override), SSH tunnel to reach it
  api:8081             ── NO host publish at all; Caddy reaches it over the `core` network
  admin_ui:3000        ── 127.0.0.1 only, and only runs when COMPOSE_PROFILES=admin
  dockerproxy:2375     ── NO host publish; dedicated `stats` network (api ↔ dockerproxy
                          only — caddy/cloudflared/admin_ui have no route). docker.sock
                          mounted read-only behind a CONTAINERS-only allowlist (gh-#148):
                          the api can enumerate containers and read stats, nothing else.
```

`api:8081` is the *only* public spectator listener — admin, `/media/*`, `/internal/*`
structurally do not exist on it no matter what Caddy sends its way. That's why routing
`/*` straight at `:8081` is safe: the second listener is the real safety boundary,
Caddy's path split is belt-and-braces on top of it.

### ⚠️ Verify the ports merge — every box, every time

`compose.yaml`'s base `api` service publishes `8080` **and** `8081` on `0.0.0.0`
(Operator/Standard want LAN reachability). Compose merges list-type keys across `-f`
files by **appending** — a naive `ports:` override would leave the `0.0.0.0` publishes
right alongside it, exposing the anonymous `/media/*` and `/internal/*` groups (whose
only boundary is network isolation) to the internet. `compose.demo.yaml` uses the
`!override` merge tag to *replace* the list. Trust nothing until you've seen the merge:

```bash
docker compose -f compose.yaml -f compose.pinned.yaml -f compose.demo.yaml config
# api.ports must resolve to exactly one entry: host_ip 127.0.0.1, target 8080.
# No 0.0.0.0, no 8081. Then confirm on the host after `up`:
ss -ltn     # 127.0.0.1:8080 and 127.0.0.1:3000, plus caddy on 0.0.0.0:80/:443 — and nothing else;
            # never 0.0.0.0:8080/:8081/:3000
```

If your Compose predates the `!override` tag (needs v2.24+), upgrade — don't ship a
public box on an unverified merge.

### Proxy trust: XFF **and** scheme, both real since v5.5.0

`Proxy:TrustedNetworks` configures **both** `ForwardedHeaders.XForwardedFor` **and**
`ForwardedHeaders.XForwardedProto` (`Program.cs`, T366 review MED-3) — trusting a hop's
`X-Forwarded-For` now also trusts its `X-Forwarded-Proto`, so `Request.Scheme` reflects the
edge's real scheme instead of the plain-HTTP hop Kestrel itself sees behind
cloudflared → Caddy. That is what makes `Secure` real on every scheme-conditioned cookie —
the admin session cookie and the spectator `genwave-listener` cookie both stamp `Secure`
only when the edge was actually TLS; without `XForwardedProto`, `Secure` would never be set
at all on a box behind a plain-HTTP internal hop (or, the other direction, could be falsely
withheld). Empty by default: the middleware's own loopback-only known-networks/known-proxies
trust leaves it inert until an operator sets this.

**Both halves of the chain must trust their hop (gh-#129).** The chain is up to two
proxies deep (cloudflared → Caddy when the optional tunnel fronts the public hostname;
Caddy alone when DNS points straight at the box), and each layer defaults to distrust: Caddy
v2.5+ *strips* inbound `X-Forwarded-For` unless the `Caddyfile` declares
`trusted_proxies` (the shipped one does), and the api's forwarded-headers walk stops
after ONE hop unless `Proxy:TrustedNetworks` is set (which also lifts the hop limit —
the walk then runs to the first untrusted address, the real client). Miss either half
and every public visitor resolves to a container IP: per-IP rate limits (the request
line, the spectator 120/min, the Gardener's own thumbs) collapse into one shared
partition — observed live as cross-IP 429s the day requests launched. Verify after deploy:
a login line's `remote:` must show a real public IP, never `172.x`.

**This is also what makes per-IP partitioning real for the thumb/request lines specifically.**
`Requests`' and the Gardener's `Thumbs` rate-limiter policies both key on
`HttpContext.Connection.RemoteIpAddress` — the same address `Proxy:TrustedNetworks` corrects.
`compose.demo.yaml` sets it (`Proxy__TrustedNetworks__0: "172.28.20.0/24"`, the pinned `core`
subnet); a self-hoster fronting their own box with a proxy and never setting this collapses
every listener's thumbs and requests into ONE shared per-IP partition — a station-wide budget
in practice, not a per-caller one. Concretely: the request line's per-IP daily cap defaults
to 20 (`Requests:PerIpDailyCap`), and the Gardener's thumbs carry their own daily cap
(`Gardener:ThumbDailyCap`, below) — either budget shared by a whole audience instead of one
visitor throttles far sooner than intended, and looks like a broken control, not a busy one.

---

## 🧠 The DJ brain (ollama) on a shared box

`compose.demo.yaml` runs ollama pinned, fenced, and resident — all three matter:

- **Fence (1 CPU / 6GB):** Liquidsoap is a real-time audio process; an unfenced LLM
  generation will starve it and cause audible dropouts. Copywriting is render-ahead and
  cached — the DJ doesn't need speed, playout needs headroom.
- **`OLLAMA_KEEP_ALIVE=-1`:** by default ollama unloads idle models after ~5 minutes, so
  every DJ segment paid a cold model load — which on a fenced CPU blows straight through
  `Llm:TimeoutSeconds`. Resident model = warm generations only.
- **`Llm:TimeoutSeconds`:** even warm, a full persona prompt on one fenced core takes
  ~25–30s. Set the (live) setting to `60` — latency is free, renders are ahead of air.
- The `ollama-init` one-shot pulls `llama3.2:3b` — keep it in lockstep with the
  `Llm:Model` setting, and size the model to the memory fence.
- **`Llm:ReasoningEffort`** (live, default `none`): thinking-capable models (gemma4, qwen3,
  deepseek-r1, magistral) otherwise spend the whole copy budget on chain-of-thought and return
  empty lines — every break falls back to a template while the tile flaps (gh-#620). `none` makes
  them answer directly; `low`/`medium`/`high` let them think; `omit` sends no field at all, for a
  third-party OpenAI-compatible backend that rejects it. Ordinary models ignore the field.

---

## 🔒 What the fail-safe actually guarantees

Two independent layers, both load-bearing:

1. **Reachability** (Caddy + the port lockdown) — a Caddy misconfiguration can at worst
   route a request to `api:8081`. It cannot reach `api:8080`, `admin_ui:3000`, or the
   engine's control port — those have no public network path, **provided the
   `ports: !override` merge actually took effect** (verify above; that's an operator
   check, not a structural guarantee).
2. **Existence** (`SurfaceGateMiddleware`) — even a request that *does* land on `:8081`
   for something other than the spectator surface gets a bare **404**, indistinguishable
   from an unmapped route. `Admin:Enabled=false` doesn't just deny admin requests, it
   removes the login form itself — a misrouted `/api/auth/login` 404s, it never shows a
   password prompt.

Either layer alone would be a real boundary; both together is why a single Caddy typo is
not an incident.

On top of both, the spectator surface itself ships hardened (gh-#180): every spectator
page/asset/API response carries a strict Content-Security-Policy (`default-src 'none'`,
no inline anything; `img-src`/`media-src` follow `Station:PublicBaseUrl` /
`Station:PublicStreamUrl` live, collapsing to `'self'` on empty or invalid config),
plus `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, and
`nosniff`. Non-spectator surfaces are untouched, and a gated surface's bare 404 stays
header-identical to an unmapped route.

---

## 🧙 The wizard and `GW_PRESET` — how a box picks its topology

`./setup.sh` (v5.3.0, SPEC F132) is the first-run path: four questions — prebuilt images or
build from source, where the music lives, which topology, admin on or off — then it generates
every secret, writes `.env` in one atomic step, and hands off to `launch.sh`. On a box that
already has a `.env` it never writes: it verifies the install against the machine (a drift
report) and `--repair` fixes what it can. The one topology fact it persists is
**`GW_PRESET`** in `.env`, a closed set:

| `GW_PRESET` | Files | Shape |
|---|---|---|
| `home` | `compose.yaml` + `compose.pinned.yaml` | published images, LAN station — no demo overlay, no `PUBLIC_HOST` |
| `home-piper-only` | + `compose.piper-only.yaml` | the 4 GB-class topology (Piper primary, no kokoro/ollama) |
| `dev` / `dev-piper-only` | `compose.yaml` (+ piper-only) | the from-source flow |

`launch.sh` is the only reader of the key; an explicit `--pinned` / `--piper-only` flag always
wins over it, and an unrecognised or retired value (`pinned`, `pinned-piper-only`) exits `2`
loudly rather than silently remapping. The **public appliance is flag-only** — `./launch.sh
--pinned` adds `compose.demo.yaml`; no preset does. `GW_PRESET` is not in `.env.example`
(the wizard writes it); set it by hand only if you skip the wizard.

---

## 🧯 Appliance checklist & temporary admin access

Appliance boot (`compose.demo.yaml` defaults):

- `.env`: `PUBLIC_HOST` set, strong `ADMIN_PASSWORD` (empty = admin locked entirely,
  fail-closed) and `ICECAST_ADMIN_PASSWORD` (also guards the listener-stats poll),
  `COMPOSE_PROFILES=` cleared.
- `docker compose -f compose.yaml -f compose.pinned.yaml -f compose.demo.yaml up -d`, then
  run the `config` + `ss -ltn` verification above.
- From a private browser: the page renders at `https://${PUBLIC_HOST}/`, the stream
  plays at `/stream`, and `/api/status`, `/api/auth/login`, `/internal/engine-config`,
  `/media/random` all return **404**.

**Applying migrations** (first boot of a fresh box, or upgrading an already-running
demo box — new images/compose files pulled, schema didn't come along automatically):
use `launch.sh`'s `--pinned` preset (STORY-201), which is exactly this topology's
sanctioned launch/upgrade path — `launch.sh` bare assumes the source-build dev stack,
`--pinned` doesn't:

```bash
./launch.sh --pinned
```

Under the hood `--pinned` runs, against `compose.yaml` + `compose.pinned.yaml` +
`compose.demo.yaml`, never builds, and is **staged** (SPEC F136): the on-air core comes up
first, the heavyweights and profile extras converge afterwards. `./launch.sh --pinned --dry-run`
prints the full plan for your file set — its heart is:

```bash
C="docker compose -f compose.yaml -f compose.pinned.yaml -f compose.demo.yaml"
$C pull db icecast engine api            # stage 1: the core only (+ piper when the fallback profile is active)
$C up -d --no-recreate db                # + poll the db healthcheck (up to 60 s)
./migrate.sh -f compose.yaml -f compose.pinned.yaml -f compose.demo.yaml
$C up -d --remove-orphans --no-deps db icecast engine api   # ── ON AIR ──
$C pull                                  # stage 2: everything else (fast no-op for the core layers)
$C up -d --remove-orphans                # converge every remaining pin + profile-gated extra
docker image prune -af --filter "until=168h"   # success-path hygiene (gh-#441)
docker builder prune -af
```

(The dry-run also shows the bookkeeping between these lines: the db healthcheck poll, the
`COMPOSE_FILE` record into `.env` (gh-#309), a final `ps`, and the pinned-tag report.)

(`--piper-only` runs the flat, unstaged form of the same steps — there are no heavyweights
left to defer.) Exit codes: `0` fully converged · `2` bad invocation · `3` preflight/stage-1
failure, the stack left exactly as it was · `4` stage 2 failed **after** the core went on air
— the printed degradation summary names the catch-up command.

The final prune runs **only after a successful `up`** (a failed upgrade leaves the
previous images untouched — they're what is still running) and keeps everything in use
plus roughly the last week of releases for instant rollback. Without it, superseded
release tags accumulate ~1.5 GB per release forever — 46 GB of dead tags filled the demo
box's disk mid-deploy on 2026-08-09, and an SD-card box hits that wall far sooner.

The `up -d --no-recreate db` step (gh-#305) is what makes a **first** boot work — before
it, `migrate.sh` (which never starts anything by design) had no db to talk to on a fresh
box and the launch deadlocked. On an upgrade it changes nothing: `--no-recreate` leaves a
running db completely untouched, so nothing restarts onto the new images before
migrations pass.

Every `db/*-migration.sh` is an idempotent in-place upgrade (`ADD COLUMN IF NOT EXISTS`
and the like), so running it with nothing new to apply is a safe no-op. `--dry-run`
prints the exact command plan without touching anything — `./launch.sh --pinned --dry-run`
or, for just the migration step, `./migrate.sh --dry-run` (`--keep-going` applies the rest
after one script fails; `--help` for the flags).

Since gh-#19, `launch.sh` **preflights before touching the stack** (Docker running,
compose plugin, `.env` secrets present and non-placeholder) and every failure exit says
how to proceed. On the pinned flow a failed pull or migration explicitly leaves the
running stack alone (exit `3`), and a part-way stage-2 `up` is *not* rolled back — whatever is
still broadcasting keeps broadcasting and the run exits `4` with the converge command named.
`SKIP_PREFLIGHT=1` bypasses the checks in both `launch.sh` and `build.sh` — needed on dev
relaunches until gh-#631 lands (the port check doesn't recognise the stack's own published
ports when docker collapses them into a range). It never touches tests: `SKIP_TESTS=1` is
the separate knob that skips `build.sh`'s pre-image test run.

Combine with `--with` to also activate compose profiles (e.g. `logging`, `tunnel`) on the
same launch: `./launch.sh --pinned --with logging,tunnel` merges them into whatever
`COMPOSE_PROFILES` is already set (env or `.env`).

### After a launch, bare `docker compose` matches it (gh-#309)

`--pinned` runs against `compose.yaml` **+** `compose.pinned.yaml` **+**
`compose.demo.yaml`, but a bare `docker compose down` in this directory loads only
`compose.yaml` — so every service that exists solely in an overlay (`caddy`, `ollama`,
`ollama-init`) was invisible to it, survived the teardown, and was left running.

`launch.sh` now records the file stack it used as `COMPOSE_FILE` in `.env` after a
successful `up`. Compose reads that from the project directory automatically, so
`down`/`ps`/`logs` all target what was actually launched, with no flags to remember:

```bash
./launch.sh --pinned          # writes e.g. COMPOSE_FILE=compose.yaml:compose.pinned.yaml:compose.demo.yaml
docker compose down           # now tears down caddy + ollama too
```

Explicit `-f` flags still outrank the variable, so the fully-spelled commands elsewhere in
this doc behave exactly as written.

⚠️ **Profiles are a separate axis.** `COMPOSE_PROFILES` is deliberately *not* persisted —
`--with` is per-launch by design — so a bare `down` can still leave profile-gated
containers (`admin_ui`, `piper`, `cloudflared`, `alloy`) behind. Use `--remove-orphans`, or set a
standing `COMPOSE_PROFILES` in `.env` (which `launch.sh` already reads as the base for
`--with`).

### TTS failover: opt-in (SPEC F99.2/F99.3, STORY-257)

The shipped default runs **no** Piper fallback sidecar and configures **no** fallback
chain — a Kokoro failure drops the affected break rather than substituting a different
voice (right voice or no speech, SPEC F99.1). `piper` sits behind
`profiles: ["fallback"]` in `compose.yaml`, off by default same as `admin`/`tunnel`/`logging`.

Opting in needs both halves:

```bash
./launch.sh --with fallback          # or COMPOSE_PROFILES=fallback in .env — starts piper
```

then a live `PUT /api/settings` for `Tts:Fallback:Endpoint` (`http://piper:5000` on this
stack) — no restart needed, `FallbackTtsSynthesizer` reads it per render. This applies to
every install, including existing ones (see ARCHITECTURE.md "The deleted default" for the
ruling and its recorded expiry).

The piper-only topology below is unrelated to this mechanism: there Piper is the PRIMARY
engine (SPEC F99.4), not an opt-in fallback, and always runs.

> ⚠️ **Deselecting `fallback` on `--pinned` leaves the old `piper` container running —
> stop it by hand, once.** `./launch.sh --pinned` now passes `--remove-orphans` to its
> `up -d` (T148 review finding F6), but that flag only removes containers for services no
> longer **defined** in the file stack at all — verified empirically (`docker-linux-ops`,
> live daemon): a service that is still defined but merely profile-gated OFF (`piper`
> dropping out of `--with fallback`/`COMPOSE_PROFILES`) is **not** an "orphan" to Compose,
> so a previously-opted-in `piper` container survives every subsequent `--pinned` launch
> untouched, still running, still (harmlessly) idle. The same is true switching topology
> the other way (`--piper-only` on ↔ off) for `kokoro`/`ollama`/`ollama-init`. One-time fix
> after any such change: `docker compose rm -fs piper` (`-s` stops it first, `-f` skips the
> confirmation) — or the equivalent service name for the topology switch you made.

### Low-memory topology: `--piper-only` (gh-#242, gh-#310)

For a 4GB-class box (Raspberry Pi, small VPS), `--piper-only` merges
`compose.piper-only.yaml` last onto whichever file set the flow uses:

```bash
./launch.sh --pinned --piper-only
```

It removes **kokoro** (its ~1.2GiB resident baseline is the single biggest tenant) and,
as of gh-#310, the **`ollama` + `ollama-init` pair** as well — the demo overlay held
`llama3.2:3b` permanently resident behind a fence larger than a 4GB box's entire RAM.
Every TTS render routes to the piper sidecar; the LLM path degrades to templated patter,
which is exactly what [HARDWARE.md](HARDWARE.md)'s topology (a) describes. See that file
for the ranked hardware topologies.

> ⚠️ **Expected on every piper-only box — not a broken install.** The api logs
> `kokoro health probe failed (Name or service not known (kokoro:8880)) — 2 consecutive
> failures, cached verdict is now unhealthy`, with a stack trace, and then goes quiet
> (gh-#338 made the warning edge-triggered). `Tts:Endpoint` **deliberately** stays pointed
> at the absent kokoro host: it cannot point at piper (kokoro-fastapi speaks
> `POST /v1/audio/speech`, piper's server speaks `POST text/plain → /`) and it cannot be
> emptied (`TtsOptions.Endpoint` is `[Required]`). The dead hostname is the mechanism, not
> the fault — it is fully quarantined to Kokoro's own health probe / voice lister now (SPEC
> F99.4, STORY-257): **Piper is the topology's PRIMARY engine directly** via
> `Tts:PiperPrimaryEndpoint`, so every render — kind-carrying or not — goes straight to
> Piper with no NXDOMAIN dance and no fallback chain involved at all. `GET /api/voices`
> still returns a 502 (Kokoro's own voice list, unreachable) for the same reason. The
> broadcast never depends on any of it. Full rationale lives in `compose.piper-only.yaml`.

As of gh-#334 it also halves **`Library:EnrichmentConcurrency` to 2**. Enrichment is the
heaviest sustained load GenWave produces — the ffmpeg analyzers use every core they are
given — and the base default of 4 pins all four cores of a small box for the whole
first-boot catalog build. Override on either stack with the env var:

```bash
LIBRARY_ENRICHMENT_CONCURRENCY=1   # in .env — lower still, or raise to reclaim throughput
```

**Not proportional** — both rates measured on the same 4GB Pi 5: **~800 tracks/hour at 4**
and **~700 at 2**. A 9,000-track library is therefore ~11h at 4 and **~13h at 2 — not the
~22h a linear model predicts**. Halving concurrency costs far less throughput than it
looks like it should; the analyzers are not purely CPU-bound (NFS media reads, catalog
writes), so two workers still keep the box busy. The concurrency-2 figure is a 9-hour
sustained run, 2026-08-03: 6,303 tracks at a flat ~700/hour with no gaps.
Enrichment is a backfill, not a broadcast dependency — the
station is on air throughout, so this trades catalog-build time for headroom, nothing more.

> ⚠️ **On a pinned appliance box the env var is the only lever.** `compose.demo.yaml` sets
> `Admin__Enabled: "false"`, which closes `PUT /api/settings` — so the live settings path
> this knob otherwise supports (it is an allowlisted live setting, no restart needed) is
> unreachable there. Set it in `.env` and re-`up` the api.

**Temporary admin access** (settings, personas, catalog curation on the public box):

1. Edit `compose.demo.yaml`'s `api` env: `Admin__Enabled: "true"`.
2. `COMPOSE_PROFILES=admin docker compose -f compose.yaml -f compose.pinned.yaml -f compose.demo.yaml up -d`
   — recreates `api`, starts `admin_ui` on loopback only.
3. Tunnel in: `ssh -L 3000:127.0.0.1:3000 you@your-box` → `http://localhost:3000`. If
   Cloudflare Access is fronting this box (see "Zero Trust Access (optional)" below),
   prefer that route for routine admin — this SSH tunnel stays as break-glass only.
   **Home Assistant users: mint (or rotate) the announce token now**, on the Announcements
   page — it is the only credential that survives the plane going back off (see "The House
   Voice" below).
4. When done, revert the flag and re-`up` without the profile. The public surface is
   unaffected throughout — spectators never notice.

**Standard mode / LAN kiosk note:** for `:8081` on the local network only (no public
internet, no Caddy), none of this file applies — leave `Admin__Enabled` at its default
`true`, set `Station__SpectatorMode: "true"`, and point a kiosk browser at
`http://<host>:8081/` (compose.yaml already publishes 8081 for exactly this).

---

## 🏠 The House Voice — announcements and the announce token (v5.4.0/v5.4.1, SPEC F143–F147)

Owner announcements let the DJ work a line into the next break (in character, or read
**verbatim** — and always verbatim when the in-character copy fails the truth gate). They are
a durable unit of content (`station.announcement`, db/40) with a visible lifecycle; nothing the
pipeline touches is ever deleted.

**Deploy knobs — env/compose only, not live settings** (`Announcements__*`, boot-validated).
⚠️ *Env/compose-only* here (and for every `Gardener__*` knob below) means the **`api`
service's `environment:` block** — on a pinned appliance, a small local overlay file. The
root `.env` alone never reaches a container unless `compose.yaml` interpolates that exact
variable, and none of these are interpolated:

| Key | Default | What it bounds |
|---|:---:|---|
| `Announcements__MessageMaxChars` | 280 | a longer message is a 400 |
| `Announcements__AcceptedPerMinute` | 6 | station-wide, across both doors (cookie and token); a refusal never spends it |
| `Announcements__PendingDepthCap` | 12 | pending + claimed rows; deeper is a 429 |

**The announce token** — for automations (Home Assistant) that must not hold the admin cookie:

- Mint on the Announcements page, or `POST /api/announcements/token` (cookie session only).
  **Reveal-once**: the plaintext is returned exactly once; only its SHA-256 lands in the
  settings row `Announcements:TokenHash` (machine-written, never listed by the settings API).
  `POST` again to rotate, `DELETE` to revoke — a revoked token is refused on the very next
  request (fresh read per call, no cache). `GET /api/announcements/token/status` reports
  whether one exists and `Announcements:TokenLastUsedAt`.
- The token is a `Bearer` on **`/api/announcements` only** (submit, history, and the
  token-authed now-playing read the sensor uses); a fitness law fences the scheme to exactly
  those two controllers, so it can never promote to the admin planes.
- ⚠️ **Reachability — read before wiring HA (ruled v5.4.1, SPEC F145.6).** Submit, history
  and the token endpoints are admin-surface: with `Admin__Enabled: "false"` (the reference
  public topology's default) they 404. **The token-authed now-playing read is the one
  exception** — it answers whenever a token exists, admin plane on or off, public or private,
  so the Home Assistant sensor works on a shipped appliance; `genwave.announce` does not until
  the plane is on (the call fails with "Not Found" in the automation trace, no reauth). Mint the
  token during the temporary-admin window (step 3 above) before turning the plane back off. The
  read is not a spectator route: point the integration at the **api port** (`:8080`), never the
  public listener port, or it 404s. The api itself must be reachable from the HA box — on the
  reference topology it binds to `127.0.0.1:8080`, so a same-host HA or a deliberate LAN bind is
  the operator's call.
- ⚠️ **Transport.** The token's guarantee equals the admin cookie's (F145.7): on the
  Operator/Standard modes the api listens on `0.0.0.0:8080` over plain HTTP, so the token
  crosses your LAN in the clear — keep it on a trusted network, or front the api with TLS
  (Caddy) before pointing an integration at it from anywhere else.
- **A public station never carries the house's events**: while `Station:SpectatorMode` is
  on, submissions are refused with a 403 and pending rows are declined at the flip — the
  demo station can never demo this by design.

**Home Assistant**: the companion [`genwave-homeassistant`](https://github.com/GenWave-Org/genwave-homeassistant)
integration (HACS custom repository, MIT; HA 2025.3.0+) takes the station URL and the token,
and provides the `genwave.announce` service (`message`, `verbatim`, `ttl_seconds`, `voice`), a
`notify` entity, `sensor.now_playing` (title, artist, DJ), and a blueprint gallery — dinner
bell, laundry done, morning ramp. Gated live 2026-08-28: a blueprint ring reached the air in
one break cycle.

---

## 📚 The library scan — a moved root is quarantined, not fed to the engine (gh-#611/#612)

A catalog row whose path lies outside the current `Library:MediaRoot` can never be
re-verified by the scan (the classic cause: the library was once scanned under a different
mount, so every file exists twice and half the picks point nowhere). Since v5.4.0 such rows
are **quarantined** (`state = unavailable`, out of rotation) after `Library:Scan:MissThreshold`
consecutive scans (default 2 — the same miss grace a vanished file gets, SPEC F58), and
resurrect through normal discovery if the root moves back. Roots that legitimately live
outside `MediaRoot` are exempt via `Library:Scan:QuarantineExemptRoots` (default `/authored`,
the authored-segments volume) — a deployment that relocates that volume must update this and
`Station:Safe:AuthoredRoot` together. Independently, every push now checks the file exists
first and declines with a WARN if not, and a chain that was pushed but never aired is surfaced
rather than silent. Per-service memory fences for every container (kokoro 4 GB, piper 768 MB,
alloy 256 MB, cloudflared 128 MB, dockerproxy 64 MB, and — demo overlay only — ollama 1 CPU /
6 GB) live in HARDWARE.md's "What each service needs" table — one source, not two.

---

## 🌱 The Library Gardener (v5.5.0, SPEC F150–F155, gh-#529)

The Gardener is a housekeeping `BackgroundService` that tends the catalog on a timer:
five rot passes (`dead_file` / `near_duplicate` / `stale_metadata` / `shelf_dust` /
`unreachable`) reconcile findings into a queue an operator works from the Gardener page
(since v5.5.1: five badged kind tabs, server-paged `?tab=&page=&limit=` — default 25,
picker 25/50/100/250, near-duplicates always paged by whole cluster), and a
listener-thumbs signal nudges rotation toward what actually gets played. Schema is
**db/41** (`library.media_rotation`, `media_thumb`, `rot_finding`, `file_action` + the
`rot_kind`/`rot_state` enums) — `migrate.sh` applies it on the way up, so a `--pinned`
upgrade runs one migration here. Every knob below is `Gardener__*` — env/compose-only
(the api `environment:` block, per the House Voice note above), never a live setting —
**except**
`Station:Thumbs:Enabled`, the one Live switch (see below). The ten top-level knobs are
boot-validated (`ValidateDataAnnotations()`): an out-of-range value refuses to start. The
two `FileActions__*` rows are NOT — `DataAnnotations` validation doesn't recurse into a
nested options class, so `Enabled` (a plain bool) is just read, and an out-of-range
`GateTimeoutSeconds` is silently clamped to 1–300 at use rather than rejected at boot; the
range column for those two rows is documentation only.

| Key | Default | Range | What it bounds |
|---|:---:|:---:|---|
| `Gardener__IntervalMinutes` | 60 | 1–1440 | Minutes between `GardenerService` ticks (thumb sweep + every registered pass) |
| `Gardener__BatchSize` | 500 | 1–10,000 | Reserved — no shipped pass consults it yet. Every pass today reconciles set-based in SQL; only a future *iterative* pass would ever read this |
| `Gardener__NudgeGain` | 0.5 | 0–2 | Multiplies the rotation nudge into the persona ranker's rung-0 score term; 0 disables the rotation signal outright |
| `Gardener__HalfLifeDays` | 30 | 1–365 | Exponential half-life, in days, for a single thumb's contribution to the nudge |
| `Gardener__Saturation` | 5 | 1–100 | Divisor that normalizes the age-decayed thumb sum into the nudge's clamped [-1, 1] range |
| `Gardener__ThumbCooldownSeconds` | 30 | 1–3600 | Per-IP cooldown on the `thumbs` route rate limiter |
| `Gardener__ThumbDailyCap` | 60 | 1–10,000 | Per-IP **and** per-listener daily cap on accepted thumb posts |
| `Gardener__ThumbRetentionDays` | 90 | 1–3650 | Age past which `library.media_thumb` rows are swept; the lifetime up/down counters and the computed nudge survive the sweep |
| `Gardener__ShelfDustDays` | 90 | 1–3650 | Days since discovery, with zero plays, before a playable row is flagged `shelf_dust` |
| `Gardener__DuplicateToleranceMs` | 2000 | 0–60,000 | Duration tolerance for the `near_duplicate` grouping, anchored to the group's shortest member |
| `Gardener__FileActions__Enabled` | `false` | — | The file-actions opt-in (see below) |
| `Gardener__FileActions__GateTimeoutSeconds` | 30 | 1–300 | How long a file action waits to enter the shared scan gate before reporting Busy |

**`Station:Thumbs:Enabled`** — a *live* allowlisted setting: `PUT` it through the settings
API/UI, or seed it at boot via `Station__Thumbs__Enabled` in the api service's
`environment:` block. Default off — disabled means
`POST /spectator/api/thumbs` 404s and the spectator page shows no thumbs controls at all,
never a distinguishable "thumbs are closed" response. Takes effect on the very next
request, no `api` restart.

### File actions: opt-in, and the only feature that writes to media

File actions (retag / rename / move — **there is no delete verb**) are OFF by default
(`Gardener__FileActions__Enabled`, F154.2's fail-closed posture on a stranger's NAS).
Opting in needs both halves:

```bash
# knob 1 — the setting, in the api service's environment: block (the root .env alone
#          never reaches the container — on a pinned box use a small local overlay):
#          services: { api: { environment: { Gardener__FileActions__Enabled: "true" } } }
# knob 2 — the mount, via the shipped overlay:
docker compose -f compose.yaml -f compose.pinned.yaml -f compose.demo.yaml \
  -f compose.local.yaml -f compose.fileactions.yaml up -d
```

`launch.sh` has no flag for extra compose files today — run the full `-f` chain above
directly, or launch normally first and append `:compose.fileactions.yaml` to `COMPOSE_FILE`
in `.env` (the gh-#309 mechanism above), so every later bare `docker compose` picks it up
too. `compose.fileactions.yaml` stacks in either order relative to the pin/demo overlays,
and touches only the `api` service's own `/media` mount — widened to `:rw` (see its own
header for the mechanism).

Both knobs are independent, and missing one is diagnosable: with the mount still `:ro`
(this file not stacked), `dry-run` still plans to a 200 — the planner never touches disk —
but `confirm` always fails at execution instead of completing (the OS refuses the write
before anything is written, so the outcome is `failed`, never `reverted` — there's nothing
to revert — and never a clean `done`). With the compose file stacked but `Enabled` left
off, the endpoints just 404, mount notwithstanding.

⚠️ **`launch.sh` does not remember this overlay.** It rewrites the whole `COMPOSE_FILE=`
line in `.env` on its own next successful `up` (the gh-#309 mechanism above), so a manually
appended `:compose.fileactions.yaml` is silently dropped and the mount quietly reverts to
`:ro` — re-append it after every `launch.sh` run. The confirm-fails-at-execution signature
above is how you notice.

Every write is jailed under `Library:MediaRoot`, never overwrites an existing path, and a
retag never touches the live file directly: the original is copied to a same-directory
temp file, tagged, then swapped in behind a same-directory `.gwbak` backup of the original.
On success the backup is deleted. **If a revert itself fails** (the backup can't be moved
back over a failed write), the `.gwbak` is left beside the file as the only surviving
original — every further retag on that file refuses (`LeftoverBackup`) until an operator
resolves it manually. Resolving it by hand: `mv 'track.mp3.<suffix>.gwbak' 'track.mp3'`
restores the pre-retag original over whatever the failed attempt left behind; once you've
confirmed the current file is the one you want, deleting the `.gwbak` instead is enough —
either way, the next retag on that file proceeds the moment no `.gwbak` sibling remains.

---

## ☁️ Cloudflare tunnel (optional)

An alternative to the Caddy topology above: instead of publishing anything on the host at
all, a [Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/)
connector reaches out from inside the `core` network to Cloudflare's edge, which then
routes your public hostname back to it. This used to run **outside the repo** as
hand-maintained, unversioned infrastructure with no observability; it's now the optional
`cloudflared` service in `compose.yaml`, off by default.

### Enabling it

1. In the Cloudflare Zero Trust dashboard: **Networks → Tunnels → Create a tunnel**
   (remote-managed). Add a public hostname pointing at whichever service you want exposed
   (e.g. `api:8081` for the spectator surface, `icecast:8000` for the raw stream).
2. Copy the connector token from **Configure → Install and run a connector** into `.env`:
   `TUNNEL_TOKEN=...`.
3. Add `tunnel` to `COMPOSE_PROFILES` (e.g. `COMPOSE_PROFILES=admin,tunnel`, or just
   `tunnel` on a headless box) and `docker compose up -d cloudflared` (or a full `up -d`
   — every other service is unaffected).

`TUNNEL_TOKEN` is deliberately NOT `${TUNNEL_TOKEN:?}` like this file's other secrets —
that form breaks `docker compose config` even with the profile inactive (compose
interpolates every service's environment before filtering by profile). Leaving it blank
is safe when the profile is off; the container itself refuses to run and exits
immediately with a clear log line if the profile is active with a blank or invalid token.

### What `/ready` and `/metrics` give you

cloudflared's own metrics server (bound to `2000` inside the `core` network, never
published on the host) exposes:

- **`/ready`** — JSON readiness: HTTP 200 plus the number of active edge connections once
  the tunnel has registered at least one. This is what the container healthcheck uses
  (`cloudflared tunnel --metrics 127.0.0.1:2000 ready`, cloudflared's own readiness
  subcommand — the image is distroless with no shell, so there's no `curl`/`wget` to
  reach for here).
- **`/metrics`** — Prometheus text format: connection counts, request/response stats,
  build info, and more.

### Checking health

```bash
docker compose ps cloudflared          # healthy / unhealthy / starting, same as any other service
docker compose logs cloudflared        # connector registration, edge location, any errors
```

To probe the endpoints directly from another container on the `core` network (there's no
host port to hit from outside):

```bash
docker compose exec cloudflared cloudflared tunnel --metrics 127.0.0.1:2000 ready
docker compose exec api curl -fsS http://cloudflared:2000/metrics | head
```

Opting into a host-side probe is a deliberate, local-only change — never commit it:
```yaml
    ports: ["127.0.0.1:2000:2000"]   # loopback only; add locally if you want to curl from the host
```

### Restart posture

`restart: unless-stopped`, same as every other service — same posture, no independent
supervision. A crashed connector (bad token, network blip) restarts automatically;
`docker compose logs cloudflared` shows why it crashed if it keeps doing so.

### Alerting — an honest note

Nothing in this repo pages you on tunnel failure today. Two ways to close that gap
yourself, in increasing order of effort:

- **Scrape `/metrics`** with your own Prometheus (or any metrics collector) pointed at
  `cloudflared:2000` from inside the `core` network, and alert on it there.
- **Probe the public hostname from outside**, the same way `.github/workflows/
  demo-health.yml` polls the demo station's `/health` on a schedule and lets GitHub
  Actions email the org on failure — point an equivalent scheduled probe at whatever
  route the tunnel exposes publicly (e.g. `/health` if it's fronting the api).

Either is a few lines to wire up; neither ships by default, so silence from this stack
does not by itself mean the tunnel is up.

### Zero Trust Access (optional)

Cloudflare Access can front a tunnel's public hostnames with authentication before a
request ever reaches a service — same tunnel, an extra gate in front of it. Generic
pattern only (SPEC F78.11): no real hostnames, zones, or tokens belong in this repo;
concrete apps/policies/hostnames live in the operator's private infra repo, never here.

Two Access application shapes cover this stack's two audiences:

- **Human app** (admin UI / Grafana) — tunnel public hostname (e.g.
  `admin.radio.example.com`) → service (`admin_ui:3000`; same shape for a Grafana
  hostname pointed at `grafana:3000` in the observability stack). Access self-hosted app
  on that hostname, allow policy = an email allowlist, login methods **Google** (primary)
  **+ One-Time PIN** (fallback). Both matter: a single login method means an IdP
  hiccup — Google outage, misconfigured SSO — is a lockout, not an inconvenience; the PIN
  fallback keeps the door open.
- **Machine app** (Loki push) — tunnel public hostname (e.g.
  `loki.homelab.example.com`) → `loki:3100`. Access policy = **Service Auth**
  (non-identity), backed by a service token. The client authenticates by sending
  `CF-Access-Client-Id` / `CF-Access-Client-Secret` headers with every push request —
  exactly what the `alloy` logging profile does, sourced from the `LOKI_ACCESS_CLIENT_ID`
  / `LOKI_ACCESS_CLIENT_SECRET` env vars — alongside **`LOKI_PUSH_URL`** (the push target;
  alloy refuses to start, exit 1, while it is empty — SPEC F78.4) and the label pair
  `ALLOY_STATION_LABEL` / `ALLOY_ENV_LABEL` (`compose.yaml`'s `alloy` service; none of the
  five are in `.env.example` — add them to `.env` when you enable the profile; header
  attachment lives in `observability/alloy/config.alloy`; label contract in
  `observability/LABELS.md`).

Verification recipes (curl-able — these are the observable contracts, SPEC F78.6/F78.7):

```bash
# Human app: unauthenticated GET redirects to the Access login, never app bytes
curl -sI https://admin.radio.example.com/ | grep -i '^location:'
# -> 302 to https://<team>.cloudflareaccess.com/... (never a 200 with page markup)

# Machine app: push without token headers is rejected at the edge, before Loki sees it
curl -sI -X POST https://loki.homelab.example.com/loki/api/v1/push
# -> 403

# Machine app: push with valid token headers succeeds
curl -sI -X POST https://loki.homelab.example.com/loki/api/v1/push \
  -H "CF-Access-Client-Id: <CF_ACCESS_CLIENT_ID>" \
  -H "CF-Access-Client-Secret: <CF_ACCESS_CLIENT_SECRET>"
# -> 204
```

With Access in front, routine admin work (settings, personas, catalog curation) needs no
SSH tunnel at all — hit the Access-gated hostname directly instead. Plain SSH (see
"Temporary admin access" above) stays the **break-glass** route when Access itself is
unreachable: `api:8080` remains loopback-published either way, unaffected by whether
Access fronts anything.

### Limits of the Access gate — an honest note

Access is enforced **at Cloudflare's edge, on the tunnel hostname only**. Two consequences
worth stating plainly:

- **The origin trusts topology, not tokens.** Nothing in the api validates the
  `Cf-Access-Jwt-Assertion` JWT Access attaches to authenticated requests. A caller that
  reaches Caddy/api over the LAN gets the real login form with Access never consulted, and
  a misrouted tunnel hostname or deleted Access app silently drops the gate. The admin
  password is therefore still load-bearing on every non-edge path — treat it accordingly.
  Origin-side JWT validation (config-gated, fail-closed) is
  [gh-#75](https://github.com/GenWave-Org/genwave/issues/75).
- **Failed logins DO log who was at the door** (gh-#74, shipped v2.3.1): every login
  outcome records the caller's remote IP (XFF-corrected behind a trusted proxy) and the
  `Cf-Access-Authenticated-User-Email` header when Access forwarded one — so "was that
  the operator or an intruder?" is answerable from the api's own logs. Note the Access
  identity is logged, not validated (the first bullet still applies).

Record-keeping: concrete apps, policies, and hostnames live in the operator's private
infra repo, never in this one (SPEC F78.11).
