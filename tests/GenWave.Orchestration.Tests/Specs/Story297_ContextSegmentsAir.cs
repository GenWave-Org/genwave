// STORY-297 — Context segments air at boundaries (F107.3, F107.4, F107.7)

using GenWave.Orchestration.Tests.Fakes;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureContextSegmentsAirAtBoundaries
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioADueSegmentAirsAtTheBoundary
    {
        [Fact(Skip = "Pending T224 — see docs/PLAN.md")]
        public void AnEnqueuedContextDeferralDrainsAtTheNextUnitSeam()
        {
            // Enqueue a due ContextSegment deferral; GetNextAsync plans the next unit;
            // the rendered segment lands in the buffer ahead of the track (F74.1).
            // Assert.Equal(SegmentKind.ContextSegment, bufferedItem.SegmentKind);
            Assert.Fail("pending T224");
        }

        [Fact(Skip = "Pending T224 — see docs/PLAN.md")]
        public void TheCopyRequestCarriesProviderFactsWithTheNewsPosture()
        {
            // The SegmentRequest/prompt carries the provider's facts and the
            // "do not add facts" instruction; copy is FreshPerAiring (blurbs cache).
            // Assert.Contains("do not add facts", capturedPrompt);
            Assert.Fail("pending T224");
        }
    }

    public sealed class ScenarioPerProviderSupersede
    {
        [Fact]
        public void WeatherAndHistoryPendingTogetherBothDrain()
        {
            // Two ContextSegment deferrals with different discriminators coexist — supersede is
            // per (kind, discriminator), so a due weather fact never silently discards a due
            // history fact (SPEC F107.4).
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);

            queue.Enqueue(SpeechDeferralKind.Context, "weather cadence elapsed", discriminator: "weather");
            queue.Enqueue(SpeechDeferralKind.Context, "history cadence elapsed", discriminator: "history");

            var drained = queue.TryDequeueDue(clock.GetUtcNow());

            Assert.Equal(2, drained.Count);
            Assert.Contains(drained, deferral => deferral.Discriminator == "weather");
            Assert.Contains(drained, deferral => deferral.Discriminator == "history");
            Assert.Null(queue.NextDue); // both consumed — nothing left leaking into a later boundary
        }

        [Fact]
        public void TwoWeatherDeferralsCollapseToTheNewer()
        {
            // Same (kind, discriminator) pair still supersedes (F74.2 semantics, now scoped to the
            // pair rather than the bare kind): the older weather deferral is discarded at the second
            // Enqueue and never reaches the drain at all.
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);

            queue.Enqueue(SpeechDeferralKind.Context, "stale weather", discriminator: "weather");
            clock.Advance(TimeSpan.FromMinutes(5)); // still mid-track — nothing has drained yet
            queue.Enqueue(SpeechDeferralKind.Context, "fresh weather", discriminator: "weather");

            var drained = queue.TryDequeueDue(clock.GetUtcNow());

            var aired = Assert.Single(drained);
            Assert.Equal("fresh weather", aired.Reason);
            Assert.Equal("weather", aired.Discriminator);
        }

        [Fact]
        public void NullDiscriminatorKindsBehaveExactlyAsToday()
        {
            // Pins the byte-identical claim (SPEC F107.4): every kind that predates F107 (StationId
            // here) always enqueues with a null discriminator, so it drives the SAME supersede code
            // path Story197_SpeechBoundaryDeferral's ScenarioSupersede fact already covers — this
            // reproduces that exact scenario through the (kind, discriminator) seam and additionally
            // pins that the surviving entry's own Discriminator reads back null.
            var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-08T00:00:00Z"));
            var queue = new SpeechDeferralQueue(clock);

            queue.Enqueue(SpeechDeferralKind.StationId, "stale ident");
            clock.Advance(TimeSpan.FromMinutes(5)); // still mid-track — the long track hasn't ended
            queue.Enqueue(SpeechDeferralKind.StationId, "fresh ident");

            var due = queue.TryDequeueDue(clock.GetUtcNow());

            var aired = Assert.Single(due);
            Assert.Equal("fresh ident", aired.Reason);
            Assert.Null(aired.Discriminator);
        }
    }

    public sealed class ScenarioPersonaAssignment
    {
        [Fact(Skip = "Pending T224 — see docs/PLAN.md")]
        public void PersonaIdZeroRendersInTheOnAirVoice()
        {
            // Context:{Key}:PersonaId = 0 during a staffed segment ⇒ on-air DJ's voice.
            // Assert.Equal(onAirVoice, capturedRequest.Voice);
            Assert.Fail("pending T224");
        }

        [Fact(Skip = "Pending T224 — see docs/PLAN.md")]
        public void MusicOnlySegmentsRenderInTheStationVoice()
        {
            // Music-only segment or gap ⇒ station voice (the ident precedent).
            // Assert.Equal(stationVoice, capturedRequest.Voice);
            Assert.Fail("pending T224");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFailedRenderNeverBlocksMusic
    {
        [Fact(Skip = "Pending T224 — see docs/PLAN.md")]
        public void ANullRenderDropsTheSegmentAndMusicContinues()
        {
            // ITtsSegmentSource returns null ⇒ no context item in the buffer, the music
            // unit is untouched, one Information/WARN line names the drop.
            // Assert.DoesNotContain(buffer, i => i.SegmentKind == SegmentKind.ContextSegment);
            Assert.Fail("pending T224");
        }
    }
}
