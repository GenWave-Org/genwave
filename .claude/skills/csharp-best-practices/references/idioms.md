# C# Idioms — rules in depth

Each rule: why it exists, the bad pattern, the idiomatic refactor, and
when it's over-engineering. Examples target C# 13 / .NET 9.

---

## §1 Nullability: prove it or handle it — never `!`

**Why.** Nullable reference types make "can this be null?" a compiler
question. The `!` operator answers "trust me" — and the compiler stops
checking forever after. Every `!` is a latent `NullReferenceException`
that the type system was specifically trying to prevent.

**Bad:**

```csharp
var track = await library.FindTrackAsync(id, ct);
PlayTrack(track!); // "it's never null in practice"
```

**Idiomatic:**

```csharp
var track = await library.FindTrackAsync(id, ct);
if (track is null)
{
    throw new TrackNotFoundException(id);
}
PlayTrack(track);
```

Or push the guarantee to the source: have `FindTrackAsync` return
`Result<Track>` (§5) or add a `GetRequiredTrackAsync` that throws with
context. If a field "is always set by DI / by EF / by the framework",
use `required` members or constructor injection so the compiler agrees —
don't decorate it with `= null!;`.

```csharp
public sealed class StreamOptions
{
    public required string MountPoint { get; init; }
    public required Uri IcecastUrl { get; init; }
}
```

**When not to apply:** unit tests deliberately passing null to verify a
guard. That's the only place.

---

## §2 Records for data, classes for behavior

**Why.** A `sealed record` gives you value equality, immutability,
`with` mutation, and a sane `ToString()` for free. Hand-rolling those on
a class is boilerplate that drifts.

**Bad:**

```csharp
public class TrackInfo
{
    public string Title { get; set; }
    public string Artist { get; set; }
    public int DurationSeconds { get; set; }
}
```

**Idiomatic:**

```csharp
public sealed record TrackInfo(string Title, string Artist, int DurationSeconds);
```

For domain values with rules, validate in a factory and keep the
constructor private — see `templates/ValueObject.cs`. For entities with
identity and lifecycle, a class is correct — see `templates/Entity.cs`.

Guidance:
- `sealed record` — DTOs, messages, config shapes, domain values.
- `readonly record struct` — small, hot-path values and typed IDs (§7).
- `class` — anything with identity, mutation rules, or services.
- Seal everything not designed for inheritance. Unsealing is a
  one-line change when a real need appears; un-inheriting is a refactor.

**When not to apply:** EF Core entities often need parameterless
constructors and settable properties — keep those as classes shaped for
the ORM, and map to records at the boundary if leakage hurts.

---

## §3 Model illegal states out of existence

**Why.** A type where `Status == Playing` but `CurrentTrack == null` can
exist forces every consumer to re-check invariants. Close the hierarchy
and each state carries exactly the data valid for it.

**Bad:**

```csharp
public class PlaybackState
{
    public PlaybackStatus Status { get; set; }
    public Track? CurrentTrack { get; set; }   // only when Playing
    public DateTime? PausedAt { get; set; }    // only when Paused
}
```

**Idiomatic — closed hierarchy:**

```csharp
public abstract record PlaybackState
{
    public sealed record Stopped : PlaybackState;
    public sealed record Playing(Track Track, DateTime StartedAt) : PlaybackState;
    public sealed record Paused(Track Track, DateTime PausedAt) : PlaybackState;
    private PlaybackState() { }
}
```

