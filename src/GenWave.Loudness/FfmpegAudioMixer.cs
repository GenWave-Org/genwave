using System.Diagnostics;
using System.Globalization;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.Loudness;

/// <summary>
/// Renders the final safe-segment artifact in exactly one ffmpeg invocation (SPEC F27.2 / F27.4 /
/// F27.5): voice-only is re-muxed with embedded RIFF INFO tags; with a bed, the bed is cue-trimmed,
/// looped or trimmed to cover the voice plus lead-in/tail-out pad, attenuated relative to the voice,
/// and mixed in after the voice's lead-in delay. One invocation in every path keeps audio rendering
/// in exactly one place.
///
/// Tag embedding is two-step (SPEC F27.2): ffmpeg's <c>-metadata</c> writes the RIFF INFO artist/title
/// chunks that generic tools (ffprobe et al.) read, but ffmpeg's wav muxer has no way to write a chunk
/// TagLibSharp reads as <c>Tag.Performers</c> — the media library's tags re-enrich path reads exactly
/// that. So once ffmpeg produces the file, a short TagLibSharp pass stamps the artist onto
/// <c>Tag.Performers</c> (a real ID3v2 TPE1 frame) so a <c>fields=tags</c> re-enrich round-trips the
/// brand instead of leaving artist NULL.
///
/// Config-free by design: every value arrives via <see cref="AudioMixRequest"/>; the mixer never
/// reads <c>Station:Safe:*</c> or the database itself (callers resolve those).
///
/// On failure (missing/unreadable input, ffmpeg non-zero exit) this throws
/// <see cref="InvalidOperationException"/> and deletes any partially-written output file.
/// </summary>
public sealed class FfmpegAudioMixer : IAudioMixer
{
    // The bed branch is resampled to this rate before looping so the aloop buffer size (computed in
    // samples) is deterministic regardless of the bed file's native sample rate.
    const int BedProcessingSampleRate = 44100;

    public async Task MixAsync(AudioMixRequest request, CancellationToken ct)
    {
        try
        {
            if (request.Bed is null)
                await RunVoiceOnlyAsync(request, ct);
            else
                await RunWithBedAsync(request, request.Bed, ct);

            EmbedPerformerTag(request.OutputPath, request.Tags.Artist);
        }
        catch (Exception)
        {
            DeletePartialOutput(request.OutputPath);
            throw;
        }
    }

    /// <summary>Re-muxes the voice clip alone into <see cref="AudioMixRequest.OutputPath"/> with tags embedded.</summary>
    static async Task RunVoiceOnlyAsync(AudioMixRequest request, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-nostdin", "-y", "-hide_banner", "-loglevel", "error",
            "-i", request.VoicePath,
            "-map", "0:a",
            "-c:a", "pcm_s16le",
        };
        AddTagArgs(args, request.Tags);
        args.Add("--");   // end-of-options: OutputPath may start with '-' once operator input drives it
        args.Add(request.OutputPath);

