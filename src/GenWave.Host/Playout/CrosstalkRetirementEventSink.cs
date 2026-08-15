using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Orchestration;

namespace GenWave.Host.Playout;

/// <summary>
/// The host's <see cref="IStationEventSink"/> binding for crosstalk retire-at-air (SPEC F127.7,
/// STORY-329, PLAN T287) — mirrors <see cref="PlayHistoryEventSink"/>'s own "forward
/// <see cref="TrackAired"/>, ignore everything else" shape one seam over. Lives in THIS namespace
/// (rather than <c>GenWave.Host.Crosstalk</c>, where the rest of the crosstalk-specific Host wiring
/// sits) deliberately: <c>GenWave.Host.Crosstalk.CrosstalkStockWorker</c> already depends on
/// <see cref="NowPlayingService"/>/<see cref="OnAirRenderGate"/> from THIS namespace, so a sink placed
/// in <c>GenWave.Host.Crosstalk</c> and composed from <c>PlayoutServiceCollectionExtensions</c> (this
/// namespace) would close a two-namespace cycle within the <c>GenWave.Host</c> project (gh-#445's own
/// fitness law) — co-locating with <see cref="PlayHistoryEventSink"/> instead keeps the dependency
/// one-directional (<c>Crosstalk -> Playout</c> only), with the cross-project reference to
/// <see cref="CrosstalkPlanner"/> (a normal <c>GenWave.Orchestration</c> dependency, exactly like
/// <c>INextItemProvider</c> already is here) carrying the actual coupling.
///
/// Retirement is deliberately driven by THIS genuine, engine-confirmed air-time signal — never by
/// <c>Orchestrator</c>'s own plan-time enqueue, and never by the feeder's own push — deleting the
/// asset any earlier risks the engine not having necessarily finished reading the file yet (see
/// <see cref="CrosstalkPlanner.RetireByMediaId"/>'s own remarks).
///
/// <para>
/// <paramref name="crosstalkPlanner"/> is OPTIONAL (default <see langword="null"/>), deliberately
/// resolved with no factory here — <c>PlayoutServiceCollectionExtensions.AddGenWavePlayout</c> and
/// <c>GenWave.Host.Crosstalk.CrosstalkHostServiceCollectionExtensions.AddGenWaveCrosstalkHost</c> are
/// two independent registration methods with no ordering dependency between them (Program.cs happens
/// to call the latter after the former, but nothing here requires that): a host that never wires the
/// crosstalk feature at all leaves this a permanent, harmless no-op rather than a composition-time
/// failure.
/// </para>
///
/// <para>
/// <b>The third outcome (round-2 review F-B, stated honestly): this sink is not a safety net for a
/// pushed exchange that never airs.</b> <see cref="Publish"/> only ever forwards a genuine,
/// engine-confirmed <see cref="TrackAired"/> for the exact <see cref="SegmentKind.Crosstalk"/> row
/// <c>CrosstalkPlanner.MarkVended</c> was told about — an exchange marked vended whose own advance
/// never arrives (a lost push, a process restart mid-flight) simply never reaches this method at all.
/// That entry — roughly 200 bytes plus one asset file on disk — leaks for the remainder of THIS
/// process's life; nothing sweeps it sooner. See <c>CrosstalkPlanner.MarkVended</c>'s own remarks for
/// why that one-entry-per-unconfirmed-vend leak, bounded to a single process's uptime by the NEXT
/// process's own <c>CrosstalkStockWorker</c> startup purge, is the deliberate posture — an eviction
/// timer was considered and rejected as speculative machinery this sink was never meant to add.
/// </para>
/// </summary>
sealed class CrosstalkRetirementEventSink(CrosstalkPlanner? crosstalkPlanner = null) : IStationEventSink
{
    public void Publish(StationEvent evt)
    {
        if (crosstalkPlanner is not null && evt is TrackAired { SegmentKind: SegmentKind.Crosstalk } t)
            crosstalkPlanner.RetireByMediaId(t.MediaId);
    }
}