(One type per file still applies in real code — nested states of one
concept count as one type's internals; if they grow, split them.)

Consume with an exhaustive switch expression (§4):

```csharp
var label = state switch
{
    PlaybackState.Stopped => "stopped",
    PlaybackState.Playing p => $"playing {p.Track.Title}",
    PlaybackState.Paused p => $"paused at {p.PausedAt:t}",
    _ => throw new UnreachableException(nameof(state)),
};
```

**When not to apply:** two booleans that genuinely vary independently
don't need a four-record hierarchy. Apply when states are *exclusive*
and carry different data.

---

## §4 Switch expressions, exhaustively

**Why.** A `switch` statement with a silent fall-through or a `default:
break;` hides the new enum case you added last sprint. A switch
*expression* over a closed set, with `UnreachableException` instead of a
silent discard, fails loudly the moment reality changes.

**Bad:**

```csharp
switch (contentType)
{
    case ContentType.Music: Play(); break;
    case ContentType.SiteId: PlayId(); break;
    default: break; // Advertisement silently does nothing
}
```

**Idiomatic:**

```csharp
var action = contentType switch
{
    ContentType.Music => PlayMusic(),
    ContentType.SiteId => PlaySiteId(),
    ContentType.Advertisement => PlayAd(),
    _ => throw new UnreachableException($"Unhandled content type {contentType}"),
};
```

The compiler warns (CS8509/CS8524) on missing cases for switch
expressions — and warnings are errors here, so this is enforced.

---

## §5 `Result<T>` for expected failures, exceptions for bugs

**Why.** "Track file missing from NFS mount" is an expected outcome the
caller must handle; throwing makes it invisible in the signature and
expensive on hot paths. "Configuration table doesn't exist" is a bug or
an environment failure; an exception with context is exactly right.

Rule of thumb: if a sad path appears in a spec/user story, it's a
`Result`. If it means a developer or operator must intervene, it's an
exception.

```csharp
public async Task<Result<AudioSource>> ResolveAsync(TrackId id, CancellationToken ct)
{
    var path = await catalog.GetPathAsync(id, ct);
    if (path is null)
    {
        return Result<AudioSource>.Failure($"Track {id} not in catalog");
    }
    if (!File.Exists(path))
    {
        return Result<AudioSource>.Failure($"Track {id} missing on disk: {path}");
    }
    return Result<AudioSource>.Success(new AudioSource(path));
}
```

See `templates/Result.cs`. Don't wrap *everything* in Result — that's
ceremony. Constructor argument validation still throws
`ArgumentException`; impossible states still throw.

**When not to apply:** ASP.NET Core controllers — there, expected
failures usually map to `ProblemDetails`/status codes directly; don't
build a parallel Result pipeline if the framework's is enough (KISS).

---

## §6 Catch what you can handle; never swallow

**Why.** `catch (Exception) { }` converts every future bug — null refs,
disposed objects, OOM-adjacent failures — into silence. The error
doesn't go away; it moves downstream and loses its stack trace on the
way.

**Bad:**

```csharp
try { await streamer.PushAsync(buffer, ct); }
catch (Exception) { /* keep the stream alive */ }
```

**Idiomatic:**

```csharp
try
{
    await streamer.PushAsync(buffer, ct);
}
catch (IcecastConnectionException ex)
{
    logger.LogWarning(ex, "Icecast push failed, scheduling reconnect");
    reconnect.Schedule();
}
// anything else propagates — the host's error handling owns it
```

Rules:
- Catch the *specific* type you have a recovery for.
- Rethrow with context when crossing a layer: wrap in a domain exception
  (`templates/AppException.cs`) with the IDs/paths needed to debug,
  passing the original as `innerException`.
- `catch (Exception)` is legal only at top-level boundaries (request
  middleware, `BackgroundService.ExecuteAsync` loops, message handlers)
  *and* it must log and make a deliberate continue/stop decision.
- Never `throw ex;` — it resets the stack trace. `throw;` rethrows.
- `OperationCanceledException` is not an error during shutdown — let it
  propagate or catch-and-exit quietly in loops (see async-patterns.md).

---

## §7 Strongly-typed IDs

**Why.** `GetPlayHistory(int stationId, int trackId)` compiles fine with
the arguments swapped. A typed ID makes the swap a compile error.

```csharp
public readonly record struct TrackId(int Value)
{
    public override string ToString() => Value.ToString();
}
```

**When not to apply:** don't brand every int in the codebase on day one
(YAGNI). Introduce a typed ID when two same-typed identifiers travel
together, or when an ID crosses module boundaries.

---

## §8 Strings: explicit comparison, explicit culture

**Why.** `"i".ToUpper() == "I"` is false in Turkish locale; `==` on
user-visible strings ignores casing intent; `string.Format` of dates
varies by server culture. Streaming metadata and file paths are machine
data — treat them invariantly.

- Comparisons: always pass `StringComparison` —
  `OrdinalIgnoreCase` for machine data (paths, mount names, genre tags),
  `CurrentCulture` only for human-facing sorting/display.
- Parsing/formatting machine data: `CultureInfo.InvariantCulture`.
- Paths: `Path.Combine`/`Path.Join`, never `+ "/"`. Case matters on
  Linux — never rely on case-insensitive matching for files.

---

## §9 LINQ: clarity first, enumerate once

**Why.** LINQ is for readable transformations, not golf. And an
`IEnumerable<T>` backed by a query or generator re-executes on every
enumeration — a classic double-DB-hit.

- If a chain needs a comment to be understood, rewrite it as a loop or
  named intermediate variables.
- Materialize (`ToList()`/`ToArray()`) when you'll enumerate more than
  once, and at API boundaries so callers can't re-trigger your query.
- Don't use `Count() > 0` (full enumeration) when `Any()` answers it.
- In EF Core queries: project early (`Select` only needed columns),
  filter in SQL not in memory, and remember `AsEnumerable()` silently
  moves the rest of the chain client-side.

---

## §10 Expose collections immutably

**Why.** Returning `List<T>` from a property hands every caller a remote
control for your internal state.

```csharp
public sealed class Playlist
{
    private readonly List<Track> tracks = [];
    public IReadOnlyList<Track> Tracks => tracks;
    public void Add(Track track) { /* invariants here */ tracks.Add(track); }
}
```

Mutation goes through methods that can enforce invariants. Use
collection expressions (`[]`, `[.. existing]`) over `new List<T>()`.

---

## §11 Properties with intent

- `{ get; init; }` — set once at construction; the default for data.
- `{ get; private set; }` — mutated only by the owning class's methods.
- `{ get; set; }` — only when external mutation is genuinely the design
  (ORM entities, options bound from config).
- Public fields: never (except `const`).
- Validate in the constructor or factory so an instance is valid from
  birth — don't ship a `Validate()` method callers might forget.

---

## §12 Constructor injection, nothing fancier

**Why.** Constructor DI makes dependencies visible, testable, and
fail-fast at startup. Service locators and property injection hide them.

```csharp
public sealed class CadenceScheduler(
    IScheduleRepository schedules,
    IClock clock,
    ILogger<CadenceScheduler> logger) : ICadenceScheduler
{
    // primary constructor parameters are captured; no underscore fields
}
```

- Don't resolve from `IServiceProvider` inside business logic. The
  exception: factories/`IServiceScopeFactory` inside singletons that
  need scoped services (see aspnetcore-patterns).
- Don't `new` a dependency that has its own dependencies or I/O.
  Do `new` plain values and domain objects — not everything needs DI.
- ≤3 constructor parameters is comfort; 5+ is a Single Responsibility
  smell — split the class before reaching for a parameter object.

---

## §13 Named constants — for repeated meaning only

`const` / `static readonly` / enum when a value has meaning *and* is
used more than once or crosses files. A value used once, next to its
context, doesn't need a name (`thread.Sleep(2000)` next to a "retry
after 2s" comment beats `TwoSeconds`).

`static readonly` for non-primitive or computed values; `const` only for
true compile-time constants (remember `const` is baked into *consuming*
assemblies at compile time).

---

## §14 File & namespace conventions

- File-scoped namespaces (`namespace GenWave.Cadence;`) — one less
  indent level for the whole file.
- One type per file, file named after the type. No exceptions.
- Namespace mirrors folder path; folder mirrors project structure.
- Usings: implicit usings on; sort and trim the rest (`dotnet format`
  does this).
- Keep files under ~300 lines. A file that big is usually two
  responsibilities — split the type, not just the file.
