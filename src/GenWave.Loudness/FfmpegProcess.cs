using System.Diagnostics;
using System.Globalization;

namespace GenWave.Loudness;

/// <summary>
/// Shared argv-only ffmpeg/ffprobe process plumbing (SPEC F127.6, T284 review consolidation) — the
/// identical "start with <see cref="ProcessStartInfo.ArgumentList"/>, read stderr, wait, kill-on-cancel"
/// shape <see cref="FfmpegAudioMixer"/> and <c>GenWave.Tts.CrosstalkAssembler</c> each need around the
/// SAME two child processes (ffmpeg itself, and ffprobe for a container duration read). Lives here,
/// not duplicated into either caller — the prior shape (T284 round 1) had the identical ~35 lines of
/// process/cancellation plumbing copy-pasted verbatim into <c>CrosstalkAssembler</c>, one project over
/// from the sibling it was copied from.
///
/// No <see cref="System.Net.Http.HttpClient"/>, no network origin, anywhere in this type — L3's
/// HttpClient-seam law (ARCHITECTURE.md "Architecture governance") has nothing to say about a type
/// that only ever shells out to a local child process.
/// </summary>
public static class FfmpegProcess
{
    /// <summary>
    /// Runs ffmpeg with <paramref name="args"/> (argv-only — every value travels via
    /// <see cref="ProcessStartInfo.ArgumentList"/>, never a shell-interpolated string), throwing
    /// <see cref="InvalidOperationException"/> on a non-zero exit (stderr captured into the message).
    /// On cancellation, kills the child and waits (uncancellably) for the OS to confirm it is
    /// actually gone before this rethrows — see <see cref="KillAndWaitForExitAsync"/>'s own remarks.
    /// </summary>
    public static async Task RunFfmpegAsync(IReadOnlyList<string> args, CancellationToken ct)
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
            await KillAndWaitForExitAsync(p);
            throw;
        }

        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {p.ExitCode}: {stderr}");
    }

    /// <summary>Probes the container duration of <paramref name="path"/> via ffprobe, argv-only.</summary>
    public static async Task<double> ProbeDurationSecondsAsync(string path, CancellationToken ct)
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
                path,
            },
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

    /// <summary>
    /// Terminates <paramref name="p"/> and any children, then waits (uncancellably) for the OS to
    /// confirm it has actually exited. A cancelled awaiter does not stop the underlying process —
    /// without this, a killed run could leak a still-running child that keeps writing an output file
    /// after the caller's own cleanup already ran.
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
}
