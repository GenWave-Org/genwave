// STORY-198 — Selection avoids burying the boundary
//
// BDD specification — xUnit (SPEC F74.3). Implements PLAN T43: fills in the two pending facts left
// by /spec, driving the real Orchestrator.GetNextAsync -> SelectMusicCandidateAsync seam through
// fakes — never re-testing SpeechDeferralQueue.NextDue's own contract (Story197 already covers
// that).
//
// Both scenarios enqueue a deferral directly into the queue with an explicit FUTURE due time, the
// same idiom Story197_SpeechBoundaryDeferral uses to stand in for a producer's own trigger: today's
// only real producer (station-id cadence) always enqueues due=now (SPEC F74.1), so a future-dated
// deferral is exactly what a scheduled-handoff producer will look like once one ships (T43 dispatch
// note) — the bias seam is built and proven here regardless.

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureBoundaryAwareSelection
{
    // Starts well past "today" purely so the drain step's real clock read (Orchestrator now reads
    // the SAME injected TimeProvider the queue uses, SPEC F74.3) can never accidentally consider the
    // deferral already due — irrelevant to what these specs assert (which MUSIC track was picked),
    // but keeping the enqueued ident pending-not-drained keeps the returned item unambiguously music.
    static readonly DateTimeOffset ClockStart = DateTimeOffset.Parse("2030-01-01T00:00:00Z");

    static MediaReference MakeTrack(string id, TimeSpan duration) => new(
        MediaId: id,
        Locator: $"/media/{id}.mp3",
        Title: $"Track {id}",
        Loudness: new Loudness(-23.0, -1.0, true),
        DurationMs: (int)duration.TotalMilliseconds,
        SampleRate: null,
        Channels: null,
        BitrateKbps: null,
        Artist: null,
        Album: null,
        Genre: null,
        Year: null);

    static CadenceConfig CadenceOff => new()
    {
        LeadInBeforeEachTrack = false,
        BackAnnounceAfterEachTrack = false,
        StationIdEveryNUnits = 0,
    };

    static Orchestrator BuildOrchestrator(
        FakeMediaCatalog catalog, SpeechDeferralQueue deferralQueue, TimeProvider clock, TimeSpan lookahead) =>
        new(
            new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default")),
            new FakeStationScopeProvider(new LibraryScope([1L])),
            new FakeCadenceProvider(CadenceOff),
            new FakeRotationSettingsProvider(new RotationSettings()),
            catalog,
            new FakeTtsSegmentSource(),
            new FakeActivePersonaAccessor(),
            NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            deferralQueue,
            clock,
            new FakeBoundaryBiasProvider(lookahead));

    static bool IsMusic(MediaItem item) =>
        !item.MediaId.StartsWith("tts:", StringComparison.Ordinal);

    public static class ScenarioBiasNearDeadline
    {
        [Fact]
        public static async Task Shorter_track_wins_when_an_ident_is_due_soon()
        {
            // Given an ident due in 3 minutes and two otherwise-equal candidates of 3 and 9 minutes
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 3 minutes",
                clock.GetUtcNow() + TimeSpan.FromMinutes(3));

            var threeMinute = MakeTrack("three-min", TimeSpan.FromMinutes(3));
            var nineMinute = MakeTrack("nine-min", TimeSpan.FromMinutes(9));
            var catalog = FakeMediaCatalog.WithPool([threeMinute, nineMinute]);

            var orchestrator = BuildOrchestrator(catalog, queue, clock, TimeSpan.FromMinutes(10));
            var ctx = new PlayoutContext([]);

            // When the next track is selected...
            var next = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // Then the 3-minute track is chosen — its predicted end lands exactly on the due time,
            // the 9-minute track's six minutes late (F74.3).
            Assert.NotNull(next);
            Assert.True(IsMusic(next));
            Assert.Equal(threeMinute.MediaId, next.MediaId);
        }
    }

    public static class ScenarioSoftNeverAFilter
    {
        [Fact]
        public static async Task Only_long_tracks_available_still_selects_a_track()
        {
            // Given only long tracks available near a deadline...
            var clock = new FakeTimeProvider(ClockStart);
            var queue = new SpeechDeferralQueue(clock);
            queue.Enqueue(
                SpeechDeferralKind.StationId, "test: due in 2 minutes",
                clock.GetUtcNow() + TimeSpan.FromMinutes(2));

            var longA = MakeTrack("long-a", TimeSpan.FromMinutes(9));
            var longB = MakeTrack("long-b", TimeSpan.FromMinutes(12));
            var catalog = FakeMediaCatalog.WithPool([longA, longB]);

            var orchestrator = BuildOrchestrator(catalog, queue, clock, TimeSpan.FromMinutes(10));
            var ctx = new PlayoutContext([]);

            // When selection runs...
            var next = await orchestrator.GetNextAsync(ctx, CancellationToken.None);

            // Then a track is still selected — the pool never empties for boundary reasons, even
            // though nothing on offer lands anywhere near the due time.
            Assert.NotNull(next);
            Assert.True(IsMusic(next));
            Assert.Contains(next.MediaId, new[] { longA.MediaId, longB.MediaId });
        }
    }
}
