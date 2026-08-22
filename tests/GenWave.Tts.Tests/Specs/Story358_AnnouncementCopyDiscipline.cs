// STORY-358 — The DJ says it: two fidelities, one fallback (SPEC F144.3/.4 · PLAN T342)
using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureAnnouncementCopyDiscipline
{
    public sealed class ScenarioFlavoredCopyCarriesTheCore
    {
        [Fact(Skip = "pending T342 (STORY-358 AC3)")]
        public void TheAiredCopyContainsTheCaseFoldedMessageCore() { }

        [Fact(Skip = "pending T342 (STORY-358 AC3)")]
        public void TheTruthGateRaisesNoFabricationViolationForTheMessageItself() { }

        [Fact(Skip = "pending T342 (STORY-358 AC3)")]
        public void CopyThatDropsTheCoreIsAGateRejectAndRidesTheReaskLadder() { }
    }

    public sealed class ScenarioTheFallbackLaw
    {
        [Fact(Skip = "pending T342 (STORY-358 AC4)")]
        public void AnExhaustedReaskLadderDegradesToTheVerbatimRead() { }

        [Fact(Skip = "pending T342 (STORY-358 AC4)")]
        public void AnUnreachableLlmDegradesToTheVerbatimRead() { }

        [Fact(Skip = "pending T342 (STORY-358 AC4)")]
        public void ABlownRenderBudgetDegradesToTheVerbatimRead() { }

        [Fact(Skip = "pending T342 (STORY-358 AC4)")]
        public void TheAnnouncementAirsInEveryDegradedCase() { }
    }
}
