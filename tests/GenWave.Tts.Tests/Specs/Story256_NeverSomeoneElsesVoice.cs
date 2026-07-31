// STORY-256 — Never someone else's voice (gh-#276, audible half)
//
// SPEC F99.1, F99.5. When kokoro dies mid-show the fallback currently speaks the DJ's line in
// a different voice — a DJ whose voice changes mid-show is the single most inhuman artifact
// the station ships, and gh-#276's OOM makes it a real duty cycle rather than a rare path.
//
// The ruling: RIGHT VOICE OR NO SPEECH. This overturns F70's standing "a wrong voice beats
// silence".
//
// ⚠️ Never-silent (F6.3) is untouched and always was about the STREAM, not the mic. Music
// continues uninterrupted; only the break is dropped. The specs below pin both halves,
// because a change that accidentally stopped the music would be a far worse bug than the one
// being fixed.
//
// Serving cached evergreen audio in the DJ's real voice was considered at /design and
// REJECTED: it needs a notion of which segments are re-airable, and "the DJ repeated
// themselves" is its own inhuman artifact.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureNeverSomeoneElsesVoice
{
    public static class ScenarioTheBreakIsDropped
    {
        [Fact(Skip = "Pending T147 — see docs/PLAN.md")]
        public static void No_segment_is_produced_when_the_dj_voice_cannot_be_rendered()
        {
            // Assert.Null(await source.RenderAsync(request, ct));
            Assert.Fail("pending T147");
        }

        [Fact(Skip = "Pending T147 — see docs/PLAN.md")]
        public static void No_other_voice_is_ever_asked_to_speak_that_line()
        {
            // The substitute engine must not be CALLED, not merely have its output discarded.
            Assert.Fail("pending T147");
        }
    }

    public static class ScenarioTheStreamIsUntouched
    {
        [Fact(Skip = "Pending T147 — see docs/PLAN.md")]
        public static void Music_continues_when_a_break_is_dropped()
        {
            // The feeder still yields a music item — never-silent governs the stream.
            Assert.Fail("pending T147");
        }

        [Fact(Skip = "Pending T147 — see docs/PLAN.md")]
        public static void The_drop_does_not_fault_the_playout_loop()
        {
            // A voice-integrity drop is a decision, not an exception escaping to the feeder.
            Assert.Fail("pending T147");
        }
    }

    public static class ScenarioTheDropIsLegible
    {
        [Fact(Skip = "Pending T147 — see docs/PLAN.md")]
        public static void The_drop_logs_at_information()
        {
            Assert.Fail("pending T147");
        }

        [Fact(Skip = "Pending T147 — see docs/PLAN.md")]
        public static void The_line_names_the_persona()
        {
            Assert.Fail("pending T147");
        }

        [Fact(Skip = "Pending T147 — see docs/PLAN.md")]
        public static void The_line_names_the_cause()
        {
            // "the engine is down" must be distinguishable from "nothing to say" (F99.5).
            Assert.Fail("pending T147");
        }
    }

    // -------------------------------------------------------------------------------------
    // ENTRY POINT — the operator-facing half of F99.5.
    // -------------------------------------------------------------------------------------
    public static class ScenarioTheHealthSurfaceShowsIt
    {
        [Fact(Skip = "Pending T149 — see docs/PLAN.md")]
        public static void The_degraded_voice_state_is_visible_on_the_health_endpoint()
        {
            // Drive the real endpoint; an operator with no log stack must still be able to
            // tell why the DJ is quiet.
            Assert.Fail("pending T149");
        }

        [Fact(Skip = "Pending T149 — see docs/PLAN.md")]
        public static void A_healthy_station_reports_no_degraded_voice_state()
        {
            Assert.Fail("pending T149");
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioRecovery
    {
        [Fact(Skip = "Pending T147 — see docs/PLAN.md")]
        public static void The_next_break_airs_in_the_dj_voice_once_the_engine_returns()
        {
            // No operator action, no restart — recovery is automatic (F99.1).
            Assert.Fail("pending T147");
        }
    }
}
