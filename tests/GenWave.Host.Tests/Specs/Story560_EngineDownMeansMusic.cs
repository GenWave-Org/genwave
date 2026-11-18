// STORY-560 — Engine down means music (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineDownMeansMusic
{
    // AC1
    public sealed class ScenarioWarmUpThenGreen
    {
        [Fact(Skip = "pending: T766")]
        public void TheTileIsGreen() { }

        [Fact(Skip = "pending: T766")]
        public void TheTileNamesTheEngine() { }
    }

    // AC2, AC3 — a render past the engine's budget
    public sealed class ScenarioLateTalkIsLogged
    {
        [Fact(Skip = "pending: T767")]
        public void ATalkDroppedLateRowIsWritten() { }

        [Fact(Skip = "pending: T767")]
        public void TheStatusResponseCarriesTheSevenDayCount() { }

        [Fact(Skip = "pending: T767")]
        public void ARowEightDaysOldIsNotCounted() { }
    }

    // Sad path
    // AC4, AC5 — connections refused, empty fallback chain
    public sealed class ScenarioTheEngineIsDown
    {
        [Fact(Skip = "pending: T766")]
        public void TheTileIsRedWithTheReason() { }

        [Fact(Skip = "pending: T766")]
        public void NoOtherEngineRendersAnything() { }
    }
}
