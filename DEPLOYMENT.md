# 📡 GenWave Deployment — Reference Public-Station Topology

One deployment == one station (`compose.yaml`'s own rule). This doc covers the four
operating modes a single deployment can run in, and the reference topology for the
"public station" case: a demo/appliance box reachable from the open internet. The
topology itself ships as **`compose.demo.yaml`** + **`Caddyfile`** in this repo:

```bash
# .env: set PUBLIC_HOST=radio.example.com (plus the usual secrets), clear COMPOSE_PROFILES
docker compose -f compose.yaml -f compose.demo.yaml up -d
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
docker compose -f compose.yaml -f compose.demo.yaml config
# api.ports must resolve to exactly one entry: host_ip 127.0.0.1, target 8080.
# No 0.0.0.0, no 8081. Then confirm on the host after `up`:
ss -ltn     # 127.0.0.1:8080 and 127.0.0.1:3000 only — never 0.0.0.0:8080/:8081/:3000
```

If your Compose predates the `!override` tag (needs v2.24+), upgrade — don't ship a
public box on an unverified merge.

### Proxy trust: XFF only, honestly

`Proxy:TrustedNetworks` only configures `ForwardedHeaders.XForwardedFor` today —
`X-Forwarded-Proto` is not read, so `Request.Scheme` stays plain HTTP on the internal
hop. Low-stakes for this topology (admin auth never crosses the TLS boundary — admin
isn't publicly routed at all), but it stops being low-stakes the moment anyone fronts
the *admin* plane with TLS. Documented gap: enable `XForwardedProto` in the same
`ForwardedHeadersOptions` block before doing that.

---

## 🧠 The DJ brain (ollama) on a shared box

`compose.demo.yaml` runs ollama pinned, fenced, and resident — all three matter:

- **Fence (1 CPU / 3GB):** Liquidsoap is a real-time audio process; an unfenced LLM
  generation will starve it and cause audible dropouts. Copywriting is render-ahead and
  cached — the DJ doesn't need speed, playout needs headroom.
- **`OLLAMA_KEEP_ALIVE=-1`:** by default ollama unloads idle models after ~5 minutes, so
  every DJ segment paid a cold model load — which on a fenced CPU blows straight through
  `Llm:TimeoutSeconds`. Resident model = warm generations only.
- **`Llm:TimeoutSeconds`:** even warm, a full persona prompt on one fenced core takes
  ~25–30s. Set the (live) setting to `60` — latency is free, renders are ahead of air.
- The `ollama-init` one-shot pulls `llama3.2:3b` — keep it in lockstep with the
  `Llm:Model` setting, and size the model to the memory fence.

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

---

## 🧯 Appliance checklist & temporary admin access

Appliance boot (`compose.demo.yaml` defaults):

- `.env`: `PUBLIC_HOST` set, strong `ADMIN_PASSWORD` (empty = admin locked entirely,
  fail-closed) and `ICECAST_ADMIN_PASSWORD` (also guards the listener-stats poll),
  `COMPOSE_PROFILES=` cleared.
- `docker compose -f compose.yaml -f compose.demo.yaml up -d`, then run the `config` +
  `ss -ltn` verification above.
- From a private browser: the page renders at `https://${PUBLIC_HOST}/`, the stream
  plays at `/stream`, and `/api/status`, `/api/auth/login`, `/internal/engine-config`,
  `/media/random` all return **404**.

**Temporary admin access** (settings, personas, catalog curation on the public box):

1. Edit `compose.demo.yaml`'s `api` env: `Admin__Enabled: "true"`.
2. `COMPOSE_PROFILES=admin docker compose -f compose.yaml -f compose.demo.yaml up -d`
   — recreates `api`, starts `admin_ui` on loopback only.
3. Tunnel in: `ssh -L 3000:127.0.0.1:3000 you@your-box` → `http://localhost:3000`.
4. When done, revert the flag and re-`up` without the profile. The public surface is
   unaffected throughout — spectators never notice.

**Standard mode / LAN kiosk note:** for `:8081` on the local network only (no public
internet, no Caddy), none of this file applies — leave `Admin__Enabled` at its default
`true`, set `Station__SpectatorMode: "true"`, and point a kiosk browser at
`http://<host>:8081/` (compose.yaml already publishes 8081 for exactly this).

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
