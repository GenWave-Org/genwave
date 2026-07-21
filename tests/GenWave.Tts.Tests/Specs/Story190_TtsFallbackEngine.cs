// STORY-190 — Local TTS fallback engine
//
// BDD specification — xUnit (SPEC F70.1, F70.4). Implemented PLAN T34 (/build-loop). The
// kill-Kokoro compose acceptance is T34's wire criterion, exercised against the running stack,
// not unit-specced here.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureTtsFallbackEngine
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

    static TestOptionsMonitor<TtsFallbackOptions> ConfiguredFallbackOptions() =>
        new(new TtsFallbackOptions { Endpoint = "http://piper:5000", Voice = "en_US-lessac-medium" });

    static DependencyHealthVerdict UnhealthyKokoroVerdict() =>
        new(DependencyNames.Kokoro, Healthy: false, DateTimeOffset.UtcNow, "connect failure", ConsecutiveFailureCount: 3);

    static FallbackTtsSynthesizer BuildRouter(
        FakeTtsSynthesizer primary,
        FakeTtsSynthesizer fallback,
        FakeDependencyHealth health,
        TestOptionsMonitor<TtsFallbackOptions>? fallbackOptions = null,
        CapturingLogger<FallbackTtsSynthesizer>? logger = null) =>
        new(primary, fallback, health, fallbackOptions ?? ConfiguredFallbackOptions(),
            logger ?? new CapturingLogger<FallbackTtsSynthesizer>());

    static SegmentRequest StationIdRequest() =>
        new(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

    static TtsSegmentSource BuildSegmentSource(
        FallbackTtsSynthesizer router,
        FakeLoudnessAnalyzer analyzer,
        FakeCueAnalyzer cueAnalyzer,
        string cacheRoot,
        ILogger<TtsSegmentSource>? logger = null) =>
        new(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            router,
            analyzer,
            cueAnalyzer,
            NoCorrections.Provider(),
            NoCorrections.PersonaCache(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            logger ?? NullLogger<TtsSegmentSource>.Instance);

    // ------------------------------------------------------------------
    // HAPPY PATH
    // ------------------------------------------------------------------

    public static class ScenarioFallthroughOnUnhealthy
    {
        [Fact]
        public static async Task Unhealthy_primary_verdict_routes_render_to_fallback()
        {
            // Given a cached unhealthy verdict for the primary engine
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeTtsSynthesizer();
            var health = new FakeDependencyHealth();
            health.Set(UnhealthyKokoroVerdict());
            var router = BuildRouter(primary, fallback, health);

            // When a segment renders
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the fallback engine renders it and the segment airs (F70.1) — the primary is
            // never even attempted.
            Assert.NotNull(path);
            Assert.Equal(0, primary.CallCount);
            Assert.Equal(1, fallback.CallCount);
        }
    }

    public static class ScenarioFallthroughOnFailure
    {
        [Fact]
        public static async Task Primary_render_throw_is_retried_on_fallback()
        {
            // Given a healthy verdict (none recorded yet — the F70.2 "unknown" case reads the same
            // as healthy: try the primary) but a render call that throws
            var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
            var fallback = new FakeTtsSynthesizer();
            var health = new FakeDependencyHealth();
            var router = BuildRouter(primary, fallback, health);

            // When the render is retried on the fallback
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then the segment still airs (F70.1) — the primary was attempted (and failed), the
            // fallback rendered it.
            Assert.NotNull(path);
            Assert.Equal("Coming up next", primary.LastText);
            Assert.Equal(0, primary.CallCount);   // never completed — it threw
            Assert.Equal(1, fallback.CallCount);
        }

        [Fact]
        public static async Task Empty_fallback_endpoint_is_a_transparent_pass_through_to_primary()
        {
            // Given no Piper endpoint configured — zero behavior change vs today (F70.1)
            var primary = new FakeTtsSynthesizer();
            var fallback = new FakeTtsSynthesizer();
            var health = new FakeDependencyHealth();
            health.Set(UnhealthyKokoroVerdict());   // even an unhealthy verdict changes nothing here
            var router = BuildRouter(primary, fallback, health,
                new TestOptionsMonitor<TtsFallbackOptions>(new TtsFallbackOptions { Endpoint = "" }));

            // When a segment renders
            var path = await router.SynthesizeAsync("Coming up next", "af_heart", CancellationToken.None);

            // Then only the primary is ever called — no health read gates it, no fallback attempt.
            Assert.NotNull(path);
            Assert.Equal(1, primary.CallCount);
            Assert.Equal(0, fallback.CallCount);
        }
    }

    public static class ScenarioSamePipeline
    {
        [Fact]
        public static async Task Fallback_renders_pass_normalize_measure_cache_identically()
        {
            // Given a fallback-rendered segment (primary marked unhealthy so the fallback is the
            // one that actually renders it)
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var primary = new FakeTtsSynthesizer();
                var fallback = new FakeTtsSynthesizer();
                var health = new FakeDependencyHealth();
                health.Set(UnhealthyKokoroVerdict());
                var router = BuildRouter(primary, fallback, health);

                var analyzer = new FakeLoudnessAnalyzer();
                var cueAnalyzer = new FakeCueAnalyzer();
                var source = BuildSegmentSource(router, analyzer, cueAnalyzer, cacheRoot);

                // When its processing is inspected
                var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);

                // Then it passed the same Normalize → loudness-measure → cache path as primary
                // renders (F70.4): the fallback engine is the one that actually rendered it...
                Assert.Equal(1, fallback.CallCount);
                Assert.Equal(0, primary.CallCount);
                // ...and TtsSegmentSource's downstream pipeline ran exactly as it would for a
                // primary render — a measured MediaItem, cached to disk.
                Assert.NotNull(item);
                Assert.True(item!.Loudness.Measurable);
                Assert.NotNull(item.Cue);
                Assert.True(File.Exists(item.Locator));
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    // ------------------------------------------------------------------
    // SAD PATH
    // ------------------------------------------------------------------

    public static class SadPathBothEnginesDown
    {
        [Fact]
        public static async Task Segment_skips_loudly_and_music_continues()
        {
            // Given both primary and fallback unavailable
            var cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                var primary = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("kokoro down") };
                var fallback = new FakeTtsSynthesizer { ThrowOnNextCall = new IOException("piper down") };
                var health = new FakeDependencyHealth();
                var router = BuildRouter(primary, fallback, health);

                var sourceLogger = new CapturingLogger<TtsSegmentSource>();
                var source = BuildSegmentSource(
                    router, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), cacheRoot, sourceLogger);

                // When a segment render is attempted
                var act = async () => await source.RenderAsync(StationIdRequest(), CancellationToken.None);
                var item = await act();

                // Then the segment is skipped with a loud log...
                Assert.Null(item);
                Assert.NotEmpty(sourceLogger.Warnings);

                // ...and no exception ever propagates out of the render call — the render-ahead
                // caller (the playout feeder) is never interrupted, so music playout continues
                // uninterrupted (STORY-190 AC4, mirrors Story005/Story008's graceful-skip contract).
                var ex = await Record.ExceptionAsync(act);
                Assert.Null(ex);
            }
            finally
            {
                if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }
}
