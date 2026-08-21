// STORY-351 — Patter can't lie about the clock (SPEC F138.3, F138.5 · PLAN T329/T332)
//
// BDD specification — xUnit. PENDING until built (see Story350's header note).
//
// The gh-#438 aired exhibit is the pinned regression: "We're diving into a neon dusk on
// this Saturday morning... Tonight we flip..." aired at Sunday 11:50 AM while the F117
// clock line named the correct instant in the prompt. The model isn't missing the
// information; it ignores it — so the check is mechanical, on EVERY patter kind.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureClockClaimsGate
{
    public static class ScenarioConsistentClaimsPass
    {
        // Given a clock line of Sunday 11:50 AM
        [Fact(Skip = "pending T329 — clock predicate does not exist yet")]
        public static void A_matching_weekday_claim_passes() =>
            Assert.Fail("pending T329: copy naming Sunday passes");

        [Fact(Skip = "pending T329")]
        public static void A_matching_daypart_claim_passes() =>
            Assert.Fail("pending T329: 'this morning' at 11:50 AM passes");
    }

    public static class ScenarioClockLiesAreCaught
    {
        [Fact(Skip = "pending T332 — check not wired across patter kinds yet")]
        public static void A_wrong_weekday_in_a_lead_in_is_rejected() =>
            Assert.Fail("pending T332: 'this Saturday morning' against a Sunday clock line rejects with the weekday violation");

        [Fact(Skip = "pending T332")]
        public static void A_wrong_daypart_is_rejected() =>
            Assert.Fail("pending T332: 'Tonight' at 11:50 AM rejects with the daypart violation");

        [Fact(Skip = "pending T332")]
        public static void A_back_announce_is_checked_like_a_lead_in() =>
            Assert.Fail("pending T332: the gate applies to every LLM patter kind, not the context lane alone");
    }

    public static class SadPathExemptionsHold
    {
        [Fact(Skip = "pending T329")]
        public static void A_track_title_naming_a_day_is_exempt() =>
            Assert.Fail("pending T329: 'Saturday Night Fever' on a Sunday does not trip the gate");

        [Fact(Skip = "pending T329")]
        public static void Copy_with_no_clock_claims_records_zero_rejections() =>
            Assert.Fail("pending T329: claim-free copy passes with no violations recorded");
    }
}
