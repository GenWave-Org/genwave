// STORY-191 — Per-kind TTS engine override
//
// BDD specification — xUnit (SPEC F70.3). Pending scaffold; /build-loop (PLAN T35)
// implements and removes Skip.

using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeaturePerKindEngineOverride
{
    private const string Pending = "pending — PLAN T35 (/build-loop)";

    public static class ScenarioMappedKind
    {
        [Fact(Skip = Pending)]
        public static void Mapped_kind_renders_on_the_mapped_engine()
        {
            // Given a map entry ident → fallback-engine
            // When  an ident renders
            // Then  the mapped engine renders it (F70.3)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioFallthrough
    {
        [Fact(Skip = Pending)]
        public static void Unmapped_kind_renders_on_the_default_engine()
        {
            // Given a kind with no map entry
            // When  it renders
            // Then  the default engine renders it (F70.3)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioEmptyDefault
    {
        [Fact(Skip = Pending)]
        public static void No_map_configured_is_identical_to_pre_feature_routing()
        {
            // Given no map configured
            // When  any kind renders
            // Then  behavior is identical to pre-feature routing (F70.3)
            Assert.Fail(Pending);
        }
    }
}
