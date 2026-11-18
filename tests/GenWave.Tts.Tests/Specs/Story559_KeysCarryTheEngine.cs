// STORY-559 — Old audio is replaced quietly, the cache keys (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Tts.Tests.Specs;

public static class FeatureKeysCarryTheEngine
{
    // AC1 — same text and voice, engine changed
    public sealed class ScenarioTheEngineChanges
    {
        [Fact(Skip = "pending: T762")]
        public void TheSegmentHashChanges() { }

        [Fact(Skip = "pending: T762")]
        public void TheRenderKeyChanges() { }

        [Fact(Skip = "pending: T762")]
        public void TheAdPreviewKeyChanges() { }

        [Fact(Skip = "pending: T762")]
        public void TheAdRenderMarkerChanges() { }
    }

    // same text and engine, voice changed
    public sealed class ScenarioTheVoiceChanges
    {
        [Fact(Skip = "pending: T762")]
        public void TheSegmentHashChangesWithTheVoice() { }
    }
}
