// STORY-016 — FfmpegCueAnalyzer (silencedetect parser)
//
// BDD specification — xUnit.
// /build-loop removes the Skip when implementing. See docs/PLAN.md and docs/STORIES.md Epic F.

using GenWave.Core.Abstractions;
using GenWave.Loudness;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureFfmpegCueAnalyzer
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioImplementationLivesInMrdGenwaveLoudness
    {
        [Fact]
        public void TypeIsInMrdGenWaveLoudnessNamespace()
        {
            var t = Type.GetType("GenWave.Loudness.FfmpegCueAnalyzer, GenWave.Loudness");
            Assert.NotNull(t);
            Assert.Equal("GenWave.Loudness", t.Namespace);
        }

        [Fact]
        public void ImplementsICueAnalyzer()
        {
            var t = typeof(FfmpegCueAnalyzer);
            Assert.True(typeof(ICueAnalyzer).IsAssignableFrom(t));
        }

        [Fact]
        public void FfmpegLoudnessAnalyzerStillExistsUnchanged()
        {
            // Regression: T019 must NOT alter the loudness analyzer's contract or location.
            var t = Type.GetType("GenWave.Loudness.FfmpegLoudnessAnalyzer, GenWave.Loudness");
            Assert.NotNull(t);
            Assert.True(typeof(ILoudnessAnalyzer).IsAssignableFrom(t));
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioDetectsLeadingSilenceOnASyntheticWav
    {
        [Fact]
        public async Task CueInSecApproximatesThreeSeconds()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateSilenceThenTone(dir, "leading_silence.wav", silenceSec: 3.0, toneSec: 10.0);
                var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions()));
                var cue = await analyzer.AnalyzeAsync(path, CancellationToken.None);
                Assert.NotNull(cue);
                Assert.InRange(cue.CueInSec, 2.9, 3.1);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioDetectsTrailingSilenceOnASyntheticWav
    {
        [Fact]
        public async Task CueOutSecApproximatesTenSeconds()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateToneThenSilence(dir, "trailing_silence.wav", toneSec: 10.0, silenceSec: 4.0);
                var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions()));
                var cue = await analyzer.AnalyzeAsync(path, CancellationToken.None);
                Assert.NotNull(cue);
                Assert.InRange(cue.CueOutSec, 9.9, 10.1);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    public sealed class ScenarioThresholdAndMinDurationAreConfigBound
    {
        [Fact(Skip = "Requires ffmpeg process recorder — no interception facility in this test project")]
        public void FfmpegInvocationIncludesConfiguredNoiseThreshold()
        {
            // Use an ffmpeg-process recorder. With opts.SilenceThresholdDb = -50.0,
            // assert the recorded argv contains "silencedetect=noise=-50dB".
            Assert.Fail("pending process-recorder facility");
        }

        [Fact(Skip = "Requires ffmpeg process recorder — no interception facility in this test project")]
        public void FfmpegInvocationIncludesConfiguredMinSilenceDuration()
        {
            // With opts.MinSilenceDurationSec = 0.5, assert "duration=0.5".
            Assert.Fail("pending process-recorder facility");
        }

        [Fact(Skip = "Requires ffmpeg process recorder — no interception facility in this test project")]
        public void AlteredThresholdReachesFfmpegArgv()
        {
            // With opts.SilenceThresholdDb = -65.0, assert "noise=-65dB" appears.
            Assert.Fail("pending process-recorder facility");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioFileWithNoDetectableSilenceReturnsFullExtentsOrNull
    {
        [Fact]
        public async Task ReturnsEitherZeroCueInSecOrNullResult()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateToneOnly(dir, "tone_only.wav", durationSec: 10.0);
                var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions()));
                var cue = await analyzer.AnalyzeAsync(path, CancellationToken.None);
                Assert.True(cue is null || cue.CueInSec == 0.0);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioFfmpegInvocationFailureReturnsNull
    {
        [Fact]
        public async Task NonexistentPathReturnsNullNotException()
        {
            var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions()));
            var cue = await analyzer.AnalyzeAsync("/tmp/does-not-exist-genwave-story016.mp3", CancellationToken.None);
            Assert.Null(cue);
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioCancellationIsObserved
    {
        [Fact]
        public async Task Throws_OperationCanceledException_WhenTokenCancelled()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                // Use a real file so ffmpeg actually starts — cancelled immediately after.
                var path = TestMedia.CreateToneOnly(dir, "cancel_test.wav", durationSec: 10.0);
                var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions()));
                using var cts = new CancellationTokenSource();
                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => analyzer.AnalyzeAsync(path, cts.Token));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
