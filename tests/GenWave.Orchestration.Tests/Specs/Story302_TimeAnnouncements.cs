// STORY-302 — The time announcement (F110.3)

namespace GenWave.Orchestration.Tests.Specs;

public static class FeatureTimeAnnouncements
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheEnumGainsItsFirstProducer
    {
        [Fact(Skip = "Pending T230 — see docs/PLAN.md")]
        public void TimeAnnouncementsOnEnqueuesATimeDateDeferral()
        {
            // Station:Imaging:TimeAnnouncements=true ⇒ the producer enqueues a
            // future-dated TimeDate deferral for the top of the hour.
            // Assert.Equal(1, queue.PendingCount(SpeechDeferralKind.TimeDate));
            Assert.Fail("pending T230");
        }

        [Fact(Skip = "Pending T232 — see docs/PLAN.md")]
        public void TheCopyIsTemplatedPerHourAndStationVoiced()
        {
            // Drain renders templated per-hour copy ("It's two o'clock…"), station voice,
            // never LLM-authored (IsLlmAuthored excludes TimeDate).
            // Assert.Equal(stationVoice, capturedRequest.Voice);
            Assert.Fail("pending T232");
        }

        [Fact(Skip = "Pending T232 — see docs/PLAN.md")]
        public void TheSameHourIsACacheHit()
        {
            // Second render of the same hour's text ⇒ forever-cache hit (templated copy,
            // FreshPerAiring=false ⇒ non-blurb path).
            // Assert.Equal(1, synthesizer.SynthesisCount);
            Assert.Fail("pending T232");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioAFailedRenderIsSilent
    {
        [Fact(Skip = "Pending T232 — see docs/PLAN.md")]
        public void ANullRenderAirsNothingAndMusicContinues()
        {
            // TTS failure on the TimeDate render ⇒ no announcement in the buffer, music
            // unit intact, one WARN (F74.1 stands).
            // Assert.DoesNotContain(buffer, i => i.SegmentKind == SegmentKind.TimeDate);
            Assert.Fail("pending T232");
        }
    }
}
