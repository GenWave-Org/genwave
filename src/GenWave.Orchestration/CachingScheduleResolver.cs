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

    // volatile (PLAN T120 review F1): TryGetCurrent reads this field from whatever thread the feeder
    // tick/BoothLogWriter/RankerPersonaPickProvider happen to run on, with no lock — a plain field
    // write in ResolveAsync could be reordered or cached per-CPU without this, letting a sync reader
    // observe a torn/stale reference even after the async load has visibly completed elsewhere.
    volatile ScheduleWeekSnapshot? snapshot;
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

    /// <summary>
    /// Synchronous, in-memory read of the on-air snapshot (SPEC F91.5, STORY-241/242, PLAN T120) —
    /// no store round trip, no awaiting: re-derives <see cref="ScheduleResolver.Resolve"/> against
    /// whichever <see cref="ScheduleWeekSnapshot"/> is already cached and the CURRENT wall clock, so
    /// the answer is always as time-accurate as the wall clock itself even between async
    /// <see cref="ResolveAsync"/> calls. Exists for a caller that sits on a hot/sync path and so
    /// cannot await a reload — <c>OnAirPersonaAccessor.ActivePersonaId</c> and
    /// <c>ScheduleEnvelopeProvider</c> are both built on this.
    ///
    /// <para>
    /// <b>What keeps the cached snapshot fresh:</b> this method never reloads anything itself — only
    /// <see cref="ResolveAsync"/> does. In production, <see cref="ResolveAsync"/> is reached every
    /// unit plan via <c>OnAirPersonaAccessor.ResolveAsync</c> (awaited from
    /// <c>Orchestrator.ResolvePersonaAsync</c> for the unit's lead-in/back-announce segments, and from
    /// <c>RankerPersonaPickProvider.TryPickAsync</c> when the ranker is bound instead of
    /// <c>NoOpPersonaPickProvider</c>), so the snapshot this method reads is at most one unit stale
    /// REGARDLESS of which <c>IPersonaPickProvider</c> is configured — the persona-accessor refresh
    /// path and the pick-provider seam are independent; a no-op pick provider does not starve this
    /// one. A deployment whose cadence disables BOTH <c>LeadInBeforeEachTrack</c> and
    /// <c>BackAnnounceAfterEachTrack</c> (neither the shipped default) is the one configuration where
    /// no per-unit caller reaches <see cref="ResolveAsync"/> at all, and the cached snapshot would
    /// then only advance on the next <see cref="IScheduleStore.WeekChanged"/>-triggered write.
    /// </para>
    ///
    /// <para>
    /// Returns <see langword="null"/> before the very first <see cref="ResolveAsync"/> call has
    /// completed (the process boot window) — there is no cached <see cref="ScheduleWeekSnapshot"/>
    /// yet to resolve against, and this method never triggers the load itself. Callers on this sync
    /// surface tolerate that null exactly the way <c>BoothLogWriter</c> already tolerates a null/zero
    /// persona id for a genuine gap: no persona stamped, nothing else.
    /// </para>
    /// </summary>
    public OnAirSnapshot? TryGetCurrent()
    {
        // Single volatile read into a local (PLAN T120 review F1) — reading the field twice (once for
        // the null check, once to pass to Resolve) would let a concurrent ResolveAsync swap the
        // reference in between the two reads on another thread.
        var current = snapshot;
        return current is null ? null : resolver.Resolve(current);
    }

    void OnWeekChanged() => dirty = true;
}
