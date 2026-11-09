// STORY-533 AC6 — old cards behave as today (SPEC F218.5, F71.3 · PLAN T683)
//
// LlmPromptBuilder.SampleQuirks draws from Random.Shared (no seed seam), so "exactly as before" is
// pinned the way Story193 pins it: across many builds, the count bound and the set. See
// tests/GenWave.Core.Tests/Specs/Story533_AOnceAShiftQuirkFiresOnceAShift_Card.cs for the map.

using GenWave.Core.Domain;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureAOnceAShiftQuirkFiresOnceAShiftCardSampling
{
    public sealed class ScenarioOldCardsBehaveAsToday
    {
        static readonly string[] AllQuirks = ["QuirkAlpha", "QuirkBravo", "QuirkCharlie", "QuirkDelta", "QuirkEcho"];

        readonly PersonaCard card;
        readonly IReadOnlyList<int> shownCounts;

        // Given a card without QuirkRules
        // When  quirks are sampled across 300 prompt builds
        public ScenarioOldCardsBehaveAsToday()
        {
            card = new PersonaCard(
                PersonaCard.CurrentSchemaVersion, "DJ Nova", "", "Soul line.", AllQuirks,
                new VoiceSpec("kokoro", "af_heart", 1.0, "en"), 0.0, [], []);
            shownCounts = Enumerable.Range(0, 300)
                .Select(_ => LlmPromptBuilder.BuildPersonaSection(persona: null, card) ?? "")
                .Select(section => AllQuirks.Count(quirk => section.Contains(quirk, StringComparison.Ordinal)))
                .ToList();
        }

        [Fact]
        public void TheCardCarriesNoRules() => Assert.Null(card.QuirkRules);

        [Fact]
        public void EveryBuildShowsTwoOrThreeQuirks() =>
            Assert.All(shownCounts, count => Assert.InRange(count, 2, 3));

        [Fact]
        public void SomeBuildShowsTwoQuirks() => Assert.Contains(2, shownCounts);

        [Fact]
        public void SomeBuildShowsThreeQuirks() => Assert.Contains(3, shownCounts);
    }
}
