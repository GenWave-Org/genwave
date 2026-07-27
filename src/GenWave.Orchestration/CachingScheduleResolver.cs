using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Orchestration;

/// <summary>
/// SPEC F91.3 (STORY-241, PLAN T119) — the thin caching wrapper around <see cref="ScheduleResolver"/>:
/// holds the current <see cref="ScheduleWeekSnapshot"/> in memory and subscribes to
/// <see cref="IScheduleStore.WeekChanged"/> for invalidation, so a caller resolving on every 3s feeder
/// tick never issues a schedule-store query itself ("the 3s feeder tick performs no schedule query").
/// <see cref="ScheduleResolver"/> stays a pure function of (snapshot, wall clock) — this type is the
/// only thing that knows the snapshot ever came from a database at all.
///
/// <para>
/// The snapshot loads lazily on the first <see cref="ResolveAsync"/> call (or the first call after a
/// <see cref="IScheduleStore.WeekChanged"/> invalidation) rather than in the constructor — a constructor
/// cannot be async, and this type never blocks on the store (no <c>.GetAwaiter().GetResult()</c>). One
/// consequence: the very first resolve after construction, and the first resolve after any schedule
/// write, costs one <see cref="IScheduleStore.LoadWeekAsync"/> round trip; every tick in between is
/// snapshot-only.
/// </para>
///
/// <para>
/// Not registered in DI and no consumer calls it yet (PLAN T119 ships this dark, same as
/// <see cref="IScheduleStore"/> itself) — PLAN T120 wires the feeder tick path to it.
/// </para>
///
/// <para>
/// Subscribes to <see cref="IScheduleStore.WeekChanged"/> in the constructor and never unsubscribes —
/// this type implements no <see cref="IDisposable"/>. Singleton lifetime is therefore load-bearing here,
/// not just convention, mirroring <c>ScheduleServiceCollectionExtensions.AddScheduleStore</c>'s own
/// remarks on <see cref="IScheduleStore"/> itself (PLAN T119 review F6): PLAN T120 must register this
/// type as a singleton — one instance, one subscription, for the life of the process. A scoped/transient
/// registration would leak one subscription (and the wrapped store reference) per instance created.
/// </para>
/// </summary>
public sealed class CachingScheduleResolver
{
    readonly IScheduleStore store;
    readonly ScheduleResolver resolver;
    ScheduleWeekSnapshot? snapshot;
    volatile bool dirty = true;

    public CachingScheduleResolver(IScheduleStore store, ScheduleResolver resolver)
    {
        this.store = store;
        this.resolver = resolver;
        store.WeekChanged += OnWeekChanged;
    }

    /// <summary>
    /// Resolves the current <see cref="OnAirSnapshot"/> against the cached week snapshot, reloading it
    /// from <see cref="IScheduleStore"/> first only when nothing has been cached yet or a write has
    /// invalidated it since the last reload.
    /// </summary>
    public async Task<OnAirSnapshot> ResolveAsync(CancellationToken ct)
    {
        if (dirty || snapshot is null)
        {
            // Cleared BEFORE the await starts (PLAN T119 review F4): a WeekChanged invalidation firing
            // WHILE this load is in flight must win the race. Clearing it AFTER the await would let that
            // mid-flight dirty=true get stomped back to false the instant this load finishes, silently
            // discarding the invalidation and serving the pre-write snapshot forever. Clearing first
            // means the worst case is one redundant reload on the very next call — cheap, and always
            // correct — rather than a lost invalidation.
            dirty = false;
            snapshot = await store.LoadWeekAsync(ct);
        }

        return resolver.Resolve(snapshot);
    }

    void OnWeekChanged() => dirty = true;
}
