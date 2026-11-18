// STORY-556 — Every speaker keeps a voice per engine, the card half (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Core.Tests.Specs;

public static class FeatureAVoicePerEngineOnTheCard
{
    // AC1 — a card with only Voice (kokoro, af_heart)
    public sealed class ScenarioAnOldCard
    {
        [Fact(Skip = "pending: T751")]
        public void ItsMapIsKokoroToAfHeart() { }
    }

    // AC2 — a card with Voices, imported then exported
    public sealed class ScenarioRoundTrip
    {
        [Fact(Skip = "pending: T751")]
        public void TheBytesAreEqual() { }

        [Fact(Skip = "pending: T751")]
        public void EditingTheNameKeepsTheMap() { }

        [Fact(Skip = "pending: T751")]
        public void VoiceAndVoicesStayInLockstep() { }
    }
}
