# 📡 GenWave Deployment — Reference Public-Station Topology

One deployment == one station (`compose.yaml`'s own rule). This doc covers the four
operating modes a single deployment can run in, and the reference topology for the
"public station" case: a demo/appliance box reachable from the open internet.

Owner: SPEC F64.3 / STORY-174. Applying this to `demo.genwaveradio.com` is PLAN T20
(manual ops action, not automated here).

---

## 🗺️ The four operating modes

Two independent flags decide what exists; every combination is a valid, supported mode
(SPEC F61, F64 — see `docs/ARCHITECTURE.md` "Operating modes & topology").

| `Admin:Enabled` | `Station:SpectatorMode` | Mode | What runs |
|---|---|---|---|
| `true` | `false` | **Operator** (default) | Admin UI + API; no public spectator surface |
| `true` | `true` | **Standard** | Admin UI + API, *plus* `:8081` for LAN/kiosk viewers |
| `false` | `true` | **Appliance** | Public spectator surface only; admin plane 404s everywhere |
| `false` | `false` | **Headless** | Stream only; zero web surface beyond `/health` |

How each flag is set:

- **`Admin:Enabled`** — env/compose-only, never a live setting (F19.3 register — not on
  `StationSettingsAllowlist`, no API can read or write it). Set via `Admin__Enabled` in the
  `api` service's `environment:` block. Flipping it requires a container recreate.
- **`Station:SpectatorMode`** — a *live* allowlisted setting (`Station:SpectatorMode` in
  `StationSettingsAllowlist`), so it can be flipped two ways: `PUT` through the admin
  settings API/UI while `Admin:Enabled=true`, or seeded at boot via the `Station__SpectatorMode`
  env var (useful when Admin will be disabled before anyone can PUT anything).
- **`COMPOSE_PROFILES`** — not one of the two mode flags, but it decides whether the
  `admin_ui` Next.js container runs at all. `admin_ui` carries `profiles: ["admin"]` in
  `compose.yaml`; the shipped `.env.example` default is `COMPOSE_PROFILES=admin`
  (Operator/Standard). Appliance/Headless boxes clear it — `docker compose up` never starts
  a Node runtime on a public machine. The api-side 404 (`Admin:Enabled=false`) is the
  fail-safe even if `admin_ui` somehow runs anyway.

---

## 📡 Reference public topology (demo / appliance box)

The reference shape: a single public hostname, fronted by Caddy, that can only ever reach
two things — the spectator surface and the stream. Everything else (admin API, `admin_ui`,
`/media/*`, `/internal/*`, the Liquidsoap control port) has no public route at all —
**but only once the overlay below locks it down.** `compose.yaml`'s base `api` service
publishes both `8080` and `8081` on `0.0.0.0` (Operator/Standard want LAN reachability on
both). Running this box off `compose.yaml` alone, without the overlay's `ports:` override,
leaves the anonymous `/media/*` and `/internal/*` groups — whose only real boundary is
network isolation, not auth — reachable from the open internet on `8080`. See the overlay's
`api.ports` block for the fix and why a naive override doesn't do it.

```
                         demo.genwaveradio.com (TLS, Caddy)
                                    │
                 ┌──────────────────┴──────────────────┐
                 │                                      │
            /stream* ─────────────────────────►  icecast:8000/stream
                 │
                /*  ───────────────────────────►  api:8081  (SpectatorSurface + /health ONLY)
                                                       │
                                          (SurfaceGateMiddleware 404s everything
                                           else on :8081, regardless of flags)

  api:8080 (admin API) ── 127.0.0.1 only (overlay's ports: !override), SSH tunnel to reach it
  api:8081             ── NO host publish at all; Caddy reaches it over the `core` network
  admin_ui:3000        ── COMPOSE_PROFILES cleared; doesn't even run on this box
```

`api:8081` is the *only* public spectator listener (SPEC F64.1) — `admin`, `/media/*`,
`/internal/*` structurally do not exist on it no matter what Caddy sends its way. That's
why routing `/*` straight at `:8081` is safe: the second listener is the real safety
boundary, Caddy's path split is belt-and-braces on top of it.

### Caddyfile

```caddyfile
demo.genwaveradio.com {
    # Match the Icecast mount first — mount name is literally /stream (icecast.xml.tmpl).
    handle /stream* {
        reverse_proxy icecast:8000
    }

    # Everything else goes to the public listener. Only SpectatorSurface + /health answer
    # there; nothing else is reachable through this route no matter what path is requested.
    handle {
        reverse_proxy api:8081
    }
}
```

### Compose overlay (`compose.public.yml`)

Adds Caddy onto the existing `core` network — reuses the real service names from
`compose.yaml` (`api`, `icecast`), no new network. Run with:
`docker compose -f compose.yaml -f compose.public.yml up -d`.

```yaml
# compose.public.yml — reference public-station overlay (SPEC F64.3).
# Pins the `core` network's subnet so Proxy:TrustedNetworks below is deterministic instead of
# whatever Docker happens to auto-assign.
networks:
  core:
    ipam:
      config:
        - subnet: 172.28.20.0/24

services:
  caddy:
    image: caddy:2.8-alpine
    networks: [core]
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
    depends_on:
      api:
        condition: service_healthy
      icecast:
        condition: service_healthy
    restart: unless-stopped

  api:
    # compose.yaml's base `ports:` publishes BOTH 8080 and 8081 on 0.0.0.0 — fine for
    # Operator/Standard (LAN reachability is the point there), wrong for a public box.
    # Compose list-type keys MERGE across `-f` files by default: a plain `ports:` override
    # here would APPEND to the base list, not replace it, leaving 0.0.0.0:8080 and
    # 0.0.0.0:8081 published right alongside whatever you add here (verified — see below).
    # The `!override` merge tag (Compose Specification) is what actually replaces the list.
    ports: !override
      - "127.0.0.1:8080:8080"
      # No 8081 entry at all — Caddy reaches api:8081 over the `core` network; a public
      # box has no reason to ever publish it to the host.
    environment:
      # F64.3 topology: Caddy lives on `core` at 172.28.20.0/24 (pinned above) — trust ITS
      # X-Forwarded-For so the spectator rate limiter (RateLimiterPolicies.Spectator) sees the
      # real client IP, not Caddy's. Never widen beyond the fronting proxy's own subnet.
      Proxy__TrustedNetworks__0: "172.28.20.0/24"

volumes:
  caddy_data: {}
```

**Verify the merge before trusting it** — `ports:` merge behavior is exactly the kind of
thing that looks right in the YAML and is wrong on the wire. Don't skip this:

```
$ docker compose -f compose.yaml -f compose.public.yml config
```

Confirmed against the installed Compose version (`docker compose version` → `v5.0.2`).
Without `!override` (plain `ports: ["127.0.0.1:8080:8080"]`), the resolved `api.ports` is
the base list **plus** the override appended — exactly the bug this section exists to
prevent:

```yaml
    ports:
      - mode: ingress
        target: 8080
        published: "8080"
        protocol: tcp
      - mode: ingress
        target: 8081
        published: "8081"
        protocol: tcp
      - mode: ingress
        host_ip: 127.0.0.1
        target: 8080
        published: "8080"
        protocol: tcp
```

With `ports: !override` as shown above, the resolved `api.ports` is exactly one entry —
no `0.0.0.0`, no `8081`:

```yaml
    ports:
      - mode: ingress
        host_ip: 127.0.0.1
        target: 8080
        published: "8080"
        protocol: tcp
```

That second output is what a correct `compose.public.yml` must produce. If your installed
Compose version doesn't understand `!override` (older releases), pin to a Compose version
that does, or replace-in-full by omitting `ports:` from `compose.yaml` and declaring it
only in the topology-specific overlay you actually run — don't ship a public box on an
unverified merge.

### Proxy trust: XFF only, honestly

`Proxy:TrustedNetworks` (T13, `ProxyOptions.cs`) only configures
`ForwardedHeaders.XForwardedFor` today — `Program.cs` never enables
`ForwardedHeaders.XForwardedProto`. Caddy's `reverse_proxy` sends `X-Forwarded-Proto`
automatically, but the api doesn't read it: `HttpContext.Request.Scheme`/`IsHttps` stay
whatever the *internal* hop looked like (plain HTTP on the `core` network), not what the
client actually used. For this reference topology that's low-stakes — admin auth (the one
place `CookieSecurePolicy.SameAsRequest` cares about scheme) never crosses the TLS
boundary, since admin isn't publicly routed at all. It stops being low-stakes the moment
anyone fronts the *admin* plane with TLS too. Documented gap, not fixed here: add
`ForwardedHeaders.XForwardedProto` to the same `ForwardedHeadersOptions` block in
`Program.cs` alongside `XForwardedFor` before doing that.

