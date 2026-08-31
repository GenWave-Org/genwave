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
/// Registered as a singleton in <c>StationSettingsHostingExtensions</c> and consumed by
/// <c>OnAirPersonaAccessor</c>/<c>ScheduleEnvelopeProvider</c> (PLAN T120) — the feeder tick path.
/// </para>
///
/// <para>
/// Subscribes to <see cref="IScheduleStore.WeekChanged"/> in the constructor and never unsubscribes —
/// this type implements no <see cref="IDisposable"/>. Singleton lifetime is therefore load-bearing here,
/// not just convention, mirroring <c>ScheduleServiceCollectionExtensions.AddScheduleStore</c>'s own
/// remarks on <see cref="IScheduleStore"/> itself (PLAN T119 review F6): registered as a singleton —
/// one instance, one subscription, for the life of the process. A scoped/transient registration would
/// leak one subscription (and the wrapped store reference) per instance created. <see cref="IShowStore.ShowChanged"/>
/// (PLAN T360 review HIGH-1) rides the identical subscribe-once-never-unsubscribe posture — see
/// <paramref name="showStore"/>'s own remarks below for why it is optional rather than a fourth
/// required constructor parameter.
/// </para>
///
/// <para>
/// <b>Specials ride the same cache (SPEC F120.2, PLAN T260).</b> Alongside the week snapshot, this type
/// also holds the <see cref="IScheduleSpecialStore.ListUpcomingAsync"/> result and hands it to
/// <see cref="ScheduleResolver.Resolve"/> as its optional <c>specials</c> argument — this caching layer
/// is the ONLY thing that turns <see cref="IScheduleSpecialStore"/>'s dark seam (PLAN
/// T258/T259 — a store an operator could already write/list/delete through, but nothing on the
/// production feeder path read) into a special that actually shadows the weekly grid live.
/// <see cref="IScheduleSpecialStore.ListUpcomingAsync"/> is unbounded above its <c>fromDate</c>
/// argument by design (that method's own remarks: "a caller wanting a narrower window filters the
/// returned list itself") — <see cref="ScheduleResolver.Resolve"/> is exactly that filter, since it
/// only ever consults TODAY (the shadow) and TOMORROW (the boundary-peek) of whatever list it is
/// handed (see its own remarks). Caching the WHOLE unbounded-above list rather than pre-trimming it to
/// two days costs nothing extra here — specials are rare rows (SPEC F120.1's own framing) — and keeps
/// this type from having to duplicate <see cref="ScheduleResolver"/>'s own "which rows matter today"
/// logic.
/// </para>
///
/// <para>
/// <b>The specials cache also reloads once a day — NOT for the reason that might look obvious (PLAN
/// T260 design; review MF1 corrected an earlier, wrong draft of this very paragraph).</b> The week
/// snapshot above only ever goes stale on a write — <see cref="IScheduleStore.WeekChanged"/> for the
/// grid itself, and, as of PLAN T360 review HIGH-1, <see cref="IShowStore.ShowChanged"/> too: a cached
/// block's own <c>Show</c> is a <see cref="GenWave.Core.Domain.ShowSummary"/> resolved at LOAD time
/// (SPEC F116.1), so an operator editing that show's name/tagline/flavor/rotation writes through a
/// DIFFERENT store than the one this cache already watches, and would otherwise sit invisible until an
/// unrelated schedule write happened to reload it. Wall-clock time passing alone still can never be
/// the cause, though — segment_schedule's own rows are date-less (a weekly grid repeats forever), so
/// only an explicit write (of either kind) can ever make a cached week snapshot wrong. It is tempting
/// to assume the specials list needs a day-rollover reload
/// for the SAME kind of reason — "a special dated the day after tomorrow only becomes relevant once the
/// date rolls over" — but that is false: <see cref="IScheduleSpecialStore.ListUpcomingAsync"/> is
/// unbounded ABOVE its <c>fromDate</c> argument (that method's own remarks), so the very FIRST load
/// already carries every future-dated special this cache will ever need, and <see cref="ScheduleResolver.Resolve"/>
/// re-computes "today"/"tomorrow" fresh on every call regardless of when the cache last reloaded — it
/// would filter a day-after-tomorrow row in correctly with or WITHOUT a reload, the instant that row's
/// own date becomes "today" or "tomorrow" for the CURRENT call. The real reasons this cache still
/// reloads once a day are both operational, not correctness-of-the-shadow: (1) re-anchoring
/// <c>fromDate</c> forward so a long-running (24/7) process's cached list does not keep accumulating
/// specials whose own date has already passed — plain memory/CPU hygiene, since every
/// <see cref="ScheduleResolver.Resolve"/> call filters the WHOLE cached list on every read; and (2) a
/// once-a-day backstop for a write that lands OUTSIDE this process's own <see cref="IScheduleSpecialStore"/>
/// implementation (a manual <c>psql</c> INSERT/DELETE against <c>station.schedule_special</c>, say) and
/// so never raises <see cref="IScheduleSpecialStore.SpecialsChanged"/> at all — eventual, not immediate,
/// consistency for that one out-of-band case; an ordinary write through this process's own
/// <see cref="IScheduleSpecialStore"/> is still caught immediately by (a) below, same cache cycle.
/// So invalidation here is: (a) <see cref="IScheduleSpecialStore.SpecialsChanged"/> (the sibling of
/// <see cref="IScheduleStore.WeekChanged"/> above — an in-process write), mirrored via
/// <see cref="specialsDirty"/> exactly the way <see cref="dirty"/> mirrors <see cref="WeekChanged"/>; and
/// (b) day rollover, detected the cheapest honest way available without a background timer or a second
/// hosted service: the <c>fromDate</c> this cache last LOADED specials with is stamped alongside the
/// list (<see cref="specialsLoadedDateNumber"/>), and <see cref="ResolveAsync"/> — the one place this
/// type ever talks to a store — compares it against station-local "today" (<see cref="ScheduleResolver.StationToday"/>,
/// PLAN T260 review SF4) on every call before deciding whether to reload. The comparison ITSELF is a
/// cheap <see cref="DateOnly.DayNumber"/> equality check, but resolving "today" in the first place still
/// costs whatever <see cref="ScheduleResolver.StationToday"/> costs — the same <see cref="TimeZoneInfo.ConvertTime(DateTimeOffset,TimeZoneInfo)"/>/
/// <see cref="IStationClockProvider"/> work every other station-local-now read in this codebase already
/// pays on every call, not a free operation. <see cref="TryGetCurrent"/> stays exactly as
/// sync/reload-free as the week-snapshot cache above — this type's own remarks on that method already
/// establish "only <see cref="ResolveAsync"/> ever reloads anything," and specials do not change that: a
/// caller sitting on <see cref="TryGetCurrent"/>'s hot path serves whatever specials list
/// <see cref="ResolveAsync"/> most recently cached, exactly as stale (at most one unit) as the week
/// snapshot it already tolerates.
/// </para>
/// </summary>
public sealed class CachingScheduleResolver
{
    readonly IScheduleStore store;
    readonly ScheduleResolver resolver;
    readonly IScheduleSpecialStore specialStore;

