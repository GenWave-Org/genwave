// STORY-358 — the verbatim overload owes the SAME non-fresh guard the ordinary path has (SPEC
// F144.2/F144.4, PLAN T341 review finding F7)
//
// BDD specification — xUnit. TtsSegmentSource.RenderAsync(SegmentRequest, SegmentCopy, ct) — the
// IVerbatimSegmentRenderer overload the Orchestrator's own announcement vend step calls — used to
// trust its caller's own FreshPerAiring stamp unchecked. This file pins the closed guard: a fresh
// announcement still renders (and still lands under blurbs/, exactly like the ordinary path's own
// LLM-authored blurbs), while a non-fresh one is dropped with the SAME drop WARN the ordinary path
// logs for a degraded SignOff/SignOn/ContextSegment/Announcement render — never a silent template
// floor line airing as if it were the owner's own words.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureAnnouncementVerbatimOverloadGuard
{
    const string StationId = "test-station";

    static SegmentRequest AnnouncementRequest() =>
        new(SegmentKind.Announcement, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, StationId);

    static TtsSegmentSource BuildSource(string cacheRoot, FakeTtsSynthesizer synth, ILogger<TtsSegmentSource> logger) =>
        new(
            // This overload never calls copyWriter — see IVerbatimSegmentRenderer's own remarks —
            // so a fixed, never-consulted stand-in satisfies the constructor only.
            new FakeSegmentCopyWriter("unused by this overload", freshPerAiring: true),
            synth,
            new FakeLoudnessAnalyzer(),
            new FakeCueAnalyzer(),
            NoCorrections.Provider(),
            NoCorrections.PersonaCache(),
            NoCorrections.PronunciationProvider(),
            NoCorrections.PersonaPronunciationCache(),
            NoCorrections.PersonaPaceCache(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" }),
            logger);

    public sealed class ScenarioTheOverloadNowGuardsNonFreshCopyToo : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly CapturingLogger<TtsSegmentSource> logger = new();

        [Fact]
        public async Task AFreshAnnouncementRendersUnderBlurbs()
        {
            var source = BuildSource(cacheRoot, synth, logger);
            var copy = new SegmentCopy("The garage sale starts at nine.", FreshPerAiring: true);

            var item = await source.RenderAsync(AnnouncementRequest(), copy, CancellationToken.None);

            Assert.NotNull(item);
            Assert.Equal("blurbs", Path.GetFileName(Path.GetDirectoryName(item!.Locator)));
        }

        [Fact]
        public async Task ANonFreshAnnouncementIsDroppedWithTheDropWarn()
        {
            var source = BuildSource(cacheRoot, synth, logger);
            var copy = new SegmentCopy("Inert template floor text.", FreshPerAiring: false);

            var item = await source.RenderAsync(AnnouncementRequest(), copy, CancellationToken.None);

            // Dropped before ever reaching the synthesizer — never merely a null RESULT of a render
            // that ran anyway.
            Assert.Null(item);
            Assert.Equal(0, synth.CallCount);
            Assert.Contains(logger.Warnings, w =>
                w.Contains("Announcement", StringComparison.Ordinal) &&
                w.Contains("not LLM-authored", StringComparison.Ordinal));
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }
}
