// STORY-555 — One setting switches the engine (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureOneSettingSwitchesTheEngine
{
    // AC1, AC3 — chatterbox in the registry; through the settings endpoint
    public sealed class ScenarioSavingTheSetting
    {
        [Fact(Skip = "pending: T748")]
        public void SettingChatterboxReturns200() { }

        [Fact(Skip = "pending: T748")]
        public void AFreshInstallReadsKokoro() { }

        [Fact(Skip = "pending: T748")]
        public void TheEnginesEndpointListsTheRegistry() { }
    }

    // AC2 — the engine changed, then a preview through the test host, no restart
    public sealed class ScenarioTheSwitchIsLive
    {
        [Fact(Skip = "pending: T749")]
        public void TheNextRenderReachesTheNewEnginesEndpoint() { }

        [Fact(Skip = "pending: T749")]
        public void TheOldEngineGetsNoRequest() { }
    }

    // Sad path
    // AC6
    public sealed class ScenarioAnUnknownEngine
    {
        [Fact(Skip = "pending: T748")]
        public void SettingNopeReturns400() { }
    }
}
