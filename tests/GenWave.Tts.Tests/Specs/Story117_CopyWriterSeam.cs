// STORY-117 — Copy-writer seam + tag widening land without behavior change
//
// BDD specification — xUnit. The ISegmentCopyWriter seam slots in at TtsSegmentSource's
// single copy-producing call site; TemplateCopyWriter wraps PatterTemplateRenderer
// verbatim and can never fail.
// See docs/PLAN.md Epic T.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureCopyWriterSeam
{
    static SegmentRequest StationIdRequest() =>
        new(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest BackAnnounceRequest() =>
        new(SegmentKind.BackAnnounce, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, "test-station");

    static SegmentRequest TimeDateRequest() =>
        new(SegmentKind.TimeDate, "af_heart", "GenWave", null,
            new DateTimeOffset(2026, 6, 9, 14, 37, 0, TimeSpan.FromHours(-4)), "test-station");

    // ---------------------------------------------------------------------
    // HAPPY PATH — the template writer preserves today's copy exactly
    // ---------------------------------------------------------------------

    public sealed class ScenarioTemplateWriterPreservesShippedCopy
    {
        readonly PatterTemplateRenderer renderer = new();
        readonly TemplateCopyWriter writer;

        public ScenarioTemplateWriterPreservesShippedCopy() => writer = new TemplateCopyWriter(renderer);

        [Fact]
        public async Task StationIdCopyIsByteIdenticalToTheRenderer()
        {
            var request = StationIdRequest();
            var written = await writer.WriteAsync(request, CancellationToken.None);
            Assert.Equal(renderer.Expand(request), written.Text);
        }

        [Fact]
        public async Task LeadInCopyIsByteIdenticalToTheRenderer()
        {
            var request = LeadInRequest();
            var written = await writer.WriteAsync(request, CancellationToken.None);
            Assert.Equal(renderer.Expand(request), written.Text);
        }

        [Fact]
        public async Task BackAnnounceCopyIsByteIdenticalToTheRenderer()
        {
            var request = BackAnnounceRequest();
            var written = await writer.WriteAsync(request, CancellationToken.None);
            Assert.Equal(renderer.Expand(request), written.Text);
        }

        [Fact]
        public async Task TimeDateCopyIsByteIdenticalToTheRenderer()
        {
            var request = TimeDateRequest();
            var written = await writer.WriteAsync(request, CancellationToken.None);
            Assert.Equal(renderer.Expand(request), written.Text);
        }
    }

    public sealed class ScenarioTtsSegmentSourceConsumesTheSeam : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task RenderAsyncSourcesItsTextFromTheCopyWriter()
        {
            var copyWriter = new FakeSegmentCopyWriter("Spin this one up, folks.");
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                copyWriter, synth, analyzer, new FakeCueAnalyzer(), NoCorrections.Provider(),
                NoCorrections.PersonaCache(), opts,
                NullLogger<TtsSegmentSource>.Instance);

            await source.RenderAsync(StationIdRequest(), CancellationToken.None);

            Assert.Equal("Spin this one up, folks.", synth.LastText);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the terminal rung cannot fail
    // ---------------------------------------------------------------------

    public sealed class ScenarioTemplateWriterNeverFails
    {
        readonly TemplateCopyWriter writer = new(new PatterTemplateRenderer());

        [Fact]
        public async Task NullTrackLeadInYieldsTheShippedFallbackPhrasing()
        {
            var request = new SegmentRequest(SegmentKind.LeadIn, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");
            var text = await writer.WriteAsync(request, CancellationToken.None);
            Assert.Equal("Coming up next.", text.Text);
        }

        [Fact]
        public async Task NullTrackBackAnnounceYieldsTheShippedFallbackPhrasing()
        {
            var request = new SegmentRequest(SegmentKind.BackAnnounce, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");
            var text = await writer.WriteAsync(request, CancellationToken.None);
            Assert.Equal("That was your last track.", text.Text);
        }
    }
}