    // volatile (PLAN T120 review F1): TryGetCurrent reads this field from whatever thread the feeder
    // tick/BoothLogWriter/RankerPersonaPickProvider happen to run on, with no lock — a plain field
    // write in ResolveAsync could be reordered or cached per-CPU without this, letting a sync reader
    // observe a torn/stale reference even after the async load has visibly completed elsewhere.
    volatile ScheduleWeekSnapshot? snapshot;
    volatile bool dirty = true;

    // The specials cache (PLAN T260) — same volatile-field posture as the week snapshot above, for the
    // same reason (TryGetCurrent's lock-free sync read). specialsLoadedDateNumber is DateOnly.DayNumber
    // (a plain int) rather than a DateOnly field: DateOnly is a struct outside the small set of types the
    // `volatile` keyword accepts, and DayNumber alone is everything a same-day equality check needs.
    volatile IReadOnlyList<ScheduleSpecial>? specials;
    volatile bool specialsDirty = true;
    volatile int specialsLoadedDateNumber = -1;

    /// <param name="showStore">
    /// When supplied, <see cref="IShowStore.ShowChanged"/> subscribes into the SAME dirty-on-write
    /// posture <paramref name="store"/>/<paramref name="specialStore"/> already use (PLAN T360 review
    /// HIGH-1) —
    /// see the class remarks' own "no cache divergence" correction. Optional (default
    /// <see langword="null"/>, mirroring <see cref="ScheduleResolver"/>'s own optional
    /// <c>IStationClockProvider?</c> collaborator) rather than a fourth required parameter: production
    /// composition (<c>StationSettingsHostingExtensions</c>) always supplies the real
    /// <see cref="IShowStore"/> via constructor injection regardless of the default (both singletons are
    /// registered, so DI resolves it whether or not this parameter is optional) — the default exists so
    /// the dozens of fixture-style specs across this repo that build a bare (store, resolver,
    /// specialStore) triple and never touch show identity at all stay unchanged. Omitting it here only
    /// means an operator's show edit would need an unrelated schedule/specials write (or a restart) to
    /// reach that ONE fixture's own cache — never a production configuration.
    /// </param>
    public CachingScheduleResolver(
        IScheduleStore store, ScheduleResolver resolver, IScheduleSpecialStore specialStore, IShowStore? showStore = null)
    {
        this.store = store;
        this.resolver = resolver;
        this.specialStore = specialStore;
        store.WeekChanged += OnWeekChanged;
        specialStore.SpecialsChanged += OnSpecialsChanged;
        if (showStore is not null)
            showStore.ShowChanged += OnShowChanged;
    }

