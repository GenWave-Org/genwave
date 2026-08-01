// STORY-255 — DJs speak at their own pace (SPEC F98)
//
// `VoiceSpec.Pace` — "speaking-rate multiplier; 1.0 is engine default" — is in the persona
// card, in the export/import contract, and in the community catalog's published JSON schema.
// Nothing reads it. kokoro-fastapi's /v1/audio/speech takes `speed`; the adapter sends
// { input, voice, response_format } and never has.
//
// The consequence is measurable: 11 of the 12 first-party catalog personas carry a deliberate
// non-default pace (Rusty Strings 0.85 slow and weathered, Maxxie Volt 1.15 fast and
// energetic) and all twelve currently speak at an identical rate.
//
// ⚠️ F98.3 — this knowingly departs from the house "sounds identical on upgrade" discipline.
// The new sound is the one the persona's author specified; the uniformity IS the defect.
//
// The cache-key half is a correctness requirement, not a nicety: audio rendered at 0.85 is not
// audio rendered at 1.0, and pace is invisible in the text, so a key that ignores it serves a
// persona's old rate forever after an edit.

namespace GenWave.Tts.Tests.Specs;

using GenWave.Core.Domain;

public static class FeatureDjsSpeakAtTheirOwnPace
{
    public static class ScenarioPaceReachesTheEngine
    {
        [Fact(Skip = "Pending T140 — see docs/PLAN.md")]
        public static void The_kokoro_request_body_carries_speed()
        {
            // var body = KokoroRequest.Build(text, voice, pace: 0.85, format: "wav");
            // Assert.Equal(0.85, body.Speed);
            Assert.Fail("pending T140");
        }

        [Fact(Skip = "Pending T140 — see docs/PLAN.md")]
        public static void The_default_pace_is_sent_as_the_engine_default()
        {
            Assert.Fail("pending T140");
        }

        [Fact]
        public static void The_render_context_carries_pace_from_the_persona()
        {
            // TtsRenderContext widened per F70.3's precedent — a default interface member, so
            // every existing engine client and test fake compiles and behaves unchanged.
            var context = new TtsRenderContext("Coming up next", "af_heart", SegmentKind.LeadIn)
                with { Pace = 0.85 };

            Assert.Equal(0.85, context.Pace);
        }
    }

    public static class ScenarioBothCacheKeysHonourPace
    {
        [Fact(Skip = "Pending T140 — see docs/PLAN.md")]
        public static void The_segment_cache_separates_two_paces()
        {
            // Same copy, same voice, pace 0.85 vs 1.0 → two distinct segment-cache entries.
            Assert.Fail("pending T140");
        }

        [Fact(Skip = "Pending T140 — see docs/PLAN.md")]
        public static void The_engine_file_cache_separates_two_paces()
        {
            // The adapter's own (text|voice) hash gains pace; without it the file cache
            // collides across rates even when the segment cache does not.
            Assert.Fail("pending T140");
        }

        [Fact(Skip = "Pending T140 — see docs/PLAN.md")]
        public static void An_unchanged_pace_still_hits_the_cache()
        {
            // Adding a key term must not defeat caching for the overwhelmingly common case.
            Assert.Fail("pending T140");
        }
    }

    // -------------------------------------------------------------------------------------
    // ENTRY POINT — the audible claim. A hash test proves keys differ; only a real render
    // proves a persona actually sounds slower.
    // -------------------------------------------------------------------------------------
    public static class ScenarioARealRenderChangesRate
    {
        [Fact(Skip = "Pending T141 — see docs/PLAN.md")]
        public static void A_slow_persona_renders_longer_audio_than_a_fast_one()
        {
            // Same copy through the production graph at 0.85 and 1.15; compare measured
            // durations, not request bodies.
            Assert.Fail("pending T141");
        }

        [Fact(Skip = "Pending T141 — see docs/PLAN.md")]
        public static void Editing_a_personas_pace_produces_fresh_audio()
        {
            // The regression this guards: serving the cached 1.0 clip after an edit to 1.15.
            Assert.Fail("pending T141");
        }
    }

    // -------------------------------------------------------------------------------------
    // SAD PATH
    // -------------------------------------------------------------------------------------
    public static class ScenarioEnginesWithoutRateControl
    {
        [Fact(Skip = "Pending T140 — see docs/PLAN.md")]
        public static void A_rate_less_engine_renders_successfully_anyway()
        {
            // F98.1 — pace is simply not applied; it is never a render failure.
            Assert.Fail("pending T140");
        }
    }
}
