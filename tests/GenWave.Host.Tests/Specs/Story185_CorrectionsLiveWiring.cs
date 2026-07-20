// STORY-185 — Corrections live from settings through one call site
//
// BDD specification — xUnit (SPEC F68.1, F68.5, F68.8). Pending scaffold; /build-loop
// (PLAN T29) implements and removes Skip.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCorrectionsLiveWiring
{
    private const string Pending = "pending — PLAN T29 (/build-loop)";

    public static class ScenarioLiveApply
    {
        [Fact(Skip = Pending)]
        public static void Saved_correction_applies_to_the_next_render_without_restart()
        {
            // Given the host running and a correction saved via PUT /api/settings
            // When  the next booth-bound string is rendered
            // Then  the correction is applied without restart (F68.5)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioSingleChokepoint
    {
        [Fact(Skip = Pending)]
        public static void Exactly_one_call_site_invokes_normalize()
        {
            // Given the production render path
            // When  callers of the TTS renderer are audited (source/route enumeration)
            // Then  exactly one call site invokes Normalize and no generator performs
            //       its own cleanup (F68.1)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioDemoSeed
    {
        [Fact(Skip = Pending)]
        public static void MacLeod_rule_is_seed_data_not_code_default()
        {
            // Given the demo station settings seed
            // When  Tts:Corrections is read
            // Then  it contains the MacLeod rule as data, and no such default exists
            //       in code (F68.8)
            Assert.Fail(Pending);
        }
    }
}
