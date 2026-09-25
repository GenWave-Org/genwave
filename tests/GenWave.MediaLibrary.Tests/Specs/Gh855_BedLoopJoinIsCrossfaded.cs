// gh-#855 — the bed loop join is crossfaded, not spliced. Pure facts pin
// FfmpegAudioMixer.BuildBedFilterGraph's copy/crossfade arithmetic directly (this project's own
// InternalsVisibleTo grant, as Story463_BedLevelIsMatchedThenDucked.cs already uses); the integration
// fact renders a real short bed under a longer voice and measures the join itself.

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using GenWave.Core.Domain;
using GenWave.Loudness;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureBedLoopJoinIsCrossfaded
{
    static AudioMixRequest FilterGraphRequest() =>
        new("voice.wav", null, new AudioTags("station", "spot"), BedDuckDb: 0.0, BedPadSeconds: 0.0,
            OutputPath: "out.wav");

    // ---------------------------------------------------------------------
    // PURE FACTS — BuildBedFilterGraph, no ffmpeg process
    // ---------------------------------------------------------------------

    public sealed class ScenarioAShortClipRepeatsAcrossALongerTotal
    {
        // Given: a 6s cue under a 20s total — crossfadeSec=0.5, stepSec=5.5,
        // copies = ceil((20 - 0.5) / 5.5) = 4, so 3 joins at absolute offsets 5.5/11.0/16.5.
        readonly string filterGraph;

        public ScenarioAShortClipRepeatsAcrossALongerTotal() =>
            filterGraph = FfmpegAudioMixer.BuildBedFilterGraph(
                FilterGraphRequest(), bedGainDb: 0.0,
                cueInSec: 0.0, cueOutSec: 6.0, totalDurationSec: 20.0, delayMs: 0);

        [Fact]
        public void EmitsExactlyThreeJoinsAcrossFourCopies() =>
            Assert.Equal(3, CountOccurrences(filterGraph, "afade=t=in"));

        [Fact]
        public void TheFirstCopyHasOnlyTheFadeOutAtTheStepBoundary() =>
            Assert.Contains("[s0]afade=t=out:st=5.5:d=0.5:curve=qsin[c0];", filterGraph, StringComparison.Ordinal);

        [Fact]
        public void TheSecondCopyFadesInThenOutAtTheFirstStepOffset() =>
            Assert.Contains(
                "[s1]afade=t=in:st=0:d=0.5:curve=qsin,afade=t=out:st=5.5:d=0.5:curve=qsin,adelay=delays=5500:all=1[c1];",
                filterGraph, StringComparison.Ordinal);

        [Fact]
        public void TheThirdCopyStartsAtTheSecondStepOffset() =>
            Assert.Contains(
                "[s2]afade=t=in:st=0:d=0.5:curve=qsin,afade=t=out:st=5.5:d=0.5:curve=qsin,adelay=delays=11000:all=1[c2];",
                filterGraph, StringComparison.Ordinal);

        [Fact]
        public void TheLastCopyHasOnlyTheFadeInAtTheThirdStepOffset() =>
            Assert.Contains(
                "[s3]afade=t=in:st=0:d=0.5:curve=qsin,adelay=delays=16500:all=1[c3];",
                filterGraph, StringComparison.Ordinal);
    }

    public sealed class ScenarioALongClipNeverRepeats
    {
        // Given: a 10s cue under a 10s total — the clip alone already covers the mix.
        readonly string filterGraph;

        public ScenarioALongClipNeverRepeats() =>
            filterGraph = FfmpegAudioMixer.BuildBedFilterGraph(
                FilterGraphRequest(), bedGainDb: 0.0,
                cueInSec: 0.0, cueOutSec: 10.0, totalDurationSec: 10.0, delayMs: 0);

        [Fact]
        public void EmitsNoAsplitAtAll() =>
            Assert.DoesNotContain("asplit", filterGraph, StringComparison.Ordinal);
    }

    public sealed class ScenarioATinyClipClampsTheCrossfade
    {
        // Given: a 1s cue (well under 4x the 0.5s default crossfade) under a 5s total — the crossfade
        // shrinks to segmentSec/4 = 0.25s so it never exceeds the clip's own length.
        readonly string filterGraph;

        public ScenarioATinyClipClampsTheCrossfade() =>
            filterGraph = FfmpegAudioMixer.BuildBedFilterGraph(
                FilterGraphRequest(), bedGainDb: 0.0,
                cueInSec: 0.0, cueOutSec: 1.0, totalDurationSec: 5.0, delayMs: 0);

        [Fact]
        public void ShrinksTheCrossfadeToAQuarterOfTheClip() =>
            Assert.Contains("afade=t=in:st=0:d=0.25:curve=qsin", filterGraph, StringComparison.Ordinal);
    }

    static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    // ---------------------------------------------------------------------
    // INTEGRATION FACTS — a real render, measured for both a step and a level dip at the join
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheRepeatJoinIsCrossfadedNotSpliced
    {
        const double BedClipSeconds = 3.0;
        const double VoiceSeconds = 8.0;
        const double TargetLufs = -16.0;

        // Old aloop splice measured ~0.566 on this fixture; the crossfaded join measures ~6e-5 — this
        // sits well between the two (gh-#855).
        const double MaxAllowedSampleDelta = 0.1;

        // A "no overlap" mutant (copies abutting instead of crossfading) still passes the delta check
        // above — the waveform stays sample-to-sample continuous, it just dips in level at the touch
        // point — so this bounds that dip separately. Only join1 catches that mutant (−3 dB); join2's
        // window lands in its copy's full-level stretch.
        const double MaxJoinLevelDeviationDb = 1.5;

        [Fact]
        public async Task TheLoopedBedNeverStepsAcrossARepeatJoin()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var bedPath = TestMedia.CreateLinearRamp(dir, "ramp_bed.wav", BedClipSeconds);
                var voicePath = TestMedia.CreateSilenceOnly(dir, "silent_voice.wav", VoiceSeconds);
                var outputPath = Path.Combine(dir, "out.wav");

                // The voice is real silence, so FfmpegLoudnessAnalyzer reports it Measurable: false and
                // ResolveBedGainDb falls back to request.TargetLufs; pinning the bed's own catalog
                // IntegratedLufs to that same value with a 0 duck makes the applied gain exactly 0 dB,
                // so the ramp reaches the output unscaled and a splice step is unambiguous.
                var bed = new BedSpec(bedPath, CueInSec: null, CueOutSec: null, IntegratedLufs: TargetLufs);
                var request = new AudioMixRequest(
                    voicePath, bed, new AudioTags("station", "spot"),
                    BedDuckDb: 0.0, BedPadSeconds: 0.0, OutputPath: outputPath, TargetLufs: TargetLufs);

                await new FfmpegAudioMixer(new FfmpegLoudnessAnalyzer()).MixAsync(request, CancellationToken.None);

                var maxDelta = await ProbeMaxAbsSampleDeltaAsync(outputPath);
                Assert.True(maxDelta < MaxAllowedSampleDelta, $"max sample-to-sample delta {maxDelta} >= {MaxAllowedSampleDelta}");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task TheJoinWindowsHoldLevelWithinToleranceOfMidCopy()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                // White noise, not the ramp above or a pure tone: a tone's repeat taps land in a fixed
                // relative phase at every step offset, so a genuine crossfade phases them constructively
                // or destructively rather than reading as flat level. Noise decorrelates that, so a real
                // overlap stays flat and a missing one reads as a dip. Same 3s-cue/8s-total shape:
                // copies=3, joins at absolute [2.5,3.0] and [5.0,5.5].
                var bedPath = TestMedia.CreateNoise(dir, "noise_bed.wav", durationSec: BedClipSeconds);
                var voicePath = TestMedia.CreateSilenceOnly(dir, "silent_voice.wav", VoiceSeconds);
                var outputPath = Path.Combine(dir, "out.wav");

                var bed = new BedSpec(bedPath, CueInSec: null, CueOutSec: null, IntegratedLufs: TargetLufs);
                var request = new AudioMixRequest(
                    voicePath, bed, new AudioTags("station", "spot"),
                    BedDuckDb: 0.0, BedPadSeconds: 0.0, OutputPath: outputPath, TargetLufs: TargetLufs);

                await new FfmpegAudioMixer(new FfmpegLoudnessAnalyzer()).MixAsync(request, CancellationToken.None);

                var midCopyDb = await ProbeMeanVolumeDbAsync(outputPath, start: 1.0, duration: 0.3);
                var join1Db = await ProbeMeanVolumeDbAsync(outputPath, start: 2.6, duration: 0.3);
                var join2Db = await ProbeMeanVolumeDbAsync(outputPath, start: 5.1, duration: 0.3);

                var maxDeviationDb = new[] { join1Db, join2Db }.Max(db => Math.Abs(db - midCopyDb));
                Assert.True(
                    maxDeviationDb < MaxJoinLevelDeviationDb,
                    $"join/mid-copy level deviation {maxDeviationDb} dB >= {MaxJoinLevelDeviationDb} dB");
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        /// <summary>
        /// Decodes <paramref name="path"/> to raw interleaved-stereo f64le PCM and returns the largest
        /// absolute difference between consecutive samples on the same channel — a hard splice reads
        /// as one big spike; a crossfaded join stays near the format's own quantization floor.
        /// </summary>
        static async Task<double> ProbeMaxAbsSampleDeltaAsync(string path)
        {
            var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var a in new[] { "-nostats", "-hide_banner", "-loglevel", "error", "-i", path, "-f", "f64le", "-" })
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            using var raw = new MemoryStream();
            await p.StandardOutput.BaseStream.CopyToAsync(raw);
            await p.WaitForExitAsync();

            var bytes = raw.ToArray();
            const int channels = 2;
            var previous = new double?[channels];
            var maxDelta = 0.0;
            for (var offset = 0; offset + sizeof(double) <= bytes.Length; offset += sizeof(double))
            {
                var channel = (offset / sizeof(double)) % channels;
                var sample = BitConverter.ToDouble(bytes, offset);
                if (previous[channel] is double prev)
                    maxDelta = Math.Max(maxDelta, Math.Abs(sample - prev));
                previous[channel] = sample;
            }
            return maxDelta;
        }

        /// <summary>Mean volume (dB) of <paramref name="path"/> over [<paramref name="start"/>, start+<paramref name="duration"/>) via ffmpeg's own <c>volumedetect</c>.</summary>
        static async Task<double> ProbeMeanVolumeDbAsync(string path, double start, double duration)
        {
            var end = (start + duration).ToString(CultureInfo.InvariantCulture);
            var filter = $"atrim=start={start.ToString(CultureInfo.InvariantCulture)}:end={end},volumedetect";

            var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in new[] { "-nostats", "-hide_banner", "-i", path, "-af", filter, "-f", "null", "-" })
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            var stderr = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            var match = Regex.Match(stderr, @"mean_volume:\s*(-?[\d.]+)\s*dB");
            Assert.True(match.Success, $"No mean_volume reading for '{path}' [{start},{end}).");
            return double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
