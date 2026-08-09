// STORY-302 — The time announcement (F110.3)

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureTimeAnnouncements
{
    // TheCopyIsTemplatedPerHourAndStationVoiced/TheSameHourIsACacheHit/ANullRenderAirsNothingAndMusicContinues
    // (PLAN T232) exercise the ORCHESTRATOR's own drain arm, not the producer above them — mirrors
    // Story297_ContextSegmentsAir.cs's harness idiom (a real Orchestrator wired to fakes at the
    // tts/clock seams). The actual cache-hit MECHANISM (TtsSegmentSource's hash-based file-exists
    // short circuit) is a Tts-level fact this project cannot see — proven in
    // GenWave.Tts.Tests/Specs/Story302_TimeAnnouncements.cs instead (mirrors Story297's own split).

    static MediaReference MakeTrackRef(string id) => new(
        id, $"/media/{id}.mp3", $"Track {id}", new Loudness(-23.0, -1.0, true),
        null, null, null, null, null, null, null, null);

    static bool IsMusic(MediaItem item) =>
        !item.MediaId.StartsWith("tts:", StringComparison.Ordinal);

    static Orchestrator BuildOrchestrator(
        SpeechDeferralQueue queue, TimeProvider clock, FakeTtsSegmentSource tts, ILogger<Orchestrator>? logger = null)
    {
        var identityProvider = new FakeStationIdentityProvider(new StationIdentity("s1", "GenWave", "default"));
        var scopeProvider = new FakeStationScopeProvider(new LibraryScope([1L]));
        var cadenceProvider = new FakeCadenceProvider(new CadenceConfig
        {
            LeadInBeforeEachTrack = false,
            BackAnnounceAfterEachTrack = false,
            StationIdEveryNUnits = 0,
        });
        var rotationProvider = new FakeRotationSettingsProvider(new RotationSettings());
        var catalog = new FakeMediaCatalog(MakeTrackRef("t1"));
        var musicSelectionPolicy = new MusicSelectionPolicy(catalog, NullLogger<MusicSelectionPolicy>.Instance);

        return new Orchestrator(
            identityProvider, scopeProvider, cadenceProvider, rotationProvider, musicSelectionPolicy, tts,
            new FakeActivePersonaAccessor(),
            logger ?? NullLogger<Orchestrator>.Instance,
            new FakeRenderBudgetProvider(TimeSpan.FromSeconds(30)),
            queue, clock, new FakeBoundaryBiasProvider(TimeSpan.Zero));
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheEnumGainsItsFirstProducer
    {
        [Fact]
        public void TimeAnnouncementsOnEnqueuesATimeDateDeferral()
        {
            // Station:Imaging:TimeAnnouncements=true ⇒ the producer enqueues a
            // future-dated TimeDate deferral for the top of the hour.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 13, 45, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var settings = new FakeStationImagingSettingsProvider
            {
                Current = new StationImagingSettings(ClockAnchoredIdents: false, TimeAnnouncements: true),
            };
            var producer = new ClockAnchoredImagingProducer(queue, settings, clock);

            producer.Produce();

            var due = queue.PeekNextDue();
            Assert.NotNull(due);
            Assert.Equal(SpeechDeferralKind.TimeDate, due.Kind);
            Assert.Equal(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero), due.Due);
        }

        [Fact]
        public async Task TheCopyIsTemplatedPerHourAndStationVoiced()
        {
            // Drain renders templated per-hour copy ("It's two o'clock…"), station voice,
            // never LLM-authored (IsLlmAuthored excludes TimeDate).
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 14, 2, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(queue, clock, tts);

            // The top this announcement was ARMED for — 14:00, even though the drain itself lands
            // at 14:02 (clock above).
            var due = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", due);

            var first = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(first);
            var request = Assert.Single(tts.Requests, r => r.Kind == SegmentKind.TimeDate);
            Assert.Equal("default", request.Voice); // the station's own identity voice, gh-#96 precedent
            Assert.Null(request.PersonaName); // station-voiced, never a persona's
            Assert.Equal(due, request.LocalNow); // the armed-for top, not the 14:02 drain-time clock
        }

        [Fact]
        public async Task TheSameHourIsACacheHit()
        {
            // Second render of the same hour's text ⇒ forever-cache hit (templated copy,
            // FreshPerAiring=false ⇒ non-blurb path). The actual hash/file-exists mechanism is
            // Tts-level (see this file's own header) — what THIS project controls, and what MAKES
            // that cache hit possible, is proven here: two drains of the SAME hour-of-day (a day
            // apart — "the hour recurs," SPEC F110.3 AC2) thread the SAME hour into LocalNow, off
            // each deferral's own Due — never a fresh drain-time clock read that could smear across
            // whatever minute the drain happens to land on.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 14, 2, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource();
            var orchestrator = BuildOrchestrator(queue, clock, tts);

            var dueToday = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", dueToday);
            // A whole unit (TimeDate + music) is planned atomically on the FIRST pull and buffered —
            // drain the buffered music track too, so the SECOND pull below genuinely re-plans a new
            // unit rather than just dequeuing what the first pull already queued (GetNextAsync's own
            // "if (buffer.Count > 0) return buffer.Dequeue()" short-circuit).
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            clock.Advance(TimeSpan.FromDays(1));
            var dueTomorrow = new DateTimeOffset(2026, 8, 9, 14, 0, 0, TimeSpan.Zero);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", dueTomorrow);
            await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            var requests = tts.Requests.Where(r => r.Kind == SegmentKind.TimeDate).ToList();
            Assert.Equal(2, requests.Count);
            Assert.Equal(dueToday, requests[0].LocalNow);
            Assert.Equal(dueTomorrow, requests[1].LocalNow);
            Assert.Equal(requests[0].LocalNow.Hour, requests[1].LocalNow.Hour);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFailedRenderIsSilent
    {
        [Fact]
        public async Task ANullRenderAirsNothingAndMusicContinues()
        {
            // TTS failure on the TimeDate render ⇒ no announcement in the buffer, music
            // unit intact (F74.1 stands) — the ordinary silent skip every non-handoff/non-context
            // kind gets (contrast SignOff/SignOn's F92.4 WARN+booth-log ladder).
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 14, 2, 0, TimeSpan.Zero));
            var queue = new SpeechDeferralQueue(clock);
            var tts = new FakeTtsSegmentSource { ShouldReturnNull = r => r.Kind == SegmentKind.TimeDate };
            var orchestrator = BuildOrchestrator(queue, clock, tts);

            var due = new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero);
            queue.Enqueue(SpeechDeferralKind.TimeDate, "clock-anchored: station-local top of the hour", due);

            var item = await orchestrator.GetNextAsync(new PlayoutContext([]), CancellationToken.None);

            Assert.NotNull(item);
            Assert.True(IsMusic(item!));

            // The render was genuinely ATTEMPTED (not skipped upstream) — proven by the request
            // reaching the fake at all — and no TimeDate item ever reached the buffer (asserted via
            // IsMusic(item) above: GetNextAsync's ONE returned item is the music track, not the
            // dropped announcement).
            Assert.Equal(SegmentKind.TimeDate, Assert.Single(tts.Requests).Kind);
        }
    }
}
