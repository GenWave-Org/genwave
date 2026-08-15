// STORY-329 — Retire at air (SPEC F127.7, PLAN T287, round-2 review finding F4)
//
// BDD specification — xUnit. CrosstalkPlanner.MarkVended/RetireByMediaId and their Host-side binding,
// CrosstalkRetirementEventSink, had ZERO coverage before this task: the sink's own remarks describe it
// as a thin TrackAired -> RetireByMediaId forward, but nothing proved that forward actually ran, that
// it was scoped to the RIGHT MediaId, or that it was scoped to the RIGHT SegmentKind. Drives the REAL
// CrosstalkRetirementEventSink against a REAL CrosstalkPlanner (only IPersonaStore/ICrosstalkScopeProvider
// are stubbed — neither is ever reached by MarkVended/RetireByMediaId/Retire, mirrors
// Story328_CrosstalkStockWorker.cs's own NeverCalledPersonaStore precedent).
//
// The third outcome MarkVended/RetireByMediaId/CrosstalkRetirementEventSink's own docs now name
// honestly (round-2 review F4): a pushed-but-never-aired exchange (MarkVended called, but the SAME
// MediaId's TrackAired never arrives — a lost push, a process restart before air) is a bounded leak
// (at most StockTargetPerShow-many exchanges plus whatever is in flight, ≤2+1 per show), swept only by
// the NEXT process's own CrosstalkStockWorker startup purge, never by this sink. That posture is not
// re-proven here (it is a purge-side fact, already covered by CrosstalkStockWorker's own
// PurgeStaleAssets tests) — this file proves the THREE outcomes the sink itself is responsible for.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Core.Events;
using GenWave.Host.Playout;
using GenWave.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;

// This test project also references GenWave.Loudness (see the csproj comment), which brings the
// `GenWave.Loudness` namespace into scope and shadows the unqualified `Loudness` domain type name
// (mirrors Story328_CrosstalkStockWorker.cs's own identical alias precedent).
using CoreLoudness = GenWave.Core.Domain.Loudness;

namespace GenWave.Host.Tests.Specs;

// Minimal, local IPersonaStore/ICrosstalkScopeProvider doubles — MarkVended/RetireByMediaId/Retire
// never reach either seam, so every member below is a throwing stub except what CrosstalkPlanner's
// constructor merely needs to exist (mirrors Story328_CrosstalkStockWorker.cs's own precedent; a
// file-scoped type cannot cross files, so this is its own copy, not a shared one).

file sealed class NeverCalledPersonaStore : IPersonaStore
{
    public Task<IReadOnlyList<Persona>> GetAllAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<Persona?> GetByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaWriteResult> CreateAsync(PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaWriteResult> UpdateAsync(long id, PersonaDraft draft, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaWriteResult> DeleteAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task<PersonaCard?> GetCardByIdAsync(long id, CancellationToken ct) => throw new NotSupportedException();
    public Task<long?> GetIdBySlugAsync(string slug, CancellationToken ct) => throw new NotSupportedException();
}

file sealed class NeverCalledCrosstalkScopeProvider : ICrosstalkScopeProvider
{
    public IReadOnlyList<string> EnabledShows => throw new NotSupportedException();
    public int EveryNthAiring => throw new NotSupportedException();
}

public static class FeatureCrosstalkRetireAtAir
{
    static StockedCrosstalkExchange MakeExchange(string assetPath) =>
        new("morning-mix", new CrosstalkCast(10, 20), assetPath, new CoreLoudness(-16.0, -1.0, true), Cue: null, DurationMs: 6_000);

    static (CrosstalkPlanner Planner, CrosstalkRetirementEventSink Sink) BuildSink()
    {
        var planner = new CrosstalkPlanner(
            new NeverCalledPersonaStore(), new NeverCalledCrosstalkScopeProvider(), NullLogger<CrosstalkPlanner>.Instance);
        return (planner, new CrosstalkRetirementEventSink(planner));
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public static class ScenarioAConfirmedAirRetiresTheAsset
    {
        [Fact]
        public static void A_TrackAired_for_the_vended_MediaId_and_Kind_Crosstalk_deletes_the_asset()
        {
            // Given an exchange marked as vended under a MediaId, on its way to the playout buffer
            var (planner, sink) = BuildSink();
            var assetPath = Path.GetTempFileName();
            planner.MarkVended("tts:crosstalk:abc", MakeExchange(assetPath));

            // When the SAME MediaId genuinely airs (the engine-confirmed TrackAired signal)
            sink.Publish(new TrackAired(
                "tts:crosstalk:abc", "GenWave", "Nova", -2.0, DateTimeOffset.UtcNow, 6_000,
                SegmentKind: SegmentKind.Crosstalk));

            // Then its asset is deleted
            Assert.False(File.Exists(assetPath));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public static class ScenarioADifferentMediaIdIsUntouched
    {
        [Fact]
        public static void A_TrackAired_for_a_different_MediaId_leaves_the_asset_alone()
        {
            // Given an exchange awaiting confirmation under ITS OWN MediaId
            var (planner, sink) = BuildSink();
            var assetPath = Path.GetTempFileName();
            planner.MarkVended("tts:crosstalk:abc", MakeExchange(assetPath));

            // When an UNRELATED Crosstalk MediaId airs instead
            sink.Publish(new TrackAired(
                "tts:crosstalk:unrelated", "GenWave", "Nova", -2.0, DateTimeOffset.UtcNow, 6_000,
                SegmentKind: SegmentKind.Crosstalk));

            // Then the awaited exchange's asset survives — nothing here confirmed ITS airing
            Assert.True(File.Exists(assetPath));
        }
    }

    public static class ScenarioNonCrosstalkKindsAreIgnored
    {
        [Fact]
        public static void A_TrackAired_for_the_same_MediaId_but_a_non_Crosstalk_kind_leaves_the_asset_alone()
        {
            // Given an exchange awaiting confirmation
            var (planner, sink) = BuildSink();
            var assetPath = Path.GetTempFileName();
            planner.MarkVended("tts:crosstalk:abc", MakeExchange(assetPath));

            // When a TrackAired for the SAME id arrives, but stamped a DIFFERENT SegmentKind — never
            // the Crosstalk row this awaiting entry is keyed for (a contrived combination for a
            // production caller, but the sharpest possible proof the sink dispatches on SegmentKind,
            // not merely MediaId)
            sink.Publish(new TrackAired(
                "tts:crosstalk:abc", "GenWave", "Nova", -2.0, DateTimeOffset.UtcNow, 6_000,
                SegmentKind: SegmentKind.LeadIn));

            // Then the asset is untouched
            Assert.True(File.Exists(assetPath));
        }
    }
}
