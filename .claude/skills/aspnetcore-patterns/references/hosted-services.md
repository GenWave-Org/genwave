# Hosted services & BackgroundService — in depth

The patterns for long-running work in a 24/7 service: audio scheduling
loops, queue consumers, periodic scans. Async-specific traps (async
void, sync-over-async, channels API) live in
`csharp-best-practices/references/async-patterns.md`; this file covers
the *hosting* side.

---

## §shape — The resilient 24/7 loop

A `BackgroundService` that throws unhandled stops silently (or takes
the host down, per `HostOptions.BackgroundServiceExceptionBehavior`).
For a service whose job is to never stop, the loop owns its faults:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    await Task.Yield(); // don't block host startup

    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await RunCycleAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            break; // normal shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cycle failed; retrying in {Delay}s", RetryDelay.TotalSeconds);
            await Task.Delay(RetryDelay, stoppingToken);
        }
    }
}
```

See `templates/BackgroundWorker.cs` for the full version with scope and
heartbeat. Decide *per service* whether a persistent failure should
keep retrying (streaming output: yes, with backoff) or stop the host
(corrupt configuration: fail fast so the orchestrator restarts the
container).

---

## §scopes — Scope-per-cycle for scoped dependencies

A hosted service is a singleton; `DbContext` is scoped. Injecting
`DbContext` into the service constructor fails at startup — and
"fixing" it by registering the context as singleton means one shared,
non-thread-safe context with an ever-growing change tracker.

The pattern: inject `IServiceScopeFactory`, create a scope per cycle:

```csharp
private async Task RunCycleAsync(CancellationToken ct)
{
    await using var scope = scopeFactory.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ConfigDbContext>();
    // ... one unit of work, then the scope (and context) is disposed
}
```

One scope == one unit of work. Don't hold a scope across the whole
service lifetime (that's the singleton-context bug with extra steps),
and don't create one per query (that defeats the unit-of-work).

---

## §queues — Queue consumer services

In-process producer/consumer uses a bounded `Channel<T>` registered as
a singleton; producers (controllers, schedulers) write, one hosted
service consumes:

```csharp
// Registration
builder.Services.AddSingleton(Channel.CreateBounded<AudioJob>(
    new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait }));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Channel<AudioJob>>().Reader);
builder.Services.AddSingleton(sp => sp.GetRequiredService<Channel<AudioJob>>().Writer);
builder.Services.AddHostedService<AudioJobConsumer>();

// Consumer core
await foreach (var job in reader.ReadAllAsync(stoppingToken))
{
    await using var scope = scopeFactory.CreateAsyncScope();
    await ProcessAsync(scope.ServiceProvider, job, stoppingToken);
}
```

Bounded + `Wait` gives backpressure: a slow consumer slows producers
instead of growing memory. If the producer is a request handler that
must not block, use `TryWrite` and return 503/queue-full explicitly.

---

## §lifecycle — Startup and shutdown ordering

- Hosted services start in **registration order**, before the app
  starts serving requests (`StartAsync`), and stop in **reverse
  order** on shutdown.
- `ExecuteAsync` runs synchronously on the startup path until its first
  real `await` — begin with `await Task.Yield()` or startup hangs.
- Shutdown: the host cancels `stoppingToken`, then waits
  `HostOptions.ShutdownTimeout` (default 30s — set explicitly; Docker's
  default SIGKILL window is 10s, so align them: compose `stop_grace_period`
  ≥ host `ShutdownTimeout`).
- Cleanup that must run on shutdown (final play-history flush, Icecast
  disconnect) goes after the loop in `ExecuteAsync` or in `StopAsync` —
  and must itself be quick and cancellation-aware.
- Don't do heavy initialization in the constructor (it runs during DI
  container build). Use the start of `ExecuteAsync`, or implement
  `IHostedLifecycleService` for explicit pre/post start hooks.

---

## §health — Making background health observable

A dead loop is invisible to HTTP health checks unless you surface it.
Pattern: a tiny singleton state object the worker updates and a health
check reads:

```csharp
public sealed class WorkerHeartbeat
{
    private long lastCycleTicks;
    public DateTimeOffset LastCycle => new(Interlocked.Read(ref lastCycleTicks), TimeSpan.Zero);
    public void Beat() => Interlocked.Exchange(ref lastCycleTicks, DateTimeOffset.UtcNow.UtcTicks);
}

public sealed class WorkerHealthCheck(WorkerHeartbeat heartbeat) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        var age = DateTimeOffset.UtcNow - heartbeat.LastCycle;
        return Task.FromResult(age < TimeSpan.FromMinutes(2)
            ? HealthCheckResult.Healthy($"last cycle {age.TotalSeconds:F0}s ago")
            : HealthCheckResult.Unhealthy($"no cycle for {age.TotalMinutes:F1} min"));
    }
}
```

The worker calls `heartbeat.Beat()` each cycle. Docker/K8s now restarts
a wedged service instead of streaming silence for hours.

---

## §periodic — Periodic work

`PeriodicTimer` over `Task.Delay` loops or `System.Timers.Timer`:
no overlapping ticks, clean cancellation:

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    await ScanLibraryAsync(stoppingToken);
}
```

If a tick can exceed the period, decide explicitly: skip (the default
with this shape — WaitForNextTickAsync just fires when you return) or
measure and log overrun. Never let ticks stack in parallel by accident.

---

## §testing — Testing hosted services

- Put the cycle logic in a method (or separate class) that takes its
  dependencies and a token — test *that* directly with real or in-memory
  dependencies; don't spin the host to test business logic.
- For wiring tests, `IHost` with `Host.CreateApplicationBuilder()`,
  start it, assert observable effects, stop it with a short timeout.
- Use a controllable clock abstraction (`TimeProvider` on .NET 8+) so
  schedule logic is testable without real waits —
  `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.