---

## 🔒 What the fail-safe actually guarantees

Two independent layers, both load-bearing (`docs/ARCHITECTURE.md` "Two planes: existence
vs reachability"):

1. **Reachability** (Caddy + the public listener) — a Caddy misconfiguration (typo'd
   `handle`, a forgotten `/stream*` block, whatever) can at worst route a request to
   `api:8081`. It cannot route it to `api:8080`, `admin_ui:3000`, or the engine's control
   port — those have no public network path to be misrouted onto, **provided the overlay's
   `ports: !override` on `api` actually took effect** (verified above). That's an operator
   action, not a structural guarantee like the surface gate below — check it on every box.
2. **Existence** (`SurfaceGateMiddleware`) — even a request that *does* land on `:8081`
   for something other than the spectator surface gets a bare **404**, indistinguishable
   from an unmapped route. `Admin:Enabled=false` doesn't just deny admin requests, it
   removes the login form itself — a misrouted `/api/auth/login` 404s, it never shows a
   password prompt.

A proxy misroute and a disabled surface fail the same way for a different reason: one
because the request never reaches a place admin exists, the other because admin doesn't
exist regardless of where the request lands. Either alone would be a real boundary; both
together is why a single Caddy typo is not an incident.

The kill switch (`Admin:Enabled`) is **env-only, by specification** — no live PUT can ever
flip it, on purpose. Applying a change to it always means editing the compose environment
and recreating the `api` container, never a settings-panel toggle.

---

## 🧯 Appliance-mode env checklist

For a public/demo box (`compose.public.yml` above), the `api` service environment needs:

- `Admin__Enabled: "false"` — kill switch; admin plane 404s everywhere, both listeners.
- `Station__SpectatorMode: "true"` — turns the spectator surface on (or `PUT` it live
  before flipping `Admin__Enabled`, see above).
- `Station__PublicStreamUrl: "/stream"` — root-relative, since Caddy serves both the page
  and the stream off the same public hostname; the spectator page's `<audio>` element
  points straight at it.
- `COMPOSE_PROFILES=` (empty/unset in `.env`, not `admin`) — `admin_ui` never starts.
- After `docker compose -f compose.yaml -f compose.public.yml up -d`, verify on the host:
  `ss -ltn` must show `127.0.0.1:8080` and must **not** show `0.0.0.0:8080` or any `:8081`
  at all. If it does, the `ports: !override` merge didn't take — see the verification
  above before going further.

Recreate the `api` container after any of these change — they're read at process start
(`Admin:Enabled`) or via `IOptionsMonitor` from the container's own env snapshot
(`Station:SpectatorMode`/`Station:PublicStreamUrl` still take a live `PUT` without a
restart if you'd rather flip them that way while `Admin:Enabled` is still `true`).

**Standard mode / LAN kiosk note:** if you just want `:8081` reachable on the local
network (no public internet, no Caddy) — Standard mode — nothing above applies. Leave
`Admin__Enabled` at its default `true`, set `Station__SpectatorMode: "true"`, and publish
`8081` on the host the same way `8080` already is in `compose.yaml`. A kiosk browser on
the LAN points at `http://<host>:8081/` directly; no reverse proxy needed.
