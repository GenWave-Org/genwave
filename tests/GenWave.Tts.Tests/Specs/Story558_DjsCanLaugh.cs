// STORY-558 — DJs can laugh (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureDjsCanLaugh
{
    // AC1 — chatterbox profile, cap 1
    public sealed class ScenarioThePromptOffersTags
    {
        [Fact(Skip = "pending: T760")]
        public void TheDjPromptListsTheNineEventTags() { }

        [Fact(Skip = "pending: T760")]
        public void TheDjPromptStatesTheCap() { }

        [Fact(Skip = "pending: T760")]
        public void TheTwoDjPromptListsTheTags() { }
    }

    // AC2 — "[laugh] hi [sigh] [dance]", cap 1
    public sealed class ScenarioTagsKeptUpToTheCap
    {
        [Fact(Skip = "pending: T759")]
        public void TheTextIsLaughHi() { }
    }

    // AC3 — "[clear throat] hello"
    public sealed class ScenarioTheNormaliserKeepsThem
    {
        [Fact(Skip = "pending: T759")]
        public void TheTagIsByteIntact() { }
    }

    // AC4 — a line that differs from a recent one only by a tag
    public sealed class ScenarioChecksIgnoreThem
    {
        [Fact(Skip = "pending: T759")]
        public void TheRepetitionCheckSeesTheSameWords() { }

        [Fact(Skip = "pending: T759")]
        public void TheTruthGateReadsTagFreeCopy() { }
    }

    // Sad path
    // AC5, AC6
    public sealed class ScenarioTagsOff
    {
        [Fact(Skip = "pending: T760")]
        public void WithCapZeroTheDjPromptIsByteEqualToTodays() { }

        [Fact(Skip = "pending: T760")]
        public void OnKokoroThePromptIsByteEqualToTodays() { }

        [Fact(Skip = "pending: T759")]
        public void OnKokoroTheTextIsHi() { }
    }
}
