// STORY-556 — Every speaker keeps a voice per engine, settings and render half (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAVoicePerEngine
{
    // AC3 — Station:Voice, AnnouncerVoice, CastVoices
    public sealed class ScenarioSettingsTakeBothShapes
    {
        [Fact(Skip = "pending: T752")]
        public void ABareIdValidates() { }

        [Fact(Skip = "pending: T752")]
        public void AnEngineMapValidates() { }

        [Fact(Skip = "pending: T752")]
        public void ACastListPerEngineValidates() { }

        [Fact(Skip = "pending: T752")]
        public void ABareIdResolvesOnKokoro() { }
    }

    // AC4 — voices for both engines; kokoro → chatterbox → kokoro through the preview endpoint
    public sealed class ScenarioThereAndBack
    {
        [Fact(Skip = "pending: T753")]
        public void TheChatterboxRenderUsesTheChatterboxVoice() { }

        [Fact(Skip = "pending: T753")]
        public void TheLastRenderUsesTheFirstVoice() { }
    }

    // Sad path
    // AC7
    public sealed class ScenarioAnUnknownEngineKey
    {
        [Fact(Skip = "pending: T752")]
        public void TheSaveReturns400() { }
    }

    // Sad path
    // AC8 — a DJ whose map lacks the active engine
    public sealed class ScenarioNoVoiceForThisEngine
    {
        [Fact(Skip = "pending: T753")]
        public void TheTalkIsDropped() { }

        [Fact(Skip = "pending: T753")]
        public void AVoiceMissingRowNamesTheDj() { }

        [Fact(Skip = "pending: T753")]
        public void NoRenderRequestIsSent() { }
    }
}
