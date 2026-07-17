---
name: aspnetcore-patterns
description: >-
  ASP.NET Core (.NET 9) structural conventions: controllers vs minimal
  APIs, middleware pipeline order, BackgroundService/IHostedService for
  long-running work (24/7 loops, queues, schedulers), the options
  pattern (IOptions<T> with validation), DI lifetimes and the
  singleton-consuming-scoped trap, health checks, configuration
  layering (appsettings → env → database-backed config), structured
  logging with correlation IDs, and the house REST conventions (plural
  kebab-case resources, proper verbs). Use when adding or restructuring
  an API endpoint, writing a hosted/background service, wiring DI,
  binding configuration, adding health checks, or answering "where does
  this go / how should this be wired in ASP.NET Core" questions. Ships
  templates for a background worker, validated options setup, and a
  REST controller skeleton.
---

# ASP.NET Core Patterns

How to structure an ASP.NET Core service so the framework works for you:
explicit pipeline order, correctly-scoped dependencies, hosted services
that survive 24/7 operation, and configuration that fails fast at
startup instead of at request time.

## 🎯 Why: Design for Change

The goal of writing software is to be able to **change it safely**.
Options classes with validation, named policies, one-responsibility
hosted services, and DI-visible dependencies mean the next feature is
added by *composition* — a new class, a new registration — not by
editing a god-startup file. Misordered middleware and hidden service
locators are change-hiders: they work until the edit that reveals them.

This skill is about *ASP.NET Core structure*. For C# language idioms use
`csharp-best-practices`; for security review of endpoints use
`security-api`; for container/runtime concerns use `docker-linux-ops`.

## Decision guide

| Symptom / question | Rule | Where |
|---|---|---|
| New endpoint: controller or minimal API? | §1 below | SKILL.md |
| Route/resource naming | House REST conventions | §2 below, templates/ItemsController.cs |
| `Program.cs` pipeline ordering doubts | Canonical order | §3 below |
| Long-running loop, scheduler, queue consumer | `BackgroundService` patterns | references/hosted-services.md, templates/BackgroundWorker.cs |
| "Cannot consume scoped service from singleton" (or worse: it works but shares a DbContext) | Scope-per-cycle pattern | references/hosted-services.md §scopes |
| Config values read via `IConfiguration["key"]` strings everywhere | Options pattern + validation | references/options-and-config.md, templates/OptionsSetup.cs |
| Which DI lifetime? | §4 below | SKILL.md |
| Service needs to know if it's healthy | Health checks | §5 below |
| Log lines impossible to correlate across services | Structured logging + correlation | §6 below |

## §1 Controllers vs minimal APIs

House default: **controllers** for the administrative REST API (they
group related endpoints, share `[Authorize]`/filters/route prefixes, and
match the existing codebase). Minimal APIs are fine for one-off
infrastructure endpoints (health, version, a single public stream
status route) where a controller class is ceremony.

Whichever you pick per area, stay consistent within it. Don't introduce
the second style into an area that already uses one (POLA).

## §2 House REST conventions (from DEVELOPMENT_CONVENTION.md)

- **Plural nouns**, **kebab-case**, lowercase:
  `/api/schedule-templates`, `/api/play-history`, `/api/user-profiles`.
- Verbs by method, not by path: `GET /api/tracks/{id}`, not
  `/api/getTrack`. `PUT` replaces, `PATCH` partially updates,
  `POST` creates, `DELETE` removes.
- Nesting only for true parent-child:
  `/api/stations/{stationId}/schedule-templates`.
- Filtering/sorting/pagination via query string:
  `?page=2&limit=20&genre=jazz&sort=title-asc`.
- See `templates/ItemsController.cs` for the full skeleton (DTOs,
  cancellation tokens, ProblemDetails, status codes).

## §3 Pipeline order (Program.cs)

Registration order is execution order. The canonical sequence:

```csharp
app.UseForwardedHeaders();    // first if behind a proxy
app.UseExceptionHandler();    // before anything that throws
app.UseHsts();                // non-dev
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("AdminUi");       // after routing, before auth
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
```

Anything custom that inspects the endpoint goes after `UseRouting`;
anything that must see the authenticated user goes after
`UseAuthentication`. Security implications of misordering are in
`security-api` (references/aspnetcore.md §middleware-order).

## §4 DI lifetimes

| Lifetime | Use for | Watch out |
|---|---|---|
| **Singleton** | Stateless services, caches, channels, config-backed lookups, anything a `BackgroundService` is | Must not capture scoped services (§hosted-services scopes); must be thread-safe |
| **Scoped** | Per-request state: `DbContext`, unit-of-work, request-bound context | Never resolved from a singleton's constructor |
| **Transient** | Lightweight, stateful-per-use helpers | A transient holding a `DbContext` gets a *different* one than the request's |

Rules of thumb:
- Default to the *longest* lifetime that is actually safe — fewer
  allocations, but correctness first.
- A singleton needing scoped services creates a scope explicitly
  (`IServiceScopeFactory`) per operation — the one sanctioned use of
  the service locator shape.
- Register interfaces, depend on interfaces; concrete-class injection
  is fine for sealed internals with no second implementation (YAGNI —
  don't create an interface that has exactly one implementation *and*
  no test seam need).

## §5 Health checks

Every service the app depends on gets a check: PostgreSQL, the
streaming output, disk/mount availability for the media library.

```csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres")
    .AddCheck<MediaRootHealthCheck>("media-root")
    .AddCheck<StreamOutputHealthCheck>("icecast");
app.MapHealthChecks("/health");
```

- `/health` is what Docker/K8s probes hit — keep it cheap (no full
  table scans) and anonymous-but-uninformative (status only; detail
  endpoints need auth).
- A `BackgroundService`'s health is invisible to HTTP by default —
  expose a heartbeat (last-cycle timestamp in a singleton state object)
  and check staleness (references/hosted-services.md §health).

## §6 Logging with correlation

- Structured logging with message templates:
  `logger.LogInformation("Segment {SegmentId} queued for {MountPoint}", id, mount)` —
  never string interpolation into the message (kills filtering).
- One correlation ID per request/cycle, flowed via
  `logger.BeginScope` so every line in a unit of work carries it.
- Log levels honestly: `Error` means someone should look, `Warning`
  means degraded-but-handled, `Information` is state changes, `Debug`
  is everything else. Don't log inside hot loops (house rule), don't
  log payloads that may carry sensitive data.

## Reference files

- `references/hosted-services.md` — BackgroundService in depth: the
  24/7 loop shape, scope-per-cycle for DbContext, queue consumption via
  Channel, startup/shutdown ordering, exposing health, testing hosted
  services.
- `references/options-and-config.md` — options pattern end to end:
  binding, `ValidateDataAnnotations`/`ValidateOnStart`, named options,
  `IOptionsMonitor` for hot reload, configuration layering including
  database-backed configuration providers.

## Templates (copy and adapt)

| File | Use case |
|---|---|
| `templates/BackgroundWorker.cs` | 24/7 resilient loop with scope-per-cycle, heartbeat, clean shutdown |
| `templates/OptionsSetup.cs` | Options class + registration with validation at startup |
| `templates/ItemsController.cs` | REST controller following the house conventions |

## Non-negotiable defaults

1. Pipeline order follows §3; deviations are commented with why.
2. No `IConfiguration["string:key"]` reads in business logic — bind to
   validated options classes.
3. Options are validated at startup (`ValidateOnStart`) — a misconfigured
   service fails to boot, not at 2 AM mid-request.
4. `BackgroundService` loops follow the resilient-loop shape (catch,
   log, delay, continue; cancellation ends the loop).
5. `DbContext` is scoped; singletons create scopes to use it.
6. Routes follow the house REST conventions (§2).
