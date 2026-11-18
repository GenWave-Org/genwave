// STORY-562 — Chatterbox ships with the station (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureChatterboxShipsWithTheStation
{
    // AC1 — compose.yaml read as text
    public sealed class ScenarioTheComposeService
    {
        [Fact(Skip = "pending: T771")]
        public void TheCpusReadTheVoiceCpusVariableDefault64() { }

        [Fact(Skip = "pending: T771")]
        public void TheServiceIsInTheChatterboxProfile() { }

        [Fact(Skip = "pending: T771")]
        public void TheClipsVolumeIsReadOnlyInTheEngine() { }

        [Fact(Skip = "pending: T771")]
        public void TheApiMountsTheClipsVolume() { }
    }

    // AC2 — ./launch.sh --chatterbox dry-run
    public sealed class ScenarioLaunch
    {
        [Fact(Skip = "pending: T771")]
        public void TheServiceListHasChatterbox() { }

        [Fact(Skip = "pending: T771")]
        public void TheServiceListHasNoKokoro() { }
    }

    // Sad path
    // AC4 — ./launch.sh with no flag
    public sealed class ScenarioOldPresets
    {
        [Fact(Skip = "pending: T771")]
        public void TheServiceListIsUnchanged() { }
    }
}