        await RunFfmpegAsync(args, ct);
    }

    /// <summary>
    /// Mixes the cue-trimmed, looped/trimmed, ducked bed under the voice (delayed by the lead-in pad)
    /// in a single filter_complex pass.
    /// </summary>
    static async Task RunWithBedAsync(AudioMixRequest request, BedSpec bed, CancellationToken ct)
    {
        var voiceDurationSec = await ProbeDurationSecondsAsync(request.VoicePath, ct);
        var totalDurationSec = voiceDurationSec + (2 * request.BedPadSeconds);

        var cueInSec = bed.CueInSec ?? 0.0;
        var cueOutSec = bed.CueOutSec ?? await ProbeDurationSecondsAsync(bed.Path, ct);
        var bedSegmentDurationSec = cueOutSec - cueInSec;
        if (bedSegmentDurationSec <= 0.0)
            throw new InvalidOperationException(
                $"Bed cue points for '{bed.Path}' produce a non-positive segment " +
                $"({cueInSec}s to {cueOutSec}s).");

        // Buffer just enough samples to hold the cue-trimmed bed segment: a short bed loops over
        // this whole buffer; a long bed never reaches the buffer's end before the final atrim cuts it.
        var loopBufferSamples = (long)Math.Round(
            bedSegmentDurationSec * BedProcessingSampleRate, MidpointRounding.AwayFromZero);
        var delayMs = (long)Math.Round(request.BedPadSeconds * 1000.0, MidpointRounding.AwayFromZero);

        var filter =
            $"[1:a]atrim=start={Fmt(cueInSec)}:end={Fmt(cueOutSec)},asetpts=PTS-STARTPTS," +
            $"aformat=sample_rates={BedProcessingSampleRate}:channel_layouts=stereo," +
            $"aloop=loop=-1:size={loopBufferSamples}," +
            $"atrim=start=0:end={Fmt(totalDurationSec)},asetpts=PTS-STARTPTS," +
            $"volume={Fmt(request.BedDuckDb)}dB[bed];" +
            $"[0:a]aformat=sample_rates={BedProcessingSampleRate}:channel_layouts=stereo," +
            $"adelay=delays={delayMs}:all=1[voice];" +
            "[bed][voice]amix=inputs=2:duration=first:dropout_transition=0:normalize=0[out]";

        var args = new List<string>
        {
            "-nostdin", "-y", "-hide_banner", "-loglevel", "error",
            "-i", request.VoicePath,
            "-i", bed.Path,
            "-filter_complex", filter,
            "-map", "[out]",
            "-c:a", "pcm_s16le",
        };
        AddTagArgs(args, request.Tags);
        args.Add("--");   // end-of-options: OutputPath may start with '-' once operator input drives it
        args.Add(request.OutputPath);

        await RunFfmpegAsync(args, ct);
    }

    static void AddTagArgs(List<string> args, AudioTags tags)
    {
        args.Add("-metadata");
        args.Add($"artist={tags.Artist}");
        args.Add("-metadata");
        args.Add($"title={tags.Title}");
    }

    /// <summary>
    /// Stamps <paramref name="artist"/> onto the rendered wav's <c>Tag.Performers</c> (SPEC F27.2).
    /// ffmpeg's <c>-metadata artist=</c> only reaches the RIFF INFO "IART" chunk, which TagLibSharp
    /// surfaces as <c>Tag.AlbumArtists</c>, not <c>Tag.Performers</c> — the property the media
    /// library's enricher reads. Setting <c>Tag.Performers</c> here writes a real ID3v2 TPE1 frame
    /// (plus the RIFF "ISTR" chunk) so a tags-only re-enrich rehydrates the artist instead of leaving
    /// it NULL. MovieId/DivX are container tag types TagLibSharp always instantiates for a wav on open
    /// (irrelevant to an audio-only file) — removed before saving so they never reach disk.
    /// </summary>
    static void EmbedPerformerTag(string outputPath, string artist)
    {
        using var file = TagLib.File.Create(outputPath);
        file.Tag.Performers = new[] { artist };
        file.RemoveTags(TagLib.TagTypes.MovieId | TagLib.TagTypes.DivX);
        file.Save();
    }

    /// <summary>Probes the container duration of <paramref name="path"/> via ffprobe.</summary>
    static async Task<double> ProbeDurationSecondsAsync(string path, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList =
            {
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                path
            }
        }) ?? throw new InvalidOperationException("Failed to start ffprobe.");

        string stdout;
        string stderr;
        try
        {
            var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
            stderr = await p.StandardError.ReadToEndAsync(ct);
            stdout = await stdoutTask;
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Cancelling the awaits does not stop the child process — kill it and wait for the OS
            // to confirm it is actually gone before the caller's cleanup runs, or a still-running
            // ffprobe/ffmpeg could keep writing after DeletePartialOutput already ran.
            await KillAndWaitForExitAsync(p);
            throw;
        }

        if (p.ExitCode != 0 ||
            !double.TryParse(stdout.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
        {
            throw new InvalidOperationException($"ffprobe failed to determine duration for '{path}': {stderr}");
        }

        return duration;
    }

    static async Task RunFfmpegAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        string stderr;
        try
        {
            stderr = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Same reasoning as ProbeDurationSecondsAsync: confirm the process is dead before we
            // return control to MixAsync, which deletes any partial output on the way out.
            await KillAndWaitForExitAsync(p);
            throw;
        }

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {p.ExitCode}: {stderr}");
    }

    /// <summary>
    /// Terminates <paramref name="p"/> and any children, then waits (uncancellably) for the OS to
    /// confirm it has actually exited. A cancelled awaiter does not stop the underlying process —
    /// without this, a killed mix could leak a running ffmpeg that keeps writing the output file
    /// after the caller already deleted it.
    /// </summary>
    static async Task KillAndWaitForExitAsync(Process p)
    {
        try
        {
            if (!p.HasExited)
                p.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and Kill() — nothing left to terminate.
        }

        await p.WaitForExitAsync(CancellationToken.None);
    }

    /// <summary>Best-effort cleanup of a partially-written output file after a failed mix.</summary>
    static void DeletePartialOutput(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
        catch (IOException)
        {
            // The original failure is what the caller needs to see; a locked/undeletable partial
            // file is a secondary concern best-effort cleaned up here and not worth masking it.
        }
    }

    static string Fmt(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
