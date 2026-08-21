// STORY-352 — Banter stays fictional, never false (SPEC F138.6, F127.4 as amended · PLAN T333)
//
// BDD specification — xUnit. PENDING until built (see Story350's header note).
//
// The ruling (Dean, 2026-08-20): real-world verifiables are forbidden — frequency/call-sign
// shapes, dates, weather words, clock lies — and mechanically enforced through F127.4's
// fail-closed discard. Fictional lore (recurring characters, running gags, station
// mythology) is explicitly ALLOWED: invented characters are good radio. Real geography is
// prompt-clause-only — a checker cannot know real places from invented ones (F138.6 says
// so honestly instead of pretending).

namespace GenWave.Tts.Tests.Specs;

public static class FeatureBanterTruth
{
    public static class ScenarioVerifiablesDiscardTheScript
    {
        [Fact(Skip = "pending T333 — F127.4 truth discard reasons not built yet")]
        public static void A_frequency_shape_discards_with_the_verifiable_reason() =>
            Assert.Fail("pending T333: a line claiming '98.7 FM' discards the exchange (fail-closed skip, no salvage)");

        [Fact(Skip = "pending T333")]
        public static void A_call_sign_shape_discards() =>
            Assert.Fail("pending T333: a K/W-prefixed call sign claim discards the exchange");

        [Fact(Skip = "pending T333")]
        public static void A_weather_claim_discards() =>
            Assert.Fail("pending T333: a condition-word claim discards the exchange");

        [Fact(Skip = "pending T333")]
        public static void A_clock_lie_discards() =>
            Assert.Fail("pending T333: a wrong-weekday line against the clock context discards with the clock reason");
    }

    public static class ScenarioFictionalLorePasses
    {
        [Fact(Skip = "pending T333")]
        public static void An_invented_recurring_character_passes() =>
            Assert.Fail("pending T333: no lore-shaped rejection exists — invented characters validate clean");

        [Fact(Skip = "pending T333")]
        public static void The_narrow_clause_rides_the_banter_prompt() =>
            Assert.Fail("pending T333: the prompt forbids real-world verifiables and explicitly allows fictional lore");
    }

    public static class ScenarioTheRatifiedTarget
    {
        [Fact(Skip = "pending T333")]
        public static void The_duration_default_is_fifty_seconds() =>
            Assert.Fail("pending T333: with no override, Crosstalk:DurationTargetSeconds reads 50 (F127.4 as amended)");
    }
}
