// STORY-190 — Local TTS fallback engine
//
// BDD specification — xUnit (SPEC F70.1, F70.4). Pending scaffold; /build-loop (PLAN T34)
// implements and removes Skip. The kill-Kokoro compose acceptance is T34's wire criterion,
// exercised against the running stack, not unit-specced here.

using Xunit;

namespace GenWave.Tts.Tests.Specs;

public static class FeatureTtsFallbackEngine
{
    private const string Pending = "pending — PLAN T34 (/build-loop)";

    public static class ScenarioFallthroughOnUnhealthy
    {
        [Fact(Skip = Pending)]
        public static void Unhealthy_primary_verdict_routes_render_to_fallback()
        {
            // Given a cached unhealthy verdict for the primary engine
            // When  a segment renders
            // Then  the fallback engine renders it and the segment airs (F70.1)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioFallthroughOnFailure
    {
        [Fact(Skip = Pending)]
        public static void Primary_render_throw_is_retried_on_fallback()
        {
            // Given a healthy verdict but a render call that throws
            // When  the render is retried on the fallback
            // Then  the segment still airs (F70.1)
            Assert.Fail(Pending);
        }
    }

    public static class ScenarioSamePipeline
    {
        [Fact(Skip = Pending)]
        public static void Fallback_renders_pass_normalize_measure_cache_identically()
        {
            // Given a fallback-rendered segment
            // When  its processing is inspected
            // Then  it passed the same Normalize → loudness-measure → cache path as
            //       primary renders (F70.4)
            Assert.Fail(Pending);
        }
    }

    public static class SadPathBothEnginesDown
    {
        [Fact(Skip = Pending)]
        public static void Segment_skips_loudly_and_music_continues()
        {
            // Given both primary and fallback unavailable
            // When  a segment render is attempted
            // Then  the segment is skipped with a loud log and music playout continues
            //       uninterrupted (F70.1)
            Assert.Fail(Pending);
        }
    }
}
