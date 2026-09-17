// STORY-016 — FfmpegCueAnalyzer (silencedetect parser)
//
// BDD specification — xUnit. The threshold/min-duration trio runs real ffmpeg at tier 1 (T507).
// See docs/PLAN.md and docs/STORIES.md Epic F.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
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

    // gh-#424 — an interior pause (a TTS sentence gap, a quiet mid-track break) must never become
    // cue_out; only silence that actually extends to EOF is a tail to trim. The buggy parser took
    // the LAST silence region unconditionally, cutting the final sentence of every multi-sentence
    // patter clip on air.
    [Trait("Category", "Integration")]
    public sealed class ScenarioInteriorPauseDoesNotSetCueOut
    {
        [Fact]
        public async Task CueOutSecIsTheFullFileNotThePauseStart()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                // tone 4s + pause 0.8s + tone 4s → audible to EOF at ~8.8s; the buggy parser cut at 4.0.
                var path = TestMedia.CreateToneSilenceTone(dir, "interior_pause.wav");
                var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions()));
                var cue = await analyzer.AnalyzeAsync(path, CancellationToken.None);
                Assert.NotNull(cue);
                Assert.Equal(0.0, cue.CueInSec);
                Assert.InRange(cue.CueOutSec, 8.5, 9.1);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioInteriorPausePlusTrailingSilenceTrimsOnlyTheTail
    {
        [Fact]
        public async Task CueOutSecApproximatesTheTrailingRegionStart()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                // tone 4s + pause 0.8s + tone 4s + trailing 3s → the trailing region starts at ~8.8s.
                var path = TestMedia.CreateToneSilenceToneSilence(dir, "pause_and_tail.wav");
                var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions()));
                var cue = await analyzer.AnalyzeAsync(path, CancellationToken.None);
                Assert.NotNull(cue);
                Assert.InRange(cue.CueOutSec, 8.6, 9.0);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // T507: no ffmpeg-process recorder exists in this test project, but that machinery was only ever
    // a means to an end — proving the configured CueDetectionOptions values actually reach ffmpeg's
    // real invocation. A real ffmpeg process against a real fixture proves that more directly than an
    // argv-string match would: run the SAME quiet-then-loud fixture through two different configured
    // SilenceThresholdDb values and show the classification flips between a detected leading cue and
    // no cue at all, and run a fixture with a brief leading gap through two different configured
    // MinSilenceDurationSec values and show detection flips. Either flip is only possible if the
    // option value actually reached the ffmpeg argv. ffmpeg is on PATH in the PR tier (CLAUDE.md;
    // Story345/Story399 in Host already run ffmpeg untraited), so this runs at tier 1 — no
    // Category=Integration trait.
    public sealed class ScenarioThresholdAndMinDurationAreConfigBound
    {
        [Fact]
        public async Task ALenientThresholdClassifiesTheQuietLeadAsSilenceAndCuesInAtTheLoudTone()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                // 1 s at a -6dB trim (measured ~-30 dBFS RMS: quieter than a -20dB threshold, so it
                // registers as silence), then 5 s at a +10dB boost (measured ~-14 dBFS RMS: louder
                // than -20dB, so playback resumes there — the fixture's own baseline sine sits around
                // -21..-24 dBFS, too quiet to clear -20dB unboosted).
                var path = TestMedia.CreateQuietToneThenLoudTone(
                    dir, "quiet_then_loud.wav", quietSec: 1.0, quietGainDb: -6.0, loudSec: 5.0, loudGainDb: 10.0);
                var opts = new CueDetectionOptions { SilenceThresholdDb = -20.0 };
                var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(opts));

                var cue = await analyzer.AnalyzeAsync(path, CancellationToken.None);

                var nonNullCue = Assert.IsType<CuePoints>(cue);
                Assert.InRange(nonNullCue.CueInSec, 0.9, 1.1);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task AStrictThresholdClassifiesTheSameQuietLeadAsHavingNoSilence()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                // Same fixture, but -50dB is quieter than the ~-30 dBFS quiet lead, so nothing
                // registers as silence at all: zero events, cue stays null.
                var path = TestMedia.CreateQuietToneThenLoudTone(
                    dir, "quiet_then_loud.wav", quietSec: 1.0, quietGainDb: -6.0, loudSec: 5.0, loudGainDb: 10.0);
                var opts = new CueDetectionOptions { SilenceThresholdDb = -50.0 };
                var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(opts));

                var cue = await analyzer.AnalyzeAsync(path, CancellationToken.None);

                Assert.Null(cue);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task AShortMinSilenceDurationDetectsABriefLeadingGapALongerDurationMisses()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                // 0.3 s of true silence, then a 5 s tone: long enough to qualify under a 0.1 s
                // minimum, too short to qualify under a 1.0 s minimum.
                var path = TestMedia.CreateSilenceThenTone(dir, "brief_leading_gap.wav", silenceSec: 0.3, toneSec: 5.0);
                var shortMinAnalyzer = new FfmpegCueAnalyzer(
                    new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions { MinSilenceDurationSec = 0.1 }));
                var longMinAnalyzer = new FfmpegCueAnalyzer(
                    new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions { MinSilenceDurationSec = 1.0 }));

                var shortMinCue = await shortMinAnalyzer.AnalyzeAsync(path, CancellationToken.None);
                var longMinCue = await longMinAnalyzer.AnalyzeAsync(path, CancellationToken.None);

                var nonNullShortMinCue = Assert.IsType<CuePoints>(shortMinCue);
                Assert.InRange(nonNullShortMinCue.CueInSec, 0.2, 0.4);
                Assert.True(longMinCue is null || longMinCue.CueInSec == 0.0);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
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
    public sealed class ScenarioEntirelySilentFileReturnsNull
    {
        [Fact]
        public async Task NoUsableContentYieldsNullNotFullExtent()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                // Guards the gh-#424 restructure: silence-only must stay null whether ffmpeg leaves
                // the final region open-ended or flushes a silence_end at EOF.
                var path = TestMedia.CreateSilenceOnly(dir, "silence_only.wav");
                var analyzer = new FfmpegCueAnalyzer(new FakeOptionsMonitor<CueDetectionOptions>(new CueDetectionOptions()));
                var cue = await analyzer.AnalyzeAsync(path, CancellationToken.None);
                Assert.Null(cue);
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
