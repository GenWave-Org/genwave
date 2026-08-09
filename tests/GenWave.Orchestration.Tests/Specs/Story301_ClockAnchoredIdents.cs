// STORY-301 — Top-of-hour idents from the imaging pool (F110.1, F110.2, gh-#381)

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureClockAnchoredIdents
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioClockAnchoredAndOptIn
    {
        [Fact(Skip = "Pending T230 — see docs/PLAN.md")]
        public void TheProducerEnqueuesAFutureDatedStationIdBeforeTheHour()
        {
            // ClockAnchoredImagingProducer with ClockAnchoredIdents=true and the station
            // clock approaching the hour ⇒ one future-dated StationId deferral, due at the top.
            // Assert.Equal(topOfHour, queue.NextDue);
            Assert.Fail("pending T230");
        }

        [Fact(Skip = "Pending T230 — see docs/PLAN.md")]
        public void SupersedeKeepsExactlyOnePending()
        {
            // Two producer ticks before the same hour ⇒ one pending deferral (F74.2).
            // Assert.Equal(1, queue.PendingCount(SpeechDeferralKind.StationId));
            Assert.Fail("pending T230");
        }

        [Fact(Skip = "Pending T232 — see docs/PLAN.md")]
        public void PoolFirstAiring()
        {
            // With a ready authored station_id row in the fake catalog, the drain airs the
            // authored MediaItem (no TTS render for the ident).
            // Assert.Equal(authoredItem.Id, bufferedItem.Id);
            Assert.Fail("pending T232");
        }

        [Fact(Skip = "Pending T232 — see docs/PLAN.md")]
        public void EmptyPoolFallsBackToTheTemplatedIdent()
        {
            // Empty pool ⇒ today's templated TTS ident renders unchanged (station voice,
            // never LLM-authored).
            // Assert.Equal(SegmentKind.StationId, capturedRequest.Kind);
            Assert.Fail("pending T232");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioDefaultsChangeNothing
    {
        [Fact(Skip = "Pending T230 — see docs/PLAN.md")]
        public void ClockAnchoringOffEnqueuesNothingEver()
        {
            // Default false ⇒ the producer never enqueues; StationIdEveryNUnits cadence
            // remains the only ident source — byte-identical sound.
            // Assert.Equal(0, queue.PendingCount(SpeechDeferralKind.StationId));
            Assert.Fail("pending T230");
        }
    }
}
