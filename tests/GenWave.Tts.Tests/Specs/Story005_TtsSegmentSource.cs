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
            NoCorrections.Provider(),
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

    // Story185-style regression: TtsSegmentSource.RenderAsync used to hash only
    // (text, voice, stationId) — computed from the PRE-normalization copy.Text — so an evergreen
    // cached clip (StationId/LeadIn/BackAnnounce, FreshPerAiring:false, never GC'd) kept airing the
    // OLD pronunciation forever after an operator saved a corrections change, because a cache hit
    // never calls synthesizer.SynthesizeAsync — the only place NormalizingTtsSynthesizer's
    // corrections actually apply. Folding SpeechCorrectionProvider.ContentHash into the cache key
    // fixes this: a corrections rebuild re-keys every subsequent lookup.
    public sealed class ScenarioCorrectionsRebuildReKeysTheCache : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeLoudnessAnalyzer analyzer = new();
        readonly FakeTtsSynthesizer innerSynth = new();

        [Fact]
        public async Task CorrectionsRebuildForcesResynthesisWithTheNewRuleApplied()
        {
            // Given a station-ID clip rendered once through the REAL NormalizingTtsSynthesizer (the
            // one production hand-off corrections apply through) with no corrections configured yet...
            var correctionsMonitor = new ChangeableOptionsMonitor<TtsCorrectionsOptions>(new TtsCorrectionsOptions());
            var corrections = new SpeechCorrectionProvider(correctionsMonitor, NullLogger<SpeechCorrectionProvider>.Instance);
            var normalizingSynth = new NormalizingTtsSynthesizer(
                innerSynth, corrections, new CorrectionsFiredStats(), NullLogger<NormalizingTtsSynthesizer>.Instance);
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                new FakeSegmentCopyWriter("Coming up, a deep cut from MacLeod."),
                normalizingSynth, analyzer, new FakeCueAnalyzer(), corrections, opts,
                NullLogger<TtsSegmentSource>.Instance);
            var request = StationIdRequest();

            var first = await source.RenderAsync(request, CancellationToken.None);
            Assert.Equal(1, innerSynth.CallCount);
            Assert.Equal("Coming up, a deep cut from MacLeod.", innerSynth.LastText);

            // A second render with nothing changed is still a genuine cache hit.
            await source.RenderAsync(request, CancellationToken.None);
            Assert.Equal(1, innerSynth.CallCount);

            // When an operator saves a correction — the same live-rebuild OnChange path
            // SpeechCorrectionProvider subscribes to in production...
            correctionsMonitor.Change(new TtsCorrectionsOptions
            {
                Corrections = "[{\"from\":\"MacLeod\",\"to\":\"Muh-cloud\"}]",
            });

            // Then the very next render of the SAME text re-keys the cache, invokes the
            // synthesizer again (never silently reuses the stale cached audio), and the corrected
            // pronunciation reaches it (F68.5, F68.7).
            var second = await source.RenderAsync(request, CancellationToken.None);
            Assert.Equal(2, innerSynth.CallCount);
            Assert.Equal("Coming up, a deep cut from Muh-cloud.", innerSynth.LastText);
            Assert.NotEqual(first!.MediaId, second!.MediaId);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(innerSynth.OutputDirectory)) Directory.Delete(innerSynth.OutputDirectory, recursive: true);
        }
    }

    // Review finding (High): SpeechCorrectionProvider.Version was a process-local counter that
    // resets to 0 at every construction, while the TTS cache (a named Docker volume — stationDir
    // files are evergreen, never swept) and the corrections rules themselves (Postgres-backed
    // settings) both persist across a container redeploy. A fresh process's version=0 could
    // therefore collide with an orphaned pre-redeploy cache entry and serve stale pronunciation
    // again. This scenario simulates that restart lifecycle directly: two independent
    // SpeechCorrectionProvider + TtsSegmentSource pairs over the SAME cache directory, standing in
    // for "before redeploy" and "after redeploy", proving ContentHash (a deterministic fingerprint
    // of the rules, not a counter) both survives a restart with unchanged rules and re-keys on a
    // changed one.
    public sealed class ScenarioRestartLifecycleNeitherOrphansNorCollidesTheCache : IDisposable
    {
        const string CorrectedText = "Coming up, a deep cut from MacLeod.";

        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly List<FakeTtsSynthesizer> synths = [];

        (TtsSegmentSource Source, FakeTtsSynthesizer InnerSynth) BuildInstance(string? correctionsJson)
        {
            // A brand-new SpeechCorrectionProvider — Version starts back at 0 here, exactly as it
            // would in a freshly started container — standing in for "the process restarted".
            var provider = new SpeechCorrectionProvider(
                new TestOptionsMonitor<TtsCorrectionsOptions>(new TtsCorrectionsOptions { Corrections = correctionsJson }),
                NullLogger<SpeechCorrectionProvider>.Instance);
            var innerSynth = new FakeTtsSynthesizer();
            synths.Add(innerSynth);
            var normalizingSynth = new NormalizingTtsSynthesizer(
                innerSynth, provider, new CorrectionsFiredStats(), NullLogger<NormalizingTtsSynthesizer>.Instance);
            var opts = new TestOptionsMonitor<TtsOptions>(new TtsOptions { CacheRoot = cacheRoot, Format = "wav" });
            var source = new TtsSegmentSource(
                new FakeSegmentCopyWriter(CorrectedText),
                normalizingSynth, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), provider, opts,
                NullLogger<TtsSegmentSource>.Instance);

            return (source, innerSynth);
        }

        [Fact]
        public async Task RestartWithUnchangedRulesIsACacheHit()
        {
            const string rulesJson = "[{\"from\":\"MacLeod\",\"to\":\"Muh-cloud\"}]";

            // Given a station-ID clip rendered once, before a "restart", with rules R already in
            // effect (pre-populating the shared cache directory)...
            var (preRestartSource, preRestartSynth) = BuildInstance(rulesJson);
            var request = StationIdRequest();
            var before = await preRestartSource.RenderAsync(request, CancellationToken.None);
            Assert.Equal(1, preRestartSynth.CallCount);

            // When the process "restarts" — a FRESH SpeechCorrectionProvider (Version resets to 0)
            // constructed over the SAME rules R, backing a FRESH TtsSegmentSource over the SAME
            // cache directory...
            var (postRestartSource, postRestartSynth) = BuildInstance(rulesJson);

            // Then rendering the SAME text is a genuine cache HIT — a restart with unchanged rules
            // must never orphan the pre-restart cache entry and force a redundant re-synthesis.
            var after = await postRestartSource.RenderAsync(request, CancellationToken.None);
            Assert.Equal(0, postRestartSynth.CallCount);
            Assert.Equal(before!.MediaId, after!.MediaId);
        }

        [Fact]
        public async Task RestartWithChangedRulesIsACacheMissWithCorrectedAudio()
        {
            // Given the same clip rendered once, before the "restart", under the ORIGINAL rules R...
            var (preRestartSource, preRestartSynth) = BuildInstance(
                "[{\"from\":\"MacLeod\",\"to\":\"Muh-cloud\"}]");
            var request = StationIdRequest();
            var before = await preRestartSource.RenderAsync(request, CancellationToken.None);
            Assert.Equal("Coming up, a deep cut from Muh-cloud.", preRestartSynth.LastText);

            // When the process restarts with a CHANGED rule R' — a fresh provider over the new
            // rules, backing a fresh source over the SAME cache directory...
            var (postRestartSource, postRestartSynth) = BuildInstance(
                "[{\"from\":\"MacLeod\",\"to\":\"Mac Cloud\"}]");

            // Then rendering the SAME text is a genuine cache MISS: the corrected rule reaches the
            // very next render instead of silently reusing the stale pre-restart audio.
            var after = await postRestartSource.RenderAsync(request, CancellationToken.None);
            Assert.Equal(1, postRestartSynth.CallCount);
            Assert.Equal("Coming up, a deep cut from Mac Cloud.", postRestartSynth.LastText);
            Assert.NotEqual(before!.MediaId, after!.MediaId);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            foreach (var synth in synths)
                if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
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
