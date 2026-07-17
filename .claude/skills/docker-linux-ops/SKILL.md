---
name: docker-linux-ops
description: >-
  Docker and Linux operations for containerized .NET services:
  multi-stage Dockerfiles (sdk build → aspnet runtime), Docker Compose
  orchestration (depends_on with healthchecks, named volumes, networks,
  .env files), bind mounts and NFS-mounted media libraries (stat-based
  change detection, open-file delete placeholders, case sensitivity),
  graceful shutdown (SIGTERM, stop_grace_period vs host
  ShutdownTimeout), container healthchecks, log inspection via docker
  compose logs, and debugging a service that works locally but not in
  the container. Use when writing or reviewing a Dockerfile or compose
  file, diagnosing container startup/networking/volume issues, wiring
  health probes, or answering "why does this behave differently in
  Docker" questions. Ships a multi-stage .NET Dockerfile and a compose
  template.
---

# Docker & Linux Ops for .NET Services

How to build, compose, run, and debug containerized .NET services on
Linux — and how to not get surprised by the gap between "works on my
machine" and "works in the container".

## 🎯 Why: Design for Change

The goal of writing software is to be able to **change it safely** —
including its runtime. A deterministic multi-stage build, explicit
healthchecks, bounded shutdown windows, and config-via-environment mean
a new service or a version bump is a compose edit, not an archaeology
dig. Implicit host dependencies (paths, casing, local daemons) are
change-hiders that detonate on deploy.

This skill is about *runtime and ops*. App-level structure is
`aspnetcore-patterns`; secrets-in-config rules are `security-api`.

## House workflow

The project pattern this skill assumes:
- **`build.sh`** (repo root, no parameters) builds the solution and the
  Docker images. Don't hand-roll `docker build` invocations when the
  script exists — extend the script.
- **`genwave.sh start main` / `genwave.sh stop main`** controls the
  container lifecycle. Same rule: lifecycle changes go *into* the
  script, so the documented entry point stays true.
- Inspect before restarting: `docker compose ps`, `docker compose logs
  --tail 100 <service>`, then decide. A restart that "fixes" something
  you didn't diagnose will fix it again at 3 AM.

## Decision guide

| Symptom / question | Where |
|---|---|
| Writing/reviewing a Dockerfile for a .NET service | templates/Dockerfile.dotnet + §1 |
| Compose service won't start in order / races the DB | §2 (healthcheck + depends_on condition) |
| Media library on NFS behaves oddly (scans, deletes) | §3 |
| Container killed before it finishes shutdown work | §4 |
| Works locally, fails in container | §5 |
| Postgres init/env drift, stale volumes | §6 |
| Health probes | §2 + aspnetcore-patterns §5 |

## §1 Images: multi-stage, slim, non-root

- Build with the `sdk` image, run on `aspnet` (or `runtime-deps` for
  self-contained/AOT). The runtime image never contains the SDK,
  source, or test artifacts. See `templates/Dockerfile.dotnet`.
- Layer for cache: copy `*.csproj`/`*.sln` and `dotnet restore` first,
  then copy source and `dotnet publish`. Restoring after every source
  change wastes minutes per build.
- Run as non-root (`USER app` — built into .NET images since 8.0;
  port 8080 by default). Root in the container + a bind-mounted NAS is
  how a path-traversal bug becomes a NAS-wide incident.
- Native audio dependencies (ffmpeg etc.) installed via `apt-get` in
  the **final** stage only, with `--no-install-recommends` and list
  cleanup, version-pinned where stability matters.
- No secrets in any layer: no `ENV ApiKey=...`, no copied `.env`,
  no connection strings. They arrive at *run* time via compose
  environment/secrets.

## §2 Compose: order, health, networks

- `depends_on` alone orders *startup*, not *readiness*. The pattern
  that actually prevents connection-refused storms:

```yaml
services:
  postgres:
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $${POSTGRES_USER}"]
      interval: 5s
      timeout: 3s
      retries: 10
  manager:
    depends_on:
      postgres:
        condition: service_healthy
```

- Every long-running service gets a `healthcheck` hitting its
  `/health` endpoint (the app side is aspnetcore-patterns §5), and
  `restart: unless-stopped` so a wedged 24/7 service self-recovers.
- Services talk over the compose network by **service name**
  (`Host=postgres`), never `localhost` — `localhost` inside a container
  is that container.
