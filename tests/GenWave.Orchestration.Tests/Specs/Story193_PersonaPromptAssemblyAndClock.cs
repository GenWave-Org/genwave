// STORY-193 — Prompt assembly on personas with a real clock (gh-#13)
//
// BDD specification — xUnit (SPEC F71.3, F71.7, F71.8). Pending scaffold; /build-loop
// (PLAN T37) implements and removes Skip.

using Xunit;

namespace GenWave.Orchestration.Tests.Specs;

public static class FeaturePersonaPromptAssembly
{
    private const string Pending = "pending — PLAN T37 (/build-loop)";

    public static class ScenarioQuirkSampling
    {
        [Fact(Skip = Pending)]
        public static void Each_prompt_contains_two_to_three_quirks_and_never_all()
        {
            // Given a persona with five quirks
            // When  many prompts are assembled
            // Then  each prompt contains 2–3 quirks and never all five (F71.3)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioTheDjsClock
    {
        [Fact(Skip = Pending)]
        public static void Every_prompt_contains_the_injected_station_local_date_weekday_and_time()
        {
            // Given an injected station-local clock
            // When  any copywriter prompt is assembled
            // Then  it contains the injected current date, weekday, and time (F71.8)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioCorrectionPrecedence
    {
        [Fact(Skip = Pending)]
        public static void Station_rule_wins_over_card_rule_with_same_from()
        {
            // Given a station correction and a card correction with the same From
            // When  the merged set is built
            // Then  the station rule wins (F71.7)
            Assert.Fail(Pending);
        }
    }
}
