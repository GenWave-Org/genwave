// STORY-177 — TTS patter carries its measured duration
//
// BDD specification — xUnit (SPEC F66.1). The render path already measures cue points; the
// MediaItem must be stamped with DurationMs = round(CueOutSec × 1000) — the same derivation
// SafeSegmentAuthor uses for authored segments. Null only when cue analysis failed (degraded,
// logged) — never fabricated. Downstream flow into now-playing/history rides the feeder's
// existing pushedMeta duration path (proven for music) once the item carries the value.
// Red until PLAN T06.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureTtsPatterDuration
{
    static TtsSegmentSource BuildSource(
        FakeTtsSynthesizer synth,
        FakeLoudnessAnalyzer analyzer,
        FakeCueAnalyzer cueAnalyzer,
        string cacheRoot)
    {
        var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
        return new TtsSegmentSource(
            new TemplateCopyWriter(new PatterTemplateRenderer()),
            synth,
            analyzer,
            cueAnalyzer,
            opts,
            NullLogger<TtsSegmentSource>.Instance);
    }

    static SegmentRequest StationIdRequest() =>
        new(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, "test-station");

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioCueMeasuredDurationIsStamped : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();
        readonly FakeCueAnalyzer fakeCue = new();

        [Fact]
        public async Task DurationMsIsCueOutSecInMilliseconds()
        {
            fakeCue.Returns(new CuePoints(CueInSec: 0.2, CueOutSec: 12.345));
            var source = BuildSource(synth, analyzer, fakeCue, cacheRoot);

            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);

            Assert.Equal(12345, item!.DurationMs);
        }

        [Fact]
        public async Task DurationRoundsToTheNearestMillisecond()
        {
            fakeCue.Returns(new CuePoints(CueInSec: 0.0, CueOutSec: 7.9996));
            var source = BuildSource(synth, analyzer, fakeCue, cacheRoot);

            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);

            Assert.Equal(8000, item!.DurationMs);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioCueFailureNeverFabricates : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();
        readonly FakeCueAnalyzer fakeCue = new();

        [Fact]
        public async Task DurationMsStaysNullWhenCueAnalysisFails()
        {
            fakeCue.Throws(new InvalidOperationException("cue analysis failed"));
            var source = BuildSource(synth, analyzer, fakeCue, cacheRoot);

            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);

            Assert.Null(item!.DurationMs);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }
}
