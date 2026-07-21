// STORY-122 — Blurb audio stays bounded on disk
//
// BDD specification — xUnit. Fresh-per-airing blurb audio lands under blurbs/ and is
// swept opportunistically on render past Tts:BlurbRetentionHours; the (text,voice)
// forever-cache for templated kinds is untouched (gitea-#205's lesson, pre-empted).
// See docs/PLAN.md Epic T.

namespace GenWave.Tts.Tests.Specs;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Tts.Tests.Fakes;

public static class FeatureBlurbAudioGc
{
    const string StationId = "test-station";

    static SegmentRequest LeadInRequest() =>
        new(SegmentKind.LeadIn, "af_heart", "GenWave",
            new MediaItem("m1", "/media/x.mp3", "Astral Plane", default, "Valerie June"),
            DateTimeOffset.UtcNow, StationId);

    static SegmentRequest StationIdRequest() =>
        new(SegmentKind.StationId, "af_heart", "GenWave", null, DateTimeOffset.UtcNow, StationId);

    static TtsSegmentSource BuildSource(
        ISegmentCopyWriter copyWriter,
        FakeTtsSynthesizer synth,
        string cacheRoot,
        int blurbRetentionHours = 24,
        ILogger<TtsSegmentSource>? logger = null) =>
        new(
            copyWriter,
            synth,
            new FakeLoudnessAnalyzer(),
            new FakeCueAnalyzer(),
            NoCorrections.Provider(),
            new TestOptionsMonitor<TtsOptions>(new TtsOptions
            {
                CacheRoot = cacheRoot,
                Format = "wav",
                BlurbRetentionHours = blurbRetentionHours,
            }),
            logger ?? NullLogger<TtsSegmentSource>.Instance);

    // ---------------------------------------------------------------------
    // HAPPY PATH — routing and retention
    // ---------------------------------------------------------------------

    public sealed class ScenarioBlurbAudioRouting : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();

        [Fact]
        public async Task LlmAuthoredLeadInAudioLandsUnderBlurbs()
        {
            var copyWriter = new FakeSegmentCopyWriter("Fresh LLM copy for this airing.", freshPerAiring: true);
            var source = BuildSource(copyWriter, synth, cacheRoot);

            var item = await source.RenderAsync(LeadInRequest(), CancellationToken.None);

            // Not in the forever-cache root (F34.6, AC1).
            Assert.NotNull(item);
            Assert.Equal("blurbs", Path.GetFileName(Path.GetDirectoryName(item!.Locator)));
        }

        [Fact]
        public async Task TemplatedStationIdReusesItsForeverCachedFile()
        {
            var copyWriter = new FakeSegmentCopyWriter("Same station id copy every time.");
            var source = BuildSource(copyWriter, synth, cacheRoot);
            var request = StationIdRequest();

            await source.RenderAsync(request, CancellationToken.None);
            await source.RenderAsync(request, CancellationToken.None);

            // Second render, same (text, voice) → no new synthesis (F12.8 stands, AC3).
            Assert.Equal(1, synth.CallCount);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    public sealed class ScenarioRetentionSweep : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();

        string BlurbsDir => Path.Combine(cacheRoot, StationId, "blurbs");

        [Fact]
        public async Task FilesOlderThanRetentionAreDeletedOnRender()
        {
            Directory.CreateDirectory(BlurbsDir);
            var agedPath = Path.Combine(BlurbsDir, "aged-clip.wav");
            File.WriteAllBytes(agedPath, [0]);
            File.SetLastWriteTimeUtc(agedPath, DateTime.UtcNow.AddHours(-2));

            var copyWriter = new FakeSegmentCopyWriter("Fresh copy triggers the sweep.", freshPerAiring: true);
            var source = BuildSource(copyWriter, synth, cacheRoot, blurbRetentionHours: 1);

            await source.RenderAsync(LeadInRequest(), CancellationToken.None);

            // Aged blurbs/ files gone after a fresh blurb render (F34.6, AC2).
            Assert.False(File.Exists(agedPath));
        }

        [Fact]
        public async Task FreshBlurbFilesSurviveTheSweep()
        {
            Directory.CreateDirectory(BlurbsDir);
            var recentPath = Path.Combine(BlurbsDir, "recent-clip.wav");
            File.WriteAllBytes(recentPath, [0]);
            File.SetLastWriteTimeUtc(recentPath, DateTime.UtcNow.AddMinutes(-5));

            var copyWriter = new FakeSegmentCopyWriter("Fresh copy triggers the sweep.", freshPerAiring: true);
            var source = BuildSource(copyWriter, synth, cacheRoot, blurbRetentionHours: 1);

            await source.RenderAsync(LeadInRequest(), CancellationToken.None);

            // (F34.6, AC2).
            Assert.True(File.Exists(recentPath));
        }

        [Fact]
        public async Task ForeverCacheFilesSurviveTheSweepRegardlessOfAge()
        {
            var stationDir = Path.Combine(cacheRoot, StationId);
            Directory.CreateDirectory(stationDir);
            var foreverCachedPath = Path.Combine(stationDir, "forever-cached.wav");
            File.WriteAllBytes(foreverCachedPath, [0]);
            File.SetLastWriteTimeUtc(foreverCachedPath, DateTime.UtcNow.AddYears(-1));

            var copyWriter = new FakeSegmentCopyWriter("Fresh copy triggers the sweep.", freshPerAiring: true);
            var source = BuildSource(copyWriter, synth, cacheRoot, blurbRetentionHours: 1);

            await source.RenderAsync(LeadInRequest(), CancellationToken.None);

            // The sweep never reaches outside blurbs/ (F34.6, AC2).
            Assert.True(File.Exists(foreverCachedPath));
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the sweep is best-effort
    // ---------------------------------------------------------------------

    public sealed class ScenarioSweepFailure : IDisposable
    {
        readonly string cacheRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        readonly FakeTtsSynthesizer synth = new();

        [Fact]
        public async Task ASweepThrowNeverBlocksTheRender()
        {
            var blurbsDir = Path.Combine(cacheRoot, StationId, "blurbs");
            Directory.CreateDirectory(blurbsDir);

            // A directory masquerading as a file: enumerated by the sweep same as any clip, but
            // File.Delete on a directory path throws deterministically on Linux — a locked/undeletable
            // failure without relying on real file-locking primitives.
            var undeletablePath = Path.Combine(blurbsDir, "undeletable.wav");
            Directory.CreateDirectory(undeletablePath);
            File.SetLastWriteTimeUtc(undeletablePath, DateTime.UtcNow.AddHours(-2));

            var copyWriter = new FakeSegmentCopyWriter("Fresh copy triggers the sweep.", freshPerAiring: true);
            var logger = new CapturingLogger<TtsSegmentSource>();
            var source = BuildSource(copyWriter, synth, cacheRoot, blurbRetentionHours: 1, logger);

            var item = await source.RenderAsync(LeadInRequest(), CancellationToken.None);

            // Locked/undeletable file → render still returns its MediaItem; failure logs (AC4).
            Assert.NotNull(item);
            Assert.Single(logger.Warnings);
        }

        public void Dispose()
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            if (Directory.Exists(synth.OutputDirectory)) Directory.Delete(synth.OutputDirectory, recursive: true);
        }
    }
}
