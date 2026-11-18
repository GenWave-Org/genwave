// STORY-554 — Engines are a list, not a hard-coded pair (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureEnginesAreAList
{
    // AC1, AC2 — Tts:Engines:chatterbox with budget 120; another engine with none
    public sealed class ScenarioTheRegistry
    {
        [Fact(Skip = "pending: T745")]
        public void TheEngineIsReturnedByName() { }

        [Fact(Skip = "pending: T745")]
        public void ItsBudgetIs120Seconds() { }

        [Fact(Skip = "pending: T745")]
        public void AnEngineWithNoBudgetGets30Seconds() { }

        [Fact(Skip = "pending: T745")]
        public void KokoroIsSeededFromTtsEndpointWhenNoEntryExists() { }
    }

    // AC3, AC4 — one line rendered per profile against a recording handler
    public sealed class ScenarioTheRequestFollowsTheProfile
    {
        [Fact(Skip = "pending: T746")]
        public void TheKokoroBodyCarriesSpeed() { }

        [Fact(Skip = "pending: T746")]
        public void TheKokoroBodyCarriesKokoroMarkup() { }

        [Fact(Skip = "pending: T746")]
        public void TheChatterboxBodyHasNoSpeed() { }

        [Fact(Skip = "pending: T746")]
        public void TheChatterboxBodyHasNoPauseMarkup() { }

        [Fact(Skip = "pending: T746")]
        public void TheRequestGoesToTheEnginesSpeechPath() { }
    }

    // AC5 — the engine lists two voices
    public sealed class ScenarioTheVoicesList
    {
        [Fact(Skip = "pending: T746")]
        public void BothVoicesAreReturned() { }
    }

    // AC6 — a render that takes 60 s (fake clock)
    public sealed class ScenarioTheBudgetIsPerEngine
    {
        [Fact(Skip = "pending: T747")]
        public void ItPassesOnChatterbox() { }

        [Fact(Skip = "pending: T747")]
        public void ItTimesOutOnKokoro() { }
    }

    // Sad path
    // AC7, AC8 — unknown profile; blank endpoint
    public sealed class ScenarioABadEngineEntry
    {
        [Fact(Skip = "pending: T745")]
        public void AnUnknownProfileFailsBootNamingTheKey() { }

        [Fact(Skip = "pending: T745")]
        public void ABlankEndpointFailsBootNamingTheKey() { }
    }
}
