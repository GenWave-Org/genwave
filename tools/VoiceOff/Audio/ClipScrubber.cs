using GenWave.Loudness;

namespace GenWave.VoiceOff.Audio;

/// <summary>
/// The fixed format and the metadata strip for every clip the tool keeps (SPEC F214.4, F214.9): 24 kHz
/// mono s16le wav with no container or tag metadata. Used by <see cref="LoudnessMatch"/> for its second
/// pass and reusable to re-strip a clip on rename.
/// </summary>
internal static class ClipScrubber
{
    internal const int SampleRate = 24000;

    /// <summary>
    /// Converts to the final format inside the filter graph, ahead of any loudness filter: a stereo measurement is
    /// up to 3 LU hotter than the mono clip it becomes, so measuring first and downmixing after lands the clip under target.
    /// </summary>
    internal static readonly string FormatPreFilter =
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"aformat=sample_rates={SampleRate}:channel_layouts=mono,");

    /// <summary>An ffmpeg input argument that is always a local file: absolute (so a leading '-' is not an option) and <c>file:</c>-prefixed (so no URL protocol is honoured).</summary>
    internal static string InputArg(string path) => "file:" + Path.GetFullPath(path);

    /// <summary>The ffmpeg argv for the loudness-measuring first pass: reads <paramref name="input"/> through <paramref name="audioFilter"/> and discards the audio.</summary>
    internal static IReadOnlyList<string> MeasureArgs(string input, string audioFilter) =>
        ["-nostdin", "-hide_banner", "-i", InputArg(input), "-af", audioFilter, "-f", "null", "-"];

    /// <summary>The ffmpeg argv that writes <paramref name="output"/> as a scrubbed wav, optionally through <paramref name="audioFilter"/>.</summary>
    internal static IReadOnlyList<string> BuildArgs(string input, string output, string? audioFilter = null)
    {
        List<string> args = ["-nostdin", "-hide_banner", "-y", "-i", InputArg(input)];
        if (audioFilter is not null)
        {
            args.AddRange(["-af", audioFilter]);
        }

        args.AddRange([
            "-ar", SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-ac", "1",
            "-c:a", "pcm_s16le",
            "-map_metadata", "-1",
            "-fflags", "+bitexact",
            "-flags:a", "+bitexact", // Defensive: encoder bitexact; no observable effect on pcm_s16le today.
            "-f", "wav",
            Path.GetFullPath(output),
        ]);
        return args;
    }

    /// <summary>The exit code and stderr; never throws on a non-zero exit.</summary>
    internal static async Task<FfmpegRunResult> ScrubAsync(string input, string output, string? audioFilter, CancellationToken ct) =>
        await FfmpegProcess.RunFfmpegCapturingStderrAsync(BuildArgs(input, output, audioFilter), ct);
}
