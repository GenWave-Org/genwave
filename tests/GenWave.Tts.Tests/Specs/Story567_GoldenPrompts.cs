// STORY-567 — The shadow week (SPEC F220 · PLAN F220)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureGoldenPrompts
{
    // AC6 — the prompt fixture set captured before any shaper moved
    public sealed class ScenarioTodaysPromptsAgainstTheCapturedOnes
    {
        [Fact(Skip = "pending: T787")]
        public void EachDjLineSystemPromptMatches() { }

        [Fact(Skip = "pending: T787")]
        public void EachDjLineUserPromptMatches() { }

        [Fact(Skip = "pending: T787")]
        public void TheChatPromptMatches() { }

        [Fact(Skip = "pending: T787")]
        public void TheAdPromptMatches() { }
    }
}
