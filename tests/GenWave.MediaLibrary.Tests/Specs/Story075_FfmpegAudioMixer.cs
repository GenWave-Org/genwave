// STORY-075 — Offline audio mixer renders the branded artifact
//
// BDD specification — xUnit. SPEC F27.2 / F27.4 / F27.5. IAudioMixer (Core) / FfmpegAudioMixer
// (GenWave.Loudness — sibling to the analyzers). Integration: runs ffmpeg/ffprobe against
// generated WAV fixtures, verifying every clause by ffprobe'ing the real rendered artifact
// (duration, onset, embedded tags) rather than inspecting the mixer's internals.
//
// Voice and bed fixtures use distinct sine frequencies (VoiceFrequencyHz / BedFrequencyHz) so a
// bandpass filter can isolate either signal from the mixed output for onset/level/gap checks.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GenWave.Core.Domain;
using GenWave.Loudness;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureFfmpegAudioMixer
{
    const string StationArtist = "GWAV 108.8";
    const string DefaultTitle = "Please Stand By";
    const int VoiceFrequencyHz = 800;
    const int BedFrequencyHz = 200;

    // Well below any ducked-bed level we expect to measure (a −12 dB duck of a full-scale tone still
    // reads far above this), so only a genuine gap (true silence) trips these checks — not "quiet".
    const double SilenceNoiseFloorDb = -50.0;
    const double MinSilenceDurationSec = 0.2;

    // ---------------------------------------------------------------------
    // HAPPY PATH — voice-only render embeds tags
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioVoiceOnlyRenderEmbedsTags
    {
        sealed record MixOutcome(bool OutputExists, string? Artist, string? Title, string? ArtistViaTagLibPerformers);

        static async Task<MixOutcome> ArrangeVoiceOnlyMixAsync()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var voicePath = TestMedia.CreateTone(dir, "voice.wav", seconds: 2.0, frequency: VoiceFrequencyHz);
                var outputPath = Path.Combine(dir, "out.wav");

                var request = new AudioMixRequest(
                    VoicePath: voicePath,
                    Bed: null,
                    Tags: new AudioTags(StationArtist, DefaultTitle),
                    BedDuckDb: -12.0,
                    BedPadSeconds: 1.5,
                    OutputPath: outputPath);

                await new FfmpegAudioMixer().MixAsync(request, CancellationToken.None);

                var exists = File.Exists(outputPath);
                var (artist, title) = exists ? await ProbeTagsAsync(outputPath) : (null, null);
                var performerArtist = exists ? ReadArtistViaTagLibPerformers(outputPath) : null;
                return new MixOutcome(exists, artist, title, performerArtist);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task OutputFileExistsAtTheRequestedPath()
        {
            // AC1 — MixAsync(voice, bed: null, tags, outPath) creates outPath
            var outcome = await ArrangeVoiceOnlyMixAsync();
            Assert.True(outcome.OutputExists);
        }

        [Fact]
        public async Task EmbeddedArtistTagCarriesTheStationName()
        {
            // AC1 — ffprobe format tags: artist == "GWAV 108.8" (RIFF INFO)
            var outcome = await ArrangeVoiceOnlyMixAsync();
            Assert.Equal(StationArtist, outcome.Artist);
        }

        [Fact]
        public async Task EmbeddedTitleTagCarriesPleaseStandBy()
        {
            // AC1 — ffprobe format tags: title == "Please Stand By"
            var outcome = await ArrangeVoiceOnlyMixAsync();
            Assert.Equal(DefaultTitle, outcome.Title);
        }

        [Fact]
        public async Task EmbeddedArtistIsReadableTheWayTheEnricherReadsIt()
        {
            // SPEC F27.2 round-trip, read the way the media library's enricher reads it: TagLib's
            // Tag.Performers (ID3v2 TPE1), not ffprobe's generic format tags. ffmpeg's -metadata
            // artist= only reaches RIFF INFO "IART", which TagLibSharp surfaces as AlbumArtists, not
            // Performers — a mismatch that let a prior regression pass ffprobe-only assertions while
            // a tags-only re-enrich still left artist NULL.
            var outcome = await ArrangeVoiceOnlyMixAsync();
            Assert.Equal(StationArtist, outcome.ArtistViaTagLibPerformers);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — bed padded, looped/trimmed, ducked
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioBedIsPaddedAndDuckedUnderTheVoice
    {
        const double VoiceDurationSec = 5.0;
        const double PadSeconds = 1.5;
        const double DuckDb = -12.0;

        sealed record PaddedDuckedOutcome(double OutputDurationSec, double VoiceOnsetSec, double BedLevelDeltaDb);

        static async Task<PaddedDuckedOutcome> ArrangePaddedDuckedMixAsync()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var voicePath = TestMedia.CreateTone(dir, "voice.wav", seconds: VoiceDurationSec, frequency: VoiceFrequencyHz);
                var bedPath = TestMedia.CreateTone(dir, "bed.wav", seconds: 3.0, frequency: BedFrequencyHz);
                var outputPath = Path.Combine(dir, "out.wav");

                // Reference level: the bed's own loudness, unducked and unmixed.
                var soloBedLufs = await ProbeIntegratedLufsAsync(bedPath, bandFrequencyHz: null);

                var request = new AudioMixRequest(
                    voicePath, new BedSpec(bedPath, CueInSec: null, CueOutSec: null),
                    new AudioTags(StationArtist, DefaultTitle),
                    DuckDb, PadSeconds, outputPath);
                await new FfmpegAudioMixer().MixAsync(request, CancellationToken.None);

                var duration = await ProbeDurationSecondsAsync(outputPath);
                var onset = await ProbeFirstSilenceEndSecondsAsync(outputPath, VoiceFrequencyHz);
                var bedLevelInMix = await ProbeIntegratedLufsAsync(outputPath, BedFrequencyHz);

                return new PaddedDuckedOutcome(duration, onset, bedLevelInMix - soloBedLufs);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task OutputDurationIsVoicePlusTwiceThePad()
        {
            // AC2 — duration ≈ D + 2×BedPadSeconds (ffprobe, tolerance ±0.5s)
            var outcome = await ArrangePaddedDuckedMixAsync();
            var expected = VoiceDurationSec + (2 * PadSeconds);
            Assert.InRange(outcome.OutputDurationSec, expected - 0.5, expected + 0.5);
        }

        [Fact]
        public async Task VoiceOnsetFollowsTheLeadInPad()
        {
            // AC2 — first voice-band audio ≥ ~BedPadSeconds from file start
            var outcome = await ArrangePaddedDuckedMixAsync();
            Assert.InRange(outcome.VoiceOnsetSec, PadSeconds - 0.2, PadSeconds + 0.5);
        }

        [Fact]
        public async Task BedLevelUnderTheVoiceIsAttenuatedByBedDuckDb()
        {
            // AC2 — bed segment loudness under voice ≈ solo level + BedDuckDb (−12 dB)
            var outcome = await ArrangePaddedDuckedMixAsync();
            Assert.InRange(outcome.BedLevelDeltaDb, DuckDb - 3.0, DuckDb + 3.0);
        }

        [Fact]
        public async Task AShortBedLoopsToCoverTheFullOutput()
        {
            // AC3 — 3s bed under a 20s voice: no silent bed gap in the output
            var dir = TestMedia.NewTempDir();
            try
            {
                var voicePath = TestMedia.CreateTone(dir, "voice20.wav", seconds: 20.0, frequency: VoiceFrequencyHz);
                var bedPath = TestMedia.CreateTone(dir, "bed3.wav", seconds: 3.0, frequency: BedFrequencyHz);
                var outputPath = Path.Combine(dir, "out.wav");

                var request = new AudioMixRequest(
                    voicePath, new BedSpec(bedPath, CueInSec: null, CueOutSec: null),
                    new AudioTags(StationArtist, DefaultTitle),
                    DuckDb, PadSeconds, outputPath);
                await new FfmpegAudioMixer().MixAsync(request, CancellationToken.None);

                var gapCount = await ProbeBandSilenceStartCountAsync(outputPath, BedFrequencyHz);
                Assert.Equal(0, gapCount);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task ALongBedIsTrimmedToTheOutputLength()
        {
            // AC3 — 60s bed under a 5s voice: output duration ≈ 5s + 2×pad
            var dir = TestMedia.NewTempDir();
            try
            {
                const double voiceDurationSec = 5.0;
                var voicePath = TestMedia.CreateTone(dir, "voice5.wav", seconds: voiceDurationSec, frequency: VoiceFrequencyHz);
                var bedPath = TestMedia.CreateTone(dir, "bed60.wav", seconds: 60.0, frequency: BedFrequencyHz);
                var outputPath = Path.Combine(dir, "out.wav");

                var request = new AudioMixRequest(
                    voicePath, new BedSpec(bedPath, CueInSec: null, CueOutSec: null),
                    new AudioTags(StationArtist, DefaultTitle),
                    DuckDb, PadSeconds, outputPath);
                await new FfmpegAudioMixer().MixAsync(request, CancellationToken.None);

                var duration = await ProbeDurationSecondsAsync(outputPath);
                var expected = voiceDurationSec + (2 * PadSeconds);
                Assert.InRange(duration, expected - 0.5, expected + 0.5);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task BedCuePointsBoundTheBedAudioUsed()
        {
            // AC4 — a bed with cue_in/cue_out contributes only its cue-trimmed audio
            var dir = TestMedia.NewTempDir();
            try
            {
                var voicePath = TestMedia.CreateTone(dir, "voice.wav", seconds: VoiceDurationSec, frequency: VoiceFrequencyHz);
                var bedPath = TestMedia.CreateSilenceToneSilence(
                    dir, "bed_cue.wav",
                    leadingSilenceSec: 2.0, toneSec: 5.0, trailingSilenceSec: 2.0, frequency: BedFrequencyHz);
                var outputPath = Path.Combine(dir, "out.wav");

                var request = new AudioMixRequest(
                    voicePath, new BedSpec(bedPath, CueInSec: 2.0, CueOutSec: 7.0),
                    new AudioTags(StationArtist, DefaultTitle),
                    DuckDb, PadSeconds, outputPath);
                await new FfmpegAudioMixer().MixAsync(request, CancellationToken.None);

                // If the silent flanks ever entered the loop, they would surface as a silence gap.
                var gapCount = await ProbeBandSilenceStartCountAsync(outputPath, BedFrequencyHz);
                Assert.Equal(0, gapCount);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — failure leaves no partial file
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioFfmpegFailureLeavesNoPartialOutput
    {
        sealed record FailedMixOutcome(Exception? Thrown, bool OutputExists);

        static async Task<FailedMixOutcome> ArrangeFailedMixAsync()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                var voicePath = TestMedia.CreateTone(dir, "voice.wav", seconds: 2.0, frequency: VoiceFrequencyHz);
                var outputPath = Path.Combine(dir, "out.wav");
                var request = new AudioMixRequest(
                    voicePath,
                    new BedSpec(Path.Combine(dir, "does-not-exist-genwave-story075-bed.wav"), CueInSec: null, CueOutSec: null),
                    new AudioTags(StationArtist, DefaultTitle),
                    BedDuckDb: -12.0,
                    BedPadSeconds: 1.5,
                    OutputPath: outputPath);

                var thrown = await Record.ExceptionAsync(
                    () => new FfmpegAudioMixer().MixAsync(request, CancellationToken.None));

                return new FailedMixOutcome(thrown, File.Exists(outputPath));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task AnUnreadableBedPathFailsTheMix()
        {
            // AC5 — MixAsync throws / returns failure per house convention
            var outcome = await ArrangeFailedMixAsync();
            Assert.IsType<InvalidOperationException>(outcome.Thrown);
        }

        [Fact]
        public async Task NoOutputFileRemainsAfterAFailedMix()
        {
            // AC5 — outPath does not exist after the failure
            var outcome = await ArrangeFailedMixAsync();
            Assert.False(outcome.OutputExists);
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioCancellationLeavesNoLiveProcessOrPartialOutput
    {
        sealed record CancelledMixOutcome(Exception? Thrown, bool OutputExists);

        // A pre-cancelled token throws before the awaited read/wait ever observes real I/O, so the
        // child ffmpeg is still starting/running when MixAsync's cleanup fires — exactly the race the
        // fix closes: the process must be killed and confirmed dead before DeletePartialOutput runs,
        // or a still-running ffmpeg could finish writing the (complete, tagged) output afterwards.
        static async Task<CancelledMixOutcome> ArrangeCancelledMixAsync()
        {
            var dir = TestMedia.NewTempDir();
            try
            {
                // A longer voice clip gives ffmpeg real work to still be doing at cancellation time.
                var voicePath = TestMedia.CreateTone(dir, "voice.wav", seconds: 20.0, frequency: VoiceFrequencyHz);
                var outputPath = Path.Combine(dir, "out.wav");
                var request = new AudioMixRequest(
                    VoicePath: voicePath,
                    Bed: null,
                    Tags: new AudioTags(StationArtist, DefaultTitle),
                    BedDuckDb: -12.0,
                    BedPadSeconds: 1.5,
                    OutputPath: outputPath);

                using var cts = new CancellationTokenSource();
                cts.Cancel();

                var thrown = await Record.ExceptionAsync(
                    () => new FfmpegAudioMixer().MixAsync(request, cts.Token));

                return new CancelledMixOutcome(thrown, File.Exists(outputPath));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task CancellingTheMixThrowsOperationCanceled()
        {
            var outcome = await ArrangeCancelledMixAsync();
            Assert.IsAssignableFrom<OperationCanceledException>(outcome.Thrown);
        }

        [Fact]
        public async Task NoOutputFileRemainsAfterCancellation()
        {
            // The wait-for-real-exit before cleanup makes this deterministic: no lingering ffmpeg
            // can still be writing the output by the time MixAsync returns.
            var outcome = await ArrangeCancelledMixAsync();
            Assert.False(outcome.OutputExists);
        }
    }

    // ---------------------------------------------------------------------
    // ffprobe/ffmpeg verification helpers — black-box: probe the rendered artifact, never the
    // mixer's internals.
    // ---------------------------------------------------------------------

    static async Task<double> ProbeDurationSecondsAsync(string path)
    {
        var stdout = await RunProbeAsync(
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            path);
        return double.Parse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static async Task<(string? Artist, string? Title)> ProbeTagsAsync(string path)
    {
        var stdout = await RunProbeAsync(
            "-v", "error",
            "-show_entries", "format_tags=artist,title",
            "-of", "json",
            path);

        using var doc = JsonDocument.Parse(stdout);
        var format = doc.RootElement.GetProperty("format");
        if (!format.TryGetProperty("tags", out var tags))
            return (null, null);

        var artist = tags.TryGetProperty("artist", out var a) ? a.GetString() : null;
        var title = tags.TryGetProperty("title", out var t) ? t.GetString() : null;
        return (artist, title);
    }

    /// <summary>
    /// Reads the artist the way the media library's enricher does: TagLib's <c>Tag.JoinedPerformers</c>
    /// (ID3v2 TPE1 / RIFF INFO "ISTR"), not ffprobe's generic format tags.
    /// </summary>
    static string? ReadArtistViaTagLibPerformers(string path)
    {
        using var file = TagLib.File.Create(path);
        return string.IsNullOrWhiteSpace(file.Tag.JoinedPerformers) ? null : file.Tag.JoinedPerformers;
    }

    /// <summary>First silence_end time (seconds) in the isolated <paramref name="bandFrequencyHz"/> band.</summary>
    static async Task<double> ProbeFirstSilenceEndSecondsAsync(string path, int bandFrequencyHz)
    {
        var stderr = await RunFfmpegFilterAsync(path, SilenceDetectFilter(bandFrequencyHz));
        var match = Regex.Match(stderr, @"silence_end:\s*(-?[\d.]+)");
        Assert.True(match.Success, $"No silence_end event in the {bandFrequencyHz} Hz band for '{path}'.");
        return double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Count of silence_start events in the isolated <paramref name="bandFrequencyHz"/> band — 0 means no gap.</summary>
    static async Task<int> ProbeBandSilenceStartCountAsync(string path, int bandFrequencyHz)
    {
        var stderr = await RunFfmpegFilterAsync(path, SilenceDetectFilter(bandFrequencyHz));
        return Regex.Matches(stderr, "silence_start:").Count;
    }

    /// <summary>Integrated loudness (LUFS) of <paramref name="path"/>, optionally isolated to one frequency band.</summary>
    static async Task<double> ProbeIntegratedLufsAsync(string path, int? bandFrequencyHz)
    {
        var filter = bandFrequencyHz is { } freq
            ? $"bandpass=f={freq}:width_type=h:w=40,ebur128=peak=true"
            : "ebur128=peak=true";

        var stderr = await RunFfmpegFilterAsync(path, filter);
        var summaryIdx = stderr.LastIndexOf("Summary:", StringComparison.Ordinal);
        Assert.True(summaryIdx >= 0, $"No ebur128 summary in ffmpeg output for '{path}'.");

        var match = Regex.Match(stderr[summaryIdx..], @"I:\s*(-?[\d.]+)\s*LUFS");
        Assert.True(match.Success, $"No integrated loudness reading for '{path}'.");
        return double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    static string SilenceDetectFilter(int bandFrequencyHz) =>
        $"bandpass=f={bandFrequencyHz}:width_type=h:w=40," +
        $"silencedetect=noise={SilenceNoiseFloorDb.ToString(CultureInfo.InvariantCulture)}dB:" +
        $"duration={MinSilenceDurationSec.ToString(CultureInfo.InvariantCulture)}";

    static Task<string> RunFfmpegFilterAsync(string path, string audioFilter) =>
        RunAndCaptureStderrAsync("ffmpeg", "-nostats", "-hide_banner", "-i", path, "-af", audioFilter, "-f", "null", "-");

    static async Task<string> RunProbeAsync(params string[] args)
    {
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffprobe.");
        var stdout = await p.StandardOutput.ReadToEndAsync();
        await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return stdout;
    }

    static async Task<string> RunAndCaptureStderrAsync(string fileName, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return stderr;
    }
}
