namespace GenWave.Tts;

using System.Globalization;
using GenWave.Loudness;
using Microsoft.Extensions.Logging;

/// <summary>
/// SPEC F219.31: an engine whose profile has no <c>speed</c> (chatterbox, piper) ignores the speaker's pace, so the
/// rendered file is time-stretched by it instead (ffmpeg <c>atempo</c>, pitch kept). It runs on the engine's own output,
/// before anything measures or caches it, so the loudness and cue figures describe the audio that airs.
///
/// <para>
/// <b>Failure policy: fail open.</b> Pace is a nicety and a lost line is worse than a line at the wrong speed, so a failed
/// stretch (ffmpeg missing, a non-zero exit, a file it cannot read) logs a warning and keeps the unstretched render. A
/// caller cancel is not a failure and propagates.
/// </para>
/// </summary>
public sealed class PaceStretch(
    ILogger logger,
    Func<IReadOnlyList<string>, CancellationToken, Task>? runFfmpeg = null)
{
    /// <summary>The slowest stretch <c>atempo</c> is asked for: below it speech turns audibly robotic.</summary>
    public const double MinTempo = 0.8;

    /// <summary>The fastest stretch <c>atempo</c> is asked for: the same pace range F219.31 clamps to (0.8–1.25).</summary>
    public const double MaxTempo = 1.25;

    readonly Func<IReadOnlyList<string>, CancellationToken, Task> run = runFfmpeg ?? FfmpegProcess.RunFfmpegAsync;

    /// <summary>
    /// The <c>atempo</c> factor for <paramref name="pace"/> on <paramref name="profile"/>, or null when no step runs: the
    /// engine applies speed itself, the pace is unusable, or it is 1.0. The pace is clamped to
    /// [<see cref="MinTempo"/>, <see cref="MaxTempo"/>] here, so nothing else reaches the ffmpeg argument.
    /// </summary>
    public static double? TempoFor(VoiceEngineProfile profile, double pace)
    {
        if (profile.Speed || TtsPace.IsDegenerate(pace))
            return null;

        var tempo = Math.Clamp(pace, MinTempo, MaxTempo);
        return tempo == TtsPace.EngineDefault ? null : tempo;
    }

    /// <summary>The ffmpeg argument list (argv only, never a shell string) that writes <paramref name="output"/> stretched by <paramref name="tempo"/>.</summary>
    public static IReadOnlyList<string> BuildArguments(string input, string output, double tempo) =>
    [
        "-hide_banner", "-nostdin", "-y",
        "-i", input,
        "-filter:a", "atempo=" + tempo.ToString("0.######", CultureInfo.InvariantCulture),
        output,
    ];

    /// <summary>Stretches the file at <paramref name="path"/> in place when <paramref name="pace"/> calls for it; returns the same path.</summary>
    public async Task<string> ApplyAsync(VoiceEngineProfile profile, double pace, string path, CancellationToken ct)
    {
        if (TempoFor(profile, pace) is not { } tempo)
            return path;

        // Absolute, so a CacheRoot that starts with '-' can never read as an ffmpeg option.
        var full = Path.GetFullPath(path);
        var stretched = Path.Combine(
            Path.GetDirectoryName(full) ?? string.Empty,
            Path.GetFileNameWithoutExtension(full) + ".stretched" + Path.GetExtension(full));
        try
        {
            await run(BuildArguments(full, stretched, tempo), ct);
            File.Move(stretched, full, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            DeleteIfExists(stretched);
            throw;
        }
        catch (Exception ex)
        {
            // Fail open (see the class remarks): the unstretched render still airs.
            DeleteIfExists(stretched);
            logger.LogWarning(
                ex, "Pace stretch of {Tempo} failed on engine {Engine}; keeping the unstretched render (SPEC F219.31)",
                tempo, profile.Name);
        }

        return path;
    }

    static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort scratch cleanup: the temp name is unique to this render, so an orphan is inert.
        }
        catch (UnauthorizedAccessException)
        {
            // As above.
        }
    }
}
