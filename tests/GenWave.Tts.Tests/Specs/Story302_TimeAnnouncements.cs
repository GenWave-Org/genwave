// STORY-302 — The time announcement (F110.3) — the Tts-level half
//
// BDD specification — xUnit. T232's Orchestration-level facts (GenWave.Orchestration.Tests/Specs/
// Story302_TimeAnnouncements.cs) cover the drain arm's own request-building — the ACTUAL cache-hit
// mechanism (TtsSegmentSource's file-exists short-circuit over a deterministic text/voice/station
// hash) is a Tts-level fact that project cannot see (no ProjectReference to GenWave.Tts), mirroring
// Story297_ContextSegmentsAir.cs's own split one epic over. Story005_TtsSegmentSource.cs already
// proves the general cache mechanism generically (any kind, same request twice); this file proves
// STORY-302 AC2's own specific claim: TWO DIFFERENT SegmentRequests that both name "the same hour"
// (different calendar days, same hour-of-day — the honest shape of "the hour recurring") render
// byte-identical copy and so land under the SAME hash.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureTimeAnnouncementCacheHit
{
    static TtsSegmentSource BuildSource(FakeTtsSynthesizer synth, string cacheRoot) =>
        new(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            synth,
            new FakeLoudnessAnalyzer(),
            new FakeCueAnalyzer(),
            NoCorrections.Provider(),
            NoCorrections.PersonaCache(),
            NoCorrections.PronunciationProvider(),
            NoCorrections.PersonaPronunciationCache(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            NullLogger<TtsSegmentSource>.Instance);

    static SegmentRequest TimeDateRequest(DateTimeOffset localNow) =>
        new(SegmentKind.TimeDate, "af_heart", "GenWave", null, localNow, "test-station");

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioSecondRenderOfTheSameHourIsACacheHit : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        // FakeTtsSynthesizer writes to {OutputDirectory}/{hash}.wav using the same hash formula
        // TtsSegmentSource itself computes (mirrors Story005's own ScenarioCacheHitAvoidsResynthesis).
        FakeTtsSynthesizer BuildSynthForCache() => new() { OutputDirectory = cacheRoot };

        [Fact]
        public async Task TheSameHourIsACacheHit()
        {
            var synth = BuildSynthForCache();
            var source = BuildSource(synth, cacheRoot);

            // Same hour-of-day (14:00), a day apart — the "hour recurs" shape SPEC F110.3's AC2
            // actually describes, not merely the literal same instant twice. PatterTemplateRenderer
            // reads only the hour component, so both requests render identical text.
            var first = TimeDateRequest(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero));
            var second = TimeDateRequest(new DateTimeOffset(2026, 8, 9, 14, 0, 0, TimeSpan.Zero));

            await source.RenderAsync(first, CancellationToken.None);
            Assert.Equal(1, synth.CallCount);

            synth.ResetCallCount();
            await source.RenderAsync(second, CancellationToken.None);
            Assert.Equal(0, synth.CallCount); // cache hit — no re-synthesis (SPEC F110.3, AC2)
        }

        [Fact]
        public async Task ADifferentHourIsACacheMiss()
        {
            // Sad-path pin, the other half of the same claim: a DIFFERENT hour must NOT collide —
            // proves the hour genuinely drives the hash rather than the cache hit above being an
            // accident of some other field.
            var synth = BuildSynthForCache();
            var source = BuildSource(synth, cacheRoot);

            var twoOClock = TimeDateRequest(new DateTimeOffset(2026, 8, 8, 14, 0, 0, TimeSpan.Zero));
            var threeOClock = TimeDateRequest(new DateTimeOffset(2026, 8, 8, 15, 0, 0, TimeSpan.Zero));

            await source.RenderAsync(twoOClock, CancellationToken.None);
            Assert.Equal(1, synth.CallCount);

            synth.ResetCallCount();
            await source.RenderAsync(threeOClock, CancellationToken.None);
            Assert.Equal(1, synth.CallCount); // different hour, different text — re-synthesized
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }
    }
}
