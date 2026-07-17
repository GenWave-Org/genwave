// STORY-031 — FfmpegEnergyAnalyzer (short-term loudness over cue-trimmed windows)
//
// BDD specification — xUnit. Integration: runs ffmpeg against WAV fixtures.
// See docs/PLAN.md / docs/STORIES.md Epic H.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Loudness;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureFfmpegEnergyAnalyzer
{
    static FfmpegEnergyAnalyzer DefaultAnalyzer() =>
        new(new FakeOptionsMonitor<EnergyOptions>(new EnergyOptions()));

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioImplementationLivesInMrdGenwaveLoudness
    {
        [Fact]
        public void TypeIsInMrdGenWaveLoudnessNamespace()
        {
            var t = Type.GetType("GenWave.Loudness.FfmpegEnergyAnalyzer, GenWave.Loudness");
            Assert.NotNull(t);
            Assert.Equal("GenWave.Loudness", t.Namespace);
        }

        [Fact]
        public void ImplementsIEnergyAnalyzer()
        {
            Assert.True(typeof(IEnergyAnalyzer).IsAssignableFrom(typeof(FfmpegEnergyAnalyzer)));
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioEnergyReflectsIntroIntensity
    {
        [Fact]
        public async Task LoudIntroFixtureReadsHighIntroEnergy()
        {
            // Given a WAV with a loud opening; AnalyzeAsync → IntroEnergy well above 0.
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateLoudIntroFile(dir, "loud_intro.wav");
                var result = await DefaultAnalyzer().AnalyzeAsync(path, cueInSec: null, cueOutSec: null, CancellationToken.None);
                Assert.NotNull(result);
                Assert.InRange(result.IntroEnergy, 0.3, 1.0);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task QuietIntroReadsLowerThanLoudIntro()
        {
            // Given a soft-opening WAV; its IntroEnergy is measurably lower than the loud fixture's.
            var dir = TestMedia.NewTempDir();
            try
            {
                var loudPath = TestMedia.CreateLoudIntroFile(dir, "loud.wav");
                var quietPath = TestMedia.CreateQuietIntroFile(dir, "quiet.wav");

                var analyzer = DefaultAnalyzer();
                var loud = await analyzer.AnalyzeAsync(loudPath, cueInSec: null, cueOutSec: null, CancellationToken.None);
                var quiet = await analyzer.AnalyzeAsync(quietPath, cueInSec: null, cueOutSec: null, CancellationToken.None);

                Assert.NotNull(loud);
                Assert.NotNull(quiet);
                Assert.True(loud.IntroEnergy > quiet.IntroEnergy,
                    $"Expected loud ({loud.IntroEnergy:F3}) > quiet ({quiet.IntroEnergy:F3})");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioWindowsFollowCuePoints
    {
        [Fact]
        public async Task IntroWindowStartsAtCueInForWindowSeconds()
        {
            // cueInSec supplied → intro energy measured over first Library:Energy:WindowSeconds (12) after cueIn.
            // File: loud intro at [0,15), silence at [15,30).
            // With cueInSec = 0 and cueOutSec = 14 the intro window [0,12] captures loud content
            // and the outro window [2,14] also captures loud content — both windows are measurable.
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateLoudIntroFile(dir, "loud_intro_cue.wav");
                var result = await DefaultAnalyzer().AnalyzeAsync(path, cueInSec: 0.0, cueOutSec: 14.0, CancellationToken.None);
                Assert.NotNull(result);
                Assert.InRange(result.IntroEnergy, 0.3, 1.0);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task OutroWindowEndsAtCueOut()
        {
            // cueOutSec supplied → outro energy measured over the last window before cueOut.
            // File: loud intro at [0,15), silence at [15,30).
            // With cueOutSec = 15 the outro window [3,15] captures loud content → high energy.
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateLoudIntroFile(dir, "loud_outro_cue.wav");
                var result = await DefaultAnalyzer().AnalyzeAsync(path, cueInSec: 0.0, cueOutSec: 15.0, CancellationToken.None);
                Assert.NotNull(result);
                Assert.InRange(result.OutroEnergy, 0.3, 1.0);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        // AC4: null cue points → head window measures loud intro; tail window measures silent outro.
        // File: loud sine at [0,15), silence at [15,30). Default window = 12 s.
        // Intro window [0,12] → loud; outro window [18,30] → silence → 0.0.
        // Two Facts share the same fixture setup to keep each assertion focused.

        static async Task<EnergyPoints> ArrangeLoudIntroSilentTailResult()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateLoudIntroFile(dir, "null_cue.wav");
                var result = await DefaultAnalyzer().AnalyzeAsync(path, cueInSec: null, cueOutSec: null, CancellationToken.None);
                Assert.NotNull(result);
                return result;
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task NullCueIntroEnergyIsHigh()
        {
            // cueIn null → intro window is the raw head [0, 12] which contains the loud sine.
            var result = await ArrangeLoudIntroSilentTailResult();
            Assert.InRange(result.IntroEnergy, 0.3, 1.0);
        }

        [Fact]
        public async Task NullCueOutroEnergyIsNearZero()
        {
            // cueOut null → outro window is the raw tail [18, 30] which is pure silence → 0.0.
            var result = await ArrangeLoudIntroSilentTailResult();
            Assert.InRange(result.OutroEnergy, 0.0, 0.1);
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioValuesAreNormalized
    {
        [Fact]
        public async Task IntroAndOutroEnergyAreWithinZeroToOne()
        {
            // For any analyzable file, IntroEnergy ∈ [0,1] and OutroEnergy ∈ [0,1].
            var dir = TestMedia.NewTempDir();
            try
            {
                // Use a tone-only file so both windows are measurable.
                var path = TestMedia.CreateToneOnly(dir, "tone_normalized.wav", durationSec: 30.0);
                var result = await DefaultAnalyzer().AnalyzeAsync(path, cueInSec: null, cueOutSec: null, CancellationToken.None);
                Assert.NotNull(result);
                Assert.InRange(result.IntroEnergy, 0.0, 1.0);
                Assert.InRange(result.OutroEnergy, 0.0, 1.0);
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
    public sealed class ScenarioUndecodableFileDegrades
    {
        [Fact]
        public async Task FfmpegFailureReturnsNullAndDoesNotThrow()
        {
            // Given a non-decodable file; AnalyzeAsync returns null (never throws).
            var result = await DefaultAnalyzer().AnalyzeAsync(
                "/tmp/does-not-exist-genwave-story031.mp3",
                cueInSec: null,
                cueOutSec: null,
                CancellationToken.None);
            Assert.Null(result);
        }
    }
}
