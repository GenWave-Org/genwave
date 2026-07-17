// STORY-005 — ITtsSegmentSource (render → measure → cache)

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureTtsSegmentSourceRenderMeasureCache
{
    // ------------------------------------------------------------------
    // Shared fixture helpers
    // ------------------------------------------------------------------

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

    // ------------------------------------------------------------------
    // HAPPY PATH
    // ------------------------------------------------------------------

    // gitea-#251: the SDK-clean TTS contracts extracted from Core into the dependency-free
    // GenWave.Abstractions project (packaged as GenWave.Abstractions); namespaces unchanged.
    public sealed class ScenarioContractsLiveInAbstractions
    {
        [Fact]
        public void ITtsSegmentSourceIsInAbstractions()
        {
            var t = Type.GetType("GenWave.Core.Abstractions.ITtsSegmentSource, GenWave.Abstractions");
            Assert.NotNull(t);
        }

        [Fact]
        public void SegmentRequestRecordExistsInAbstractions()
        {
            var t = Type.GetType("GenWave.Core.Domain.SegmentRequest, GenWave.Abstractions");
            Assert.NotNull(t);
        }

        [Fact]
        public void SegmentKindEnumHasFourCases()
        {
            var values = Enum.GetValues<SegmentKind>();
            Assert.Equal(4, values.Length);
        }
    }

    public sealed class ScenarioRendersNewSegmentWhenCacheMisses : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task SynthesizerCalledExactlyOnce()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            Assert.Equal(1, synth.CallCount);
        }

        [Fact]
        public async Task ReturnsNonNullMediaItem()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            Assert.NotNull(item);
        }

        [Fact]
        public async Task MediaIdStartsWithTtsColonPrefix()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            Assert.StartsWith("tts:", item!.MediaId);
        }

        [Fact]
        public async Task MeasuredLoudnessIsMeasurableTrue()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            Assert.True(item!.Loudness.Measurable);
        }

        [Fact]
        public async Task DisplayTitleIsStationNameNotSpokenText()
        {
            // Issue gitea-#154: players show MediaItem.Title as now-playing — the patter script must
            // never appear there; the station name does instead.
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            Assert.Equal("GenWave", item!.Title);
            Assert.DoesNotContain("listening", item.Title, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ArtistIsTheStationName()
        {
            // Issue gitea-#192: patter is station-authored content, so the gitea-#172 brand rule applies —
            // artist = <Station Name>. Without it, every station ID / lead-in / back-announce
            // surfaced "Unknown artist" in the admin UI's now-playing and play-history.
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            Assert.Equal("GenWave", item!.Artist);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    public sealed class ScenarioMediaIdIsContentHashOfTextAndVoice : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task TwoIdenticalRequestsProduceEqualMediaIds()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var request = StationIdRequest();
            var a = await source.RenderAsync(request, CancellationToken.None);
            var b = await source.RenderAsync(request, CancellationToken.None);
            Assert.Equal(a!.MediaId, b!.MediaId);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    public sealed class ScenarioCacheHitAvoidsResynthesis : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeLoudnessAnalyzer analyzer = new();

        // FakeTtsSynthesizer writes to {OutputDirectory}/{hash}.wav using the same hash formula
        // as KokoroTtsSynthesizer, so setting OutputDirectory = cacheRoot means TtsSegmentSource
        // will find the file on the second call and skip synthesis.
        FakeTtsSynthesizer BuildSynthForCache() => new() { OutputDirectory = cacheRoot };

        [Fact]
        public async Task SynthesizerNotCalledOnSecondRequest()
        {
            var synth = BuildSynthForCache();
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var request = StationIdRequest();

            await source.RenderAsync(request, CancellationToken.None);
            Assert.Equal(1, synth.CallCount);

            synth.ResetCallCount();
            await source.RenderAsync(request, CancellationToken.None);
            Assert.Equal(0, synth.CallCount);
        }

        [Fact]
        public async Task ReturnedLocatorIsTheSameCachedFile()
        {
            var synth = BuildSynthForCache();
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var request = StationIdRequest();

            var a = await source.RenderAsync(request, CancellationToken.None);
            var b = await source.RenderAsync(request, CancellationToken.None);
            Assert.Equal(a!.Locator, b!.Locator);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }
    }

    public sealed class ScenarioMediaItemCarriesAUsableLocator : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task LocatorPointsToExistingFile()
        {
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            Assert.True(File.Exists(item!.Locator));
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // SAD PATH
    // ------------------------------------------------------------------

    public sealed class ScenarioSynthesizerFailureReturnsNullNeverThrows : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task RenderReturnsNullWhenSynthesizerThrows()
        {
            synth.ThrowOnNextCall = new IOException("kokoro down");
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            Assert.Null(item);
        }

        [Fact]
        public async Task NoExceptionIsPropagated()
        {
            synth.ThrowOnNextCall = new IOException("kokoro down");
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var act = async () => await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            var ex = await Record.ExceptionAsync(act);
            Assert.Null(ex);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    public sealed class ScenarioLoudnessMeasurementFailureReturnsNull : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task RenderReturnsNullWhenAnalyzerThrows()
        {
            analyzer.ThrowOnNextCall = new InvalidDataException();
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(StationIdRequest(), CancellationToken.None);
            Assert.Null(item);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    public sealed class ScenarioCancellationPropagatesCleanly : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();
        readonly FakeLoudnessAnalyzer analyzer = new();

        [Fact]
        public async Task RenderReturnsNullOrThrowsCanceledWhenTokenIsTripped()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var source = BuildSource(synth, analyzer, new FakeCueAnalyzer(), cacheRoot);
            var item = await source.RenderAsync(StationIdRequest(), cts.Token);
            Assert.Null(item);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }
}
