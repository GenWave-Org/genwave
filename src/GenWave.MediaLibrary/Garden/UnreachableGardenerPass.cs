using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Garden;

/// <summary>
/// The <see cref="RotKind.Unreachable"/> pass (SPEC F153.8; STORY-378; PLAN T376, gh-#529) — a thin,
/// Dapper-free orchestrator (L2 as narrowed at T357), the SAME shape every sibling pass already
/// establishes: every statement lives in <see cref="RotFindingRepository.ReconcileUnreachableAsync"/>;
/// this type's own job is building the DISTINCT set of effective envelope tuples the store
/// reconciles against, via <see cref="ScheduleSegment.EffectiveEnvelope"/> (T376 review MED-4: the
/// per-field fallback is now ONE piece of code, shared byte-for-byte with
/// <c>GenWave.Orchestration.ScheduleResolver.BuildSegmentEnvelope</c> — this type never re-derives
/// it). An empty grid (no segments at all) uses the station default as the WHOLE envelope — the same
/// rule <c>ScheduleResolver.ResolveGap</c> already applies for a schedule with no blocks (SPEC F91.4).
///
/// <para>
/// <see cref="IScheduleStore"/>/<see cref="IStationDefaultEnvelopeSource"/> are both Core/Abstractions
/// ports (L10) — resolved lazily via constructor injection here exactly like every other dependency
/// in this codebase; <c>MediaLibraryServiceCollectionExtensions.AddMediaLibrary</c> never needs to
/// care which assembly registers their effective adapters, or in what order, since DI resolves both
/// singletons on first use, not at registration time.
/// </para>
///
/// <para>
/// <b>Registered before shelf_dust</b> (<c>MediaLibraryServiceCollectionExtensions</c>) — SPEC
/// F153.7's own shelf_dust predicate reads THIS kind's own open findings, so unreachable must
/// reconcile first in every tick.
/// </para>
/// </summary>
sealed class UnreachableGardenerPass(
    IRotFindingStore store,
    IScheduleStore schedule,
    IStationDefaultEnvelopeSource stationDefault) : IGardenerPass
{
    public RotKind Kind => RotKind.Unreachable;

    public async Task RunAsync(CancellationToken ct)
    {
        var week = await schedule.LoadWeekAsync(ct);
        await store.ReconcileUnreachableAsync(BuildDistinctTuples(week.Segments, stationDefault.Current), ct);
    }

    /// <summary>Builds the DISTINCT effective envelope tuples for the current grid — private (the
    /// DeadFileGardenerPass/ShelfDustGardenerPass T372/T375 review LOW-1 precedent): only
    /// <see cref="RunAsync"/> calls this, and Story378's own facts exercise it exclusively through
    /// the real pass's observable behaviour (the tuples the repository actually reconciles against),
    /// never a direct call. An empty <paramref name="segments"/> list (no schedule grid at all)
    /// returns the station default alone (SPEC F91.4's "the station default is the whole envelope"
    /// rule) — there is no <see cref="ScheduleSegment"/> to call <see cref="ScheduleSegment.EffectiveEnvelope"/>
    /// on in that case, so this is the one place the station default is read directly rather than
    /// through that method.</summary>
    static IReadOnlyList<EnvelopeTuple> BuildDistinctTuples(
        IReadOnlyList<ScheduleSegment> segments, SegmentEnvelope stationDefaultEnvelope)
    {
        if (segments.Count == 0)
            return [ToTuple(stationDefaultEnvelope)];

        return segments
            .Select(s => ToTuple(s.EffectiveEnvelope(stationDefaultEnvelope)))
            .Distinct()
            .ToList();
    }

    /// <summary>Lower-cases, de-duplicates, and sorts <paramref name="envelope"/>'s own genres so two
    /// segments naming the same genres in a different order or casing fold to the textually IDENTICAL
    /// tuple (T376 ORCHESTRATOR ruling: "distinctness is textual") — the SAME lower-casing
    /// <c>MediaRepository.GetEnvelopeCandidateAsync</c>'s own <c>genresLower</c> already applies (T376
    /// review LOW-1: no <c>.Trim()</c> — that call site never trims either, at
    /// <c>MediaRepository.cs:462</c>; a genre tag carrying stray whitespace is a data-entry problem
    /// for the schedule write boundary to reject, a follow-up, not something this READ-side pass
    /// should silently paper over), done here once so every envelope this pass hands the store
    /// already matches <c>RotFindingRepository</c>'s own <c>lower(m.genre) = any(e.genres)</c>
    /// predicate with no further casing work on the SQL side.</summary>
    static EnvelopeTuple ToTuple(SegmentEnvelope envelope) => new(
        envelope.Genres
            .Select(g => g.ToLowerInvariant())
            .Distinct()
            .OrderBy(g => g, StringComparer.Ordinal)
            .ToList(),
        envelope.EnergyRange.Min,
        envelope.EnergyRange.Max);
}
