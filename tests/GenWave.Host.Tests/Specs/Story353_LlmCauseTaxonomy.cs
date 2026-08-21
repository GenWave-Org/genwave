// STORY-353 — A red LLM tile names its cause (SPEC F139 · PLAN T330/T334/T335)
//
// BDD specification — xUnit. PENDING until built (see the Tts Story350 header note).
// The admin-ui tile half rides admin-ui/__specs__/health-tile-llm-cause.spec.tsx.
//
// gh-#365's acceptance is the dev-station case verbatim: a tile that flaps red every
// 1–2 hours on an external ollama (gemma-class on a 16GB 4090 laptop) explains itself
// from the admin UI — no SSH, no Loki, no darts at Llm settings.

namespace GenWave.Host.Tests.Specs;

public static class FeatureLlmCauseTaxonomy
{
    public static class ScenarioOutcomesAreTyped
    {
        [Fact(Skip = "pending T330 — LlmCallOutcome does not exist yet")]
        public static void A_successful_call_records_Success() =>
            Assert.Fail("pending T330: the F73 ring entry carries exactly one cause");

        [Fact(Skip = "pending T330")]
        public static void A_timed_out_call_records_Timeout() =>
            Assert.Fail("pending T330: a Llm:TimeoutSeconds breach records Timeout, never a generic failure");

        [Fact(Skip = "pending T330")]
        public static void An_over_length_call_records_OverLength() =>
            Assert.Fail("pending T330: a MaxCopyChars rejection records OverLength (the gh-#277 family gains a name)");

        [Fact(Skip = "pending T330")]
        public static void A_window_cancelled_stock_call_records_CanceledByWindow() =>
            Assert.Fail("pending T330: a crosstalk mid-flight abandon records CanceledByWindow");
    }

    public static class ScenarioCountersRoll
    {
        [Fact(Skip = "pending T330")]
        public static void Counts_group_per_cause_model_and_kind() =>
            Assert.Fail("pending T330: the 24h counters key on (cause, model, segment kind)");

        [Fact(Skip = "pending T330")]
        public static void Entries_older_than_24h_stop_counting() =>
            Assert.Fail("pending T330: the rolling window forgets (TimeProvider-driven, testable)");
    }

    public static class ScenarioTheSurfaceServesTheTaxonomy
    {
        // The deployed entry point: /api/llm-calls through WebApplicationFactory<Program>.
        [Fact(Skip = "pending T334 — surface not extended yet")]
        public static void Each_call_row_carries_its_cause() =>
            Assert.Fail("pending T334: a real request through the production pipeline shows the cause per call");

        [Fact(Skip = "pending T334")]
        public static void The_counter_summary_rides_the_response() =>
            Assert.Fail("pending T334: the 24h by-cause summary is served alongside the ring");
    }

    public static class SadPathDiscipline
    {
        [Fact(Skip = "pending T330")]
        public static void Nothing_survives_a_restart() =>
            Assert.Fail("pending T330: fresh ring, fresh counters — F73.3 stands");

        [Fact(Skip = "pending T331")]
        public static void A_truth_gate_rejection_is_its_own_cause() =>
            Assert.Fail("pending T331: a F138 gate failure records TruthGateReject, distinct from every other cause");
    }
}
