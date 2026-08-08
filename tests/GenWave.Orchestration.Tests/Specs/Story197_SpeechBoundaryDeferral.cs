// STORY-197 — Speech waits for the track boundary
//
// BDD specification — xUnit (SPEC F74.1, F74.2, F74.4). PLAN T42 wires the station-id cadence
// trigger through SpeechDeferralQueue (see Story136_StationIdCadence.cs, kept green unchanged —
// that producer's own "every N units, never at boot" behavior is untouched by this seam).
//
// These specs exercise the QUEUE's consumer/producer contract directly rather than re-proving
// cadence math: ScenarioBoundaryAiring enqueues a deferral the way a future wall-clock-scheduled
// producer would (cadence is OFF throughout, so the only station id that can air comes from the
// manually-enqueued deferral), then drives the real Orchestrator decision loop to prove it lands
// at the very next boundary and never mid-track.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureSpeechBoundaryDeferral
{
    static MediaReference MakeRef(string id) => new(
        id,
        $"/media/{id}.mp3",
        $"Track {id}",
        new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    // Cadence off: LeadIn/BackAnnounce never contribute an extra item, and the unit-count
    // trigger never fires on its own — the only station id in these specs is the one a test
    // enqueues directly into the SpeechDeferralQueue, standing in for a future deferred producer.
    static CadenceConfig CadenceOff => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    static Orchestrator BuildOrchestrator(CadenceConfig cadence, SpeechDeferralQueue deferralQueue, TimeProvider clock) =>
        new(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(cadence),
            new FakeRotationSettingsProvider(new RotationSettings()),
            new MusicSelectionPolicy(new FakeMediaCatalog(MakeRef("track")), NullLogger<MusicSelectionPolicy>.Instance),
            new FakeTtsSegmentSource(),
            new FakeActivePersonaAccessor(),
            NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            deferralQueue,
            clock,
            new FakeBoundaryBiasProvider(TimeSpan.Zero));

    static bool IsStationId(MediaItem item) =>
        item.MediaId.StartsWith("tts:stationid", StringComparison.OrdinalIgnoreCase);

    static bool IsMusic(MediaItem item) =>
        !item.MediaId.StartsWith("tts:", StringComparison.Ordinal);

    public static class ScenarioBoundaryAiring
    {
        [Fact]
        public static async Task Deferred_ident_airs_at_the_boundary_never_mid_track()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-20T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);
            var orchestrator = BuildOrchestrator(CadenceOff, queue, clock);
            var ctx = new PlayoutContext([]);

            // Given an ident deferral queued mid-track: the first unit is already fully handed
            // to the caller (its one-item buffer drained) and nothing new has been planned yet —
            // exactly the moment a track is on air with nothing queued behind it.
            var firstTrack = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(firstTrack);
            Assert.True(IsMusic(firstTrack));

            queue.Enqueue(SpeechDeferralKind.StationId, "test: mid-track trigger");

            // When the current track ends and the next unit is planned...
            var afterBoundary = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // Then the ident airs at the boundary — the very next item — and the queue is
            // drained (nothing left to leak into a later, wrong boundary).
            Assert.NotNull(afterBoundary);
            Assert.True(IsStationId(afterBoundary));
            Assert.Null(queue.NextDue);

            // ...and it never interrupted a track: the item right after it is plain music, not
            // a second speech segment spliced in.
            var nextTrack = await orchestrator.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(nextTrack);
            Assert.True(IsMusic(nextTrack));
        }
    }

    public static class ScenarioOrderedDrain
    {
        // T124 review finding F1: TryDequeueDue used to return Dictionary.Values' raw enumeration
        // order, which silently stops matching insertion order once a slot is freed (by a prior
        // Remove) and reused by a later Enqueue — .NET's dictionary reuses freed slots LIFO. Two
        // deferrals of two fixed kinds (SignOff/SignOn), drained-then-re-enqueued across two
        // consecutive boundaries, is exactly the cycle that flips the SECOND boundary's order — this
        // fact pins that the queue's own Due-ascending, kind-tiebreak contract holds at BOTH, not
        // just the first (which happened to still match insertion order by accident).

        [Fact]
        public static void SignOffPrecedesSignOnAtEachOfTwoConsecutiveBoundaries()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-20T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);

            // Boundary 1 — both due, sign-off first.
            queue.Enqueue(SpeechDeferralKind.SignOff, "boundary 1 sign-off", clock.GetUtcNow().AddSeconds(15));
            queue.Enqueue(SpeechDeferralKind.SignOn, "boundary 1 sign-on", clock.GetUtcNow().AddSeconds(30));
            clock.Advance(TimeSpan.FromSeconds(30));
            var firstBoundary = queue.TryDequeueDue(clock.GetUtcNow());

            Assert.Equal(2, firstBoundary.Count);
            Assert.Equal(SpeechDeferralKind.SignOff, firstBoundary[0].Kind);
            Assert.Equal(SpeechDeferralKind.SignOn, firstBoundary[1].Kind);

            // Boundary 2 — the SAME two kinds enqueued again, reusing the slots boundary 1 just freed.
            queue.Enqueue(SpeechDeferralKind.SignOff, "boundary 2 sign-off", clock.GetUtcNow().AddSeconds(15));
            queue.Enqueue(SpeechDeferralKind.SignOn, "boundary 2 sign-on", clock.GetUtcNow().AddSeconds(30));
            clock.Advance(TimeSpan.FromSeconds(30));
            var secondBoundary = queue.TryDequeueDue(clock.GetUtcNow());

            Assert.Equal(2, secondBoundary.Count);
            Assert.Equal(SpeechDeferralKind.SignOff, secondBoundary[0].Kind);
            Assert.Equal(SpeechDeferralKind.SignOn, secondBoundary[1].Kind);
        }
    }

    public static class ScenarioSupersede
    {
        [Fact]
        public static void Newer_deferral_of_same_kind_replaces_the_stale_one()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-20T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);

            // Given two idents of the same kind queued across one long track...
            queue.Enqueue(SpeechDeferralKind.StationId, "stale ident");
            clock.Advance(TimeSpan.FromMinutes(5)); // still mid-track — the long track hasn't ended
            queue.Enqueue(SpeechDeferralKind.StationId, "fresh ident");

            // When the boundary arrives...
            var due = queue.TryDequeueDue(clock.GetUtcNow());

            // Then only the newer airs — the stale one was discarded at the second Enqueue and
            // never reaches the drain at all.
            var aired = Assert.Single(due);
            Assert.Equal("fresh ident", aired.Reason);
        }
    }

    public static class SadPathRestart
    {
        [Fact]
        public static async Task Deferrals_regenerate_after_restart_and_nothing_double_airs()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-20T00:00:00Z"));
            var cadence = new CadenceConfig
            {
                LeadInBeforeEachTrack = false,
                BackAnnounceAfterEachTrack = false,
                StationIdEveryNUnits = 2,
            };
            var ctx = new PlayoutContext([]);

            // Given queued deferrals: the pre-restart process runs far enough (4 units) for its
            // cadence (N=2) to have produced exactly one station id.
            var beforeRestart = BuildOrchestrator(cadence, new SpeechDeferralQueue(clock), clock);
            var producedBeforeRestart = new List<MediaItem>();
            for (var i = 0; i < 4; i++)
            {
                var item = await beforeRestart.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotNull(item);
                producedBeforeRestart.Add(item);
            }
            Assert.Single(producedBeforeRestart, IsStationId);

            // When the schedule state is rebuilt: a host restart drops the whole process — a
            // brand new queue (empty, F74.4 — nothing persisted to leak forward) and a brand new
            // Orchestrator (its unit counter, the "schedule state" F74.4 regenerates from,
            // restarts at zero exactly like a fresh boot, SPEC F42.1).
            var afterRestart = BuildOrchestrator(cadence, new SpeechDeferralQueue(clock), clock);

            // Then due deferrals regenerate — nothing carried over survives to double-air on the
            // very first post-restart unit...
            var firstAfterRestart = await afterRestart.GetNextAsync(ctx, CancellationToken.None);
            Assert.NotNull(firstAfterRestart);
            Assert.True(IsMusic(firstAfterRestart));

            // ...and the cadence naturally reproduces the next due ident once N units elapse
            // again post-restart — exactly once, never duplicated with the pre-restart one.
            var producedAfterRestart = new List<MediaItem> { firstAfterRestart };
            for (var i = 0; i < 3; i++)
            {
                var item = await afterRestart.GetNextAsync(ctx, CancellationToken.None);
                Assert.NotNull(item);
                producedAfterRestart.Add(item);
            }
            Assert.Single(producedAfterRestart, IsStationId);
        }
    }
}