- Ports: only publish what the host genuinely needs (admin UI, API,
  stream output). The database does not need a published port in
  production compose.
- `.env` beside the compose file feeds `${VARS}`; it is gitignored, and
  a committed `.env.example` documents every required key. See §6 for
  the drift trap.

## §3 NFS-mounted media libraries

A media library on an NFS mount is not a local filesystem; code and ops
that assume it is will misbehave:

- **No reliable file-change events.** inotify does not propagate over
  NFS — library scanning must be periodic and stat-based (compare
  size/mtime), not watcher-based. A watcher that "works" in dev on a
  local volume silently sees nothing in production.
- **Deletes under open files leave placeholders.** Deleting a file
  another process holds open yields `.nfsXXXX` placeholder files;
  scanners must ignore `.nfs*`, and "delete then verify gone"
  logic must tolerate them.
- **Latency and staleness.** Attribute caching means a just-written
  file's metadata can be stale for seconds across clients. Don't build
  read-after-write races across containers.
- **Locking is unreliable.** Don't depend on file locks across NFS for
  correctness; coordinate through the database instead.
- Mount into the container read-only (`:ro`) wherever the service only
  reads — the streaming path rarely needs write access to the library.

## §4 Graceful shutdown

The chain that has to agree: Docker sends SIGTERM → .NET host cancels
`stoppingToken` → services finish within `HostOptions.ShutdownTimeout`
→ compose `stop_grace_period` expires → SIGKILL.

- Set them consistently: `stop_grace_period` (compose, default 10s)
  **≥** `ShutdownTimeout` (host, default 30s — note the defaults
  disagree; align them explicitly).
- PID 1 must be `dotnet` (exec-form `ENTRYPOINT ["dotnet", "App.dll"]`)
  — shell-form wraps it in `sh`, which doesn't forward SIGTERM, so the
  app never sees shutdown and is always SIGKILLed.
- Verify with `docker compose stop <svc>` while tailing logs: you
  should see the app's own shutdown logging, not an abrupt end.

## §5 "Works locally, fails in container" checklist

- **Case sensitivity**: Linux fs is case-sensitive; `Cover.JPG` ≠
  `cover.jpg`. Also true for config file names and env var casing.
- **Paths**: no Windows separators, no absolute host paths; everything
  request- or config-derived goes through `Path.Combine`.
- **localhost**: means "this container" — use service names (§2).
- **Missing native libs**: ffmpeg/ICU/ssl in the runtime image?
  (.NET images set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` on some
  slim variants — culture-dependent code changes behavior.)
- **Permissions**: non-root user vs volume ownership — `chown` in the
  Dockerfile or `user:` in compose, especially for bind mounts.
- **Memory/CPU limits**: the GC honors container limits; a service
  tuned on a 32 GB host behaves differently under a 512 MB limit.
- **Time zone**: containers default to UTC. Schedule logic must use
  explicit `TimeZoneInfo`, never server-local time.

## §6 Stateful volumes & init drift

Databases initialize from environment **only on first boot of an empty
volume**. Changing `POSTGRES_PASSWORD` (or an init script) in `.env`
later does *nothing* to an existing volume — the app gets auth failures
while compose looks correct. This class of drift (env says X, volume
state says Y) is a known dead-air footgun.

- Diagnose: `docker compose exec postgres psql -U <user>` with both old
  and new credentials to learn which reality you're in.
- Fix deliberately: either `ALTER USER ... PASSWORD` inside the running
  DB to match `.env`, or — dev only, destroys data — remove the volume
  (`docker compose down -v`) and re-init.
- Same rule for *any* stateful image with init-time-only config.
  Document per service which settings are init-only.

## Templates

| File | Use |
|---|---|
| `templates/Dockerfile.dotnet` | Multi-stage .NET 9 build, non-root, ffmpeg example, exec-form entrypoint |
| `templates/compose.example.yml` | Service + Postgres with healthchecks, named volumes, NFS volume, .env wiring |

## Non-negotiable defaults

1. Multi-stage builds; runtime image has no SDK, source, or secrets.
2. Exec-form `ENTRYPOINT`; shutdown chain timings aligned (§4).
3. `depends_on` always paired with `condition: service_healthy` for
   stateful dependencies.
4. Containers run non-root.
5. NFS-backed features use periodic stat-based scanning, never inotify.
6. `.env` gitignored with a committed `.env.example`; init-only
   settings documented (§6).
