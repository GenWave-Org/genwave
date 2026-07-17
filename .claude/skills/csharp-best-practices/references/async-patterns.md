# Async & Concurrency Patterns — rules in depth

Async bugs are silent: a swallowed `async void` exception, a missing
token that ignores shutdown, a `.Result` that deadlocks under load.
These rules exist because every one of them has a 3 AM failure mode.

---

## §1 No `async void`

**Why.** Exceptions in an `async void` method can't be caught by the
caller — they crash the process (or vanish, depending on context).
`async void` also can't be awaited, so "done" is unobservable.

- Always `async Task` / `async Task<T>` / `async ValueTask`.
- The only legitimate `async void` is a UI/event handler signature you
  don't control — and its body should be a single `try/catch`-wrapped
  call into an `async Task` method.
- A method that ends in a single `await` can sometimes drop
  `async`/`await` and return the task directly — but keep `async` when
  there's a `using`/`try` in scope, or the dispose/catch happens before
  the task finishes.

---

## §2 CancellationToken: accept it, propagate it, honor it

**Why.** A 24/7 streaming service that ignores cancellation can't shut
down cleanly — Docker sends SIGTERM, waits, then SIGKILLs you
mid-stream-write.

Rules:
- Every async method that does I/O or loops takes a
  `CancellationToken ct` (last parameter, default only on public API
  edges: `CancellationToken ct = default`).
- Pass it to *every* awaited call that accepts one. A token that stops
  at one stack frame is decorative.
- Long loops without awaited I/O: call `ct.ThrowIfCancellationRequested()`
  per iteration.
- During shutdown, `OperationCanceledException` is the *success* path:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    try
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PlayNextSegmentAsync(stoppingToken);
        }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
        // normal shutdown — not an error, don't log it as one
    }
}
```

- Linking tokens: `CancellationTokenSource.CreateLinkedTokenSource(a, b)`
  when both a request and the host can cancel. Dispose the CTS.

---

## §3 No sync-over-async: `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`

**Why.** Sync-blocking on a task burns a thread-pool thread while
another finishes the work; under load this starves the pool and
collapses throughput, and in some contexts deadlocks outright.

- Async all the way down. If a caller "can't be async," that's the
  thing to fix.
- Truly unavoidable sync edges (a `Main` you don't control, a legacy
  interface): one documented `GetAwaiter().GetResult()` at the outermost
  edge, with a comment, never in a library or hot path.
- Constructors can't be async — use an async factory
  (`static async Task<T> CreateAsync(...)`) or move I/O to a
  `StartAsync`/initialization step.

---

## §4 Timeouts are explicit

**Why.** `HttpClient` defaults to 100s; sockets and DB calls can hang
indefinitely. Anything network-facing without a deadline eventually
hangs the pipeline that calls it.

```csharp
using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
timeout.CancelAfter(TimeSpan.FromSeconds(10));
var response = await http.GetAsync(url, timeout.Token);
```

Or `.WaitAsync(TimeSpan.FromSeconds(10), ct)` on .NET 8+ for tasks that
don't take a token. Decide what the timeout *means* (retry? skip
segment? fail the request?) at the call site — don't just make it throw
somewhere else.

---

## §5 Parallelism: deliberate, bounded

- Independent I/O: start tasks then `await Task.WhenAll(...)` — don't
  await serially in a loop out of habit.
- *Bounded* concurrency over a collection:
  `Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, ...)`.
  Unbounded `Select(x => DoAsync(x))` over 10,000 items is a
  self-inflicted DoS on your own database.
- CPU-bound work in an async context: `Task.Run` it so you don't block
  the caller — but never `Task.Run` just to "make it async"; that's a
  thread tax with no benefit.
- Fire-and-forget is almost always a bug (exceptions vanish, shutdown
  races). If work must outlive the request, queue it to a
  `BackgroundService` via `Channel<T>` (§6) so something owns it.

---

## §6 Channels for producer/consumer

`System.Threading.Channels` is the idiomatic in-process queue — right
fit for an audio pipeline (scheduler produces segments, streamer
consumes).

```csharp
var channel = Channel.CreateBounded<AudioSegment>(
    new BoundedChannelOptions(capacity: 8)
    {
        FullMode = BoundedChannelFullMode.Wait, // backpressure, not OOM
        SingleReader = true,
    });

// producer
await channel.Writer.WriteAsync(segment, ct);

// consumer
await foreach (var segment in channel.Reader.ReadAllAsync(ct))
{
    await streamer.PushAsync(segment, ct);
}
```

- Always **bounded** for anything fed by I/O or generation — an
  unbounded channel converts a slow consumer into unbounded memory
  growth.
- `Writer.Complete()` (or `Complete(exception)`) when production ends,
  so `ReadAllAsync` terminates instead of hanging.

---

## §7 IAsyncEnumerable

Use `IAsyncEnumerable<T>` when items become available over time and the
consumer should process them as they arrive (library scan results, play
history pages). Always thread the token:

```csharp
public async IAsyncEnumerable<TrackFile> ScanAsync(
    string root,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    foreach (var path in Directory.EnumerateFiles(root, "*.mp3", SearchOption.AllDirectories))
    {
        ct.ThrowIfCancellationRequested();
        yield return await ReadMetadataAsync(path, ct);
    }
}
```

`[EnumeratorCancellation]` is what makes
`scanner.ScanAsync(root).WithCancellation(ct)` actually cancel. Don't
buffer an entire `IAsyncEnumerable` into a list "for convenience" — that
forfeits the reason it exists.

---

## §8 BackgroundService pitfalls

(Structure and lifetime guidance lives in `aspnetcore-patterns`; these
are the async-specific traps.)

1. **Synchronous start blocks the host.** `ExecuteAsync` runs on the
   startup path until its first real `await`. Begin with
   `await Task.Yield();` or ensure the first statement awaits, or app
   startup hangs on your service.
2. **An unhandled exception kills the service silently** (or stops the
   host, depending on `HostOptions.BackgroundServiceExceptionBehavior`).
   The 24/7 loop pattern: catch-log-delay-continue *inside* the loop for
   recoverable faults, let cancellation end it:

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        await RunCycleAsync(stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
        break;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Cycle failed; retrying in 5s");
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
    }
}
```

3. **Timers:** prefer `PeriodicTimer` + `await timer.WaitForNextTickAsync(ct)`
   over `System.Timers.Timer`/`Task.Delay` loops — it doesn't overlap
   ticks and it cancels cleanly.
4. **Scoped services:** a `BackgroundService` is a singleton; create a
   scope per cycle for DbContexts etc. (see aspnetcore-patterns).

---

## §9 ConfigureAwait

In application code (ASP.NET Core, console hosts, workers) there is no
synchronization context — `ConfigureAwait(false)` changes nothing.
Leave it out (KISS). Add it only in reusable library code that might be
consumed from a UI/legacy context.

---

## §10 Async disposal

Types owning async resources implement `IAsyncDisposable`; consume with
`await using`. In `DisposeAsync`, don't block (`.Wait()`) on cleanup —
that's §3 in a trench coat. If a class owns a long-running task started
in its constructor/start method, `DisposeAsync` should cancel its token
and await the task with a timeout.