    /// <summary>
    /// Resolves the current <see cref="OnAirSnapshot"/> against the cached week snapshot and specials
    /// list, reloading either from its own store first only when nothing has been cached yet, a write has
    /// invalidated it since the last reload, or — specials only — station-local "today" has rolled past
    /// the date the cached specials list was loaded for (see this type's own remarks).
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

        // Station-local "today" delegates to ScheduleResolver.StationToday() (PLAN T260 review SF4)
        // rather than this type resolving its own clock independently — the SAME method Resolve() below
        // will itself use to decide "today"/"tomorrow" for these very specials, so the two can never
        // silently disagree about what day it is.
        var today = resolver.StationToday();
        if (specialsDirty || specials is null || today.DayNumber != specialsLoadedDateNumber)
        {
            // Same clear/stamp-BEFORE-the-await discipline as the week snapshot above, and for the
            // identical reason: a SpecialsChanged invalidation (or a day rollover the very instant this
            // load is in flight) must not be silently lost the moment this load completes.
            specialsDirty = false;
            specialsLoadedDateNumber = today.DayNumber;
            specials = await specialStore.ListUpcomingAsync(today, ct);
        }

        return resolver.Resolve(snapshot, specials);
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
        // specials (PLAN T260) reads the same way — no null check needed before passing it on:
        // ScheduleResolver.Resolve's own specials parameter already treats null as "none" (the pre-T258
        // call shape), so the boot window before the very first ResolveAsync load (specials still null
        // here too) degrades to "no specials yet" exactly the way it degrades to no snapshot at all.
        return current is null ? null : resolver.Resolve(current, specials);
    }

    /// <summary>
    /// Synchronous, in-memory read of the cached <see cref="ScheduleWeekSnapshot"/> ITSELF (PLAN
    /// T286) — the sibling <see cref="TryGetCurrent"/> re-derives only an <see cref="OnAirSnapshot"/>
    /// from (who/what is on right now). <c>CrosstalkStockWorker</c> needs the raw snapshot because
    /// <c>CrosstalkPlanner.TryCastAsync</c> casts by walking the WHOLE grid's own cyclic adjacency
    /// (the next block AND the previous block), which a single on-air answer cannot reconstruct. No
    /// store round trip, and no staleness beyond what <see cref="TryGetCurrent"/>'s own remarks already
    /// document for the identical cached reference (at most one unit stale in production).
    /// <see langword="null"/> before the very first <see cref="ResolveAsync"/> call has completed, on
    /// the same terms as <see cref="TryGetCurrent"/>.
    /// </summary>
    public ScheduleWeekSnapshot? TryGetCurrentWeekSnapshot() => snapshot;

    void OnWeekChanged() => dirty = true;

    void OnSpecialsChanged() => specialsDirty = true;

    // PLAN T360 review HIGH-1: a show edit (name/tagline/flavor/rotation) can land on EITHER the
    // cached week snapshot's own ShowSummary (a weekly block naming that show) or the cached specials
    // list's own ShowSummary (a special naming it) — this repository has no way to know which without
    // re-deriving the store's own join, so both are dirtied unconditionally, the same "cheap, always
    // correct, worst case one redundant reload" posture ResolveAsync's own remarks already accept for
    // WeekChanged/SpecialsChanged individually.
    void OnShowChanged()
    {
        dirty = true;
        specialsDirty = true;
    }
}
