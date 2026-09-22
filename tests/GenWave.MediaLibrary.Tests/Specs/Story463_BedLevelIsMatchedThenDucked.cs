// STORY-463 — Bed level is matched then ducked (gh-#712 · SPEC F196 · PLAN T542)
//
// BDD specification — xUnit. FfmpegAudioMixer.ResolveBedGainDb/BuildBedFilterGraph/
// ResolveAndLogBedGainDb are exercised directly via this project's own InternalsVisibleTo grant on
// GenWave.Loudness (csproj remarks) — no ffmpeg process, no I/O — the SAME escape hatch
// Story075_FfmpegAudioMixer.cs already uses for these pure seams. Every scenario but ScenarioAnOffTargetVoice
// fixes the voice at Loudness(-16.0, -1.0, Measurable: true) (the T542 orchestrator decision's own
// fixture) so the arithmetic in each Given below is exact.

using GenWave.Core.Domain;
using GenWave.Loudness;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureBedlevelismatchedthenducked
{
    const double TargetLufs = -16.0;
    const double DuckDb = -12.0;
    static readonly GenWave.Core.Domain.Loudness Voice = new(-16.0, -1.0, Measurable: true);

    static AudioMixRequest FilterGraphRequest(double bedFadeSeconds = 0.0) =>
        new("voice.wav", null, new AudioTags("station", "spot"), BedDuckDb: DuckDb, BedPadSeconds: 0.0,
            OutputPath: "out.wav", BedFadeSeconds: bedFadeSeconds);

    public sealed class ScenarioAQuietBed
    {
        // Given: target −16 LUFS, a bed measured −22 LUFS, Ads:BedDuckDb −12, BuildBedFilterGraph
        readonly string filterGraph;

        public ScenarioAQuietBed()
        {
            var gain = FfmpegAudioMixer.ResolveBedGainDb(DuckDb, TargetLufs, Voice, bedLufs: -22.0);
            filterGraph = FfmpegAudioMixer.BuildBedFilterGraph(
                FilterGraphRequest(bedFadeSeconds: 0.5), bedGainDb: gain, cueInSec: 0.0, cueOutSec: 10.0,
                totalDurationSec: 10.0, loopBufferSamples: 441000, delayMs: 0);
        }

        /// <summary>AC1 — volume=-6dB</summary>
        [Fact]
        public void PullsTheBedToTwelveUnderTarget() =>
            Assert.Contains("volume=-6dB", filterGraph, StringComparison.Ordinal);

        /// <summary>AC3 — fade filters follow the volume filter</summary>
        [Fact]
        public void KeepsTheFadeAfterTheVolume() =>
            Assert.True(
                filterGraph.IndexOf("volume=-6dB", StringComparison.Ordinal) <
                filterGraph.IndexOf("afade=t=out", StringComparison.Ordinal));
    }

    public sealed class ScenarioALoudBed
    {
        // Given: same settings, a bed measured −10 LUFS
        readonly string filterGraph;

        public ScenarioALoudBed()
        {
            var gain = FfmpegAudioMixer.ResolveBedGainDb(DuckDb, TargetLufs, Voice, bedLufs: -10.0);
            filterGraph = FfmpegAudioMixer.BuildBedFilterGraph(
                FilterGraphRequest(), bedGainDb: gain, cueInSec: 0.0, cueOutSec: 10.0, totalDurationSec: 10.0,
                loopBufferSamples: 441000, delayMs: 0);
        }

        /// <summary>AC2 — volume=-18dB</summary>
        [Fact]
        public void PullsALoudBedDownFurther() =>
            Assert.Contains("volume=-18dB", filterGraph, StringComparison.Ordinal);
    }

    /// <summary>
    /// gh-#746 preserved (T542 orchestrator decision): the voice reference is ALWAYS preferred over the
    /// station target when the voice is measurable, so an off-target Kokoro render still ducks the bed
    /// relative to the voice actually heard, not to what the station wishes the voice had landed at.
    /// Given: voice −20 LUFS, target −16 LUFS, a bed measured −22 LUFS, BedDuckDb −12.
    /// </summary>
    public sealed class ScenarioAnOffTargetVoice
    {
        readonly string filterGraph;

        public ScenarioAnOffTargetVoice()
        {
            var offTargetVoice = new GenWave.Core.Domain.Loudness(-20.0, -1.0, Measurable: true);
            var gain = FfmpegAudioMixer.ResolveBedGainDb(DuckDb, TargetLufs, offTargetVoice, bedLufs: -22.0);
            filterGraph = FfmpegAudioMixer.BuildBedFilterGraph(
                FilterGraphRequest(), bedGainDb: gain, cueInSec: 0.0, cueOutSec: 10.0, totalDurationSec: 10.0,
                loopBufferSamples: 441000, delayMs: 0);
        }

        [Fact]
        public void TheBedFollowsTheVoiceNotTheTarget() =>
            Assert.Contains("volume=-10dB", filterGraph, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioABedWithNoLoudness
    {
        // Given: bed.IntegratedLufs null (and the mixer's own measure attempt also failed); a recording logger
        const string BedPath = "beds/silent-catalog-miss.wav";

        readonly string filterGraph;
        readonly CapturingLogger<FfmpegAudioMixer> recordingLogger = new();

        public ScenarioABedWithNoLoudness()
        {
            var mixer = new FfmpegAudioMixer(new FakeLoudnessAnalyzer(), recordingLogger);
            var bed = new BedSpec(BedPath, CueInSec: null, CueOutSec: null, IntegratedLufs: null);
            var gain = mixer.ResolveAndLogBedGainDb(bed, DuckDb, TargetLufs, Voice, bedLufs: null);
            filterGraph = FfmpegAudioMixer.BuildBedFilterGraph(
                FilterGraphRequest(), bedGainDb: gain, cueInSec: 0.0, cueOutSec: 10.0, totalDurationSec: 10.0,
                loopBufferSamples: 441000, delayMs: 0);
        }

        /// <summary>AC5 — volume=-12dB</summary>
        [Fact]
        public void FallsBackToTheDuckAlone() =>
            Assert.Contains("volume=-12dB", filterGraph, StringComparison.Ordinal);

        /// <summary>AC6 — exactly one WARN names the locator</summary>
        [Fact]
        public void WarnsOncePerRender() =>
            Assert.Contains(BedPath, Assert.Single(recordingLogger.Warnings), StringComparison.Ordinal);
    }

    public sealed class ScenarioABedFarBelowRange
    {
        // Given: a bed measured −70 LUFS
        const string BedPath = "beds/gated-way-down.wav";

        readonly string filterGraph;
        readonly CapturingLogger<FfmpegAudioMixer> recordingLogger = new();

        public ScenarioABedFarBelowRange()
        {
            var mixer = new FfmpegAudioMixer(new FakeLoudnessAnalyzer(), recordingLogger);
            var bed = new BedSpec(BedPath, CueInSec: null, CueOutSec: null, IntegratedLufs: -70.0);
            var gain = mixer.ResolveAndLogBedGainDb(bed, DuckDb, TargetLufs, Voice, bedLufs: -70.0);
            filterGraph = FfmpegAudioMixer.BuildBedFilterGraph(
                FilterGraphRequest(), bedGainDb: gain, cueInSec: 0.0, cueOutSec: 10.0, totalDurationSec: 10.0,
                loopBufferSamples: 441000, delayMs: 0);
        }

        /// <summary>AC7 — clamped to +12dB</summary>
        [Fact]
        public void ClampsTheGain() =>
            Assert.Contains("volume=12dB", filterGraph, StringComparison.Ordinal);

        /// <summary>AC7 — the WARN carries the computed value</summary>
        // Computed pre-clamp: target -16 + duck -12 - bed -70 = 42, clamped to MaxBedGainDb (+12).
        [Fact]
        public void WarnsWithTheComputedValue() =>
            Assert.Contains("42", Assert.Single(recordingLogger.Warnings), StringComparison.Ordinal);
    }
}
