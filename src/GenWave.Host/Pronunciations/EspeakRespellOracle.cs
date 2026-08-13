using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GenWave.Host.Pronunciations;

/// <summary>
/// <see cref="IRespellOracle"/> over the espeak-ng binary vendored in the api image's runtime stage
/// (SPEC F126.2, STORY-324, PLAN T278 — see the Dockerfile's own remarks for the apt-get pin).
/// Invokes <c>espeak-ng -q --ipa -v en-us -- &lt;respelling&gt;</c> — <c>-q</c> suppresses audio
/// output (no sound device is ever opened), <c>--ipa</c> writes International Phonetic Alphabet
/// phoneme mnemonics to stdout instead of speaking, <c>-v en-us</c> pins the voice/language so the
/// mapping is deterministic regardless of any locale the container happens to boot with. Verified
/// against the real Debian/Ubuntu <c>espeak-ng</c> 1.5x package: <c>espeak-ng -q --ipa -v en-us --
/// "muh-KLOWD"</c> prints <c>mˈʌklˈoʊd</c> and exits 0.
///
/// <para>
/// <b>Argv-only is NOT enough — argument injection (CWE-88), review round 2 finding F1.</b>
/// <see cref="ProcessStartInfo.ArgumentList"/> with <see cref="ProcessStartInfo.UseShellExecute"/>
/// <see langword="false"/> closes the SHELL-composition class of bug, but espeak-ng parses its OWN
/// argv the way every getopt-style CLI does: a respelling that happens to start with <c>-</c> is not
/// data to it, it is another OPTION. Proven in-container against the real binary: a respelling of
/// <c>-f/root/appsettings.json</c> makes espeak-ng READ AND SPEAK THAT FILE's contents instead of
/// the literal text (an arbitrary-file-read primitive — the derived "IPA" is the file's content,
/// handed straight back in the 200 body); a respelling of <c>--phonout=/some/path</c> makes it WRITE
/// (truncating) that path instead of printing to stdout (an arbitrary-file-write/truncate primitive
/// — a Data Protection key ring at a predictable path is destroyed, breaking cookie validation).
/// The fix is the POSIX end-of-options marker: <c>--</c> is appended as its own
/// <see cref="ProcessStartInfo.ArgumentList"/> entry BEFORE the respelling
/// (<see cref="BuildProcessStartInfo"/>) — getopt-family parsers (espeak-ng's included) stop
/// interpreting anything after a bare <c>--</c> as an option, no matter what it starts with.
/// Re-verified in-container after the fix: <c>-f/etc/hostname</c> now phonemizes as the LITERAL
/// string "dash eff slash etc slash hostname", never touching the file; <c>--phonout=...</c> now
/// phonemizes as literal text and the target file is untouched. <see cref="PronunciationDerivationController"/>
/// additionally rejects a leading <c>-</c> at the HTTP boundary as defence-in-depth — belt and
/// braces, not the actual fix (the <c>--</c> marker is).
/// </para>
///
/// <para>
/// <b>Argv-only, never a shell string</b> (security-api's <c>Process.Start</c> rule): every argument
/// — including the operator-authored <paramref name="respelling"/> a caller passes to
/// <see cref="DeriveAsync"/> — is appended to <see cref="ProcessStartInfo.ArgumentList"/> with
/// <see cref="ProcessStartInfo.UseShellExecute"/> <see langword="false"/>. There is no
/// string-concatenation/composition step for this class to get wrong.
/// </para>
///
/// <para>
/// <b>Output is normalized, not just trimmed (review round 2 finding F2).</b> espeak-ng's <c>--ipa</c>
/// output is one line PER CLAUSE, not one line per call — a respelling with internal punctuation
/// (a comma, a period) prints MULTIPLE lines, proven in-container (<c>"hello, world."</c> → two
/// lines). A bare <c>.Trim()</c> only strips the ends, leaving embedded <c>\n</c> characters that
/// would ride straight into the JSON response body and any markup built from it downstream. Every
/// internal whitespace run (including newlines) is collapsed to a single space
/// (<see cref="NormalizeIpa"/>) before this method ever returns.
/// </para>
///
/// <para>
/// <b>The absence latch distinguishes "never works" from "didn't work this once" (review round 2
/// finding F3).</b> <see cref="IsAvailable"/> starts optimistic and flips to <see langword="false"/>
/// forever the first time starting the process fails in a way that means the BINARY ITSELF is
/// unusable — <see cref="FileNotFoundException"/>, or a <see cref="Win32Exception"/> whose
/// <see cref="Win32Exception.NativeErrorCode"/> is one of ENOENT (2, not on PATH), EACCES (13, not
/// permitted to execute), or ENOEXEC (8, not a valid executable) — the CoreCLR Unix PAL surfaces a
/// raw errno as <see cref="Win32Exception.NativeErrorCode"/> on Linux, not a translated Windows code.
/// A container either ships the apt package or it doesn't; none of those three answers changes
/// between requests within one process lifetime. A <em>different</em> <see cref="Win32Exception"/> —
/// EAGAIN (11), ENOMEM (12), EMFILE (24), the <c>fork()</c>-time resource-exhaustion class — means
/// THIS attempt failed because the box was briefly out of some resource, which says nothing about
/// whether the NEXT attempt will too: it returns <see langword="null"/> (the controller answers 502)
/// WITHOUT touching the latch, so a request during a transient squeeze does not permanently disable
/// the assist for the rest of the process's life.
/// </para>
///
/// <para>
/// <b>Both streams drained concurrently (review round 2 finding F4).</b> stdout and stderr are read
/// with <see cref="Task.WhenAll(Task,Task)"/> before <see cref="Process.WaitForExitAsync(System.Threading.CancellationToken)"/>
/// is awaited — reading only one redirected stream risks a classic full-pipe deadlock if espeak-ng
/// ever writes enough to the OTHER one to fill its OS pipe buffer while blocked waiting for it to be
/// read. A failed derivation (non-zero exit, empty stdout) logs ONE Warning line naming the exit
/// code and the stderr LENGTH only — never stderr's TEXT (SPEC F126.5's "log the event, not the
/// text", the same posture this class's own request-boundary caller already holds for the
/// respelling/derived IPA).
/// </para>
///
/// <para>
/// <b>Timeout, kill on expiry:</b> <see cref="RenderTimeout"/> bounds the whole exec — long enough
/// for a single word/short phrase's text-to-phoneme pass, short enough that a wedged process never
/// pins an admin request open. On expiry the process is killed (with its process tree, in case
/// espeak-ng ever shells out to a helper of its own) rather than left to leak.
/// </para>
/// </summary>
public sealed class EspeakRespellOracle(ILogger<EspeakRespellOracle> logger) : IRespellOracle
{
    const string ExecutableName = "espeak-ng";

    // Unix errno values that mean "the binary itself cannot be executed, ever" — surfaced by the
    // CoreCLR Unix PAL as Win32Exception.NativeErrorCode directly (no Windows-code translation on
    // Linux). See the class remarks (review round 2 finding F3) for the EAGAIN/ENOMEM/EMFILE class
    // this deliberately does NOT include.
    const int ENOENT = 2;
    const int ENOEXEC = 8;
    const int EACCES = 13;

    static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(5);

    volatile bool isAvailable = true;

    public bool IsAvailable => isAvailable;

    public async Task<string?> DeriveAsync(string respelling, CancellationToken ct)
    {
        if (!isAvailable)
            return null;

        var psi = BuildProcessStartInfo(respelling);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {ExecutableName}.");
        }
        catch (Win32Exception ex) when (IsBinaryUnusable(ex.NativeErrorCode))
        {
            // ENOENT/EACCES/ENOEXEC — the binary is missing, unreadable, or not executable. The ONE
            // case that latches IsAvailable false for the rest of this process's life.
            isAvailable = false;
            return null;
        }
        catch (FileNotFoundException)
        {
            // The .NET executable-resolution path some runtimes take instead of a Win32Exception —
            // same "the binary itself was never found" meaning, same latch.
            isAvailable = false;
            return null;
        }
        catch (Win32Exception)
        {
            // A transient fork-time failure (EAGAIN/ENOMEM/EMFILE and similar resource exhaustion) —
            // says nothing about whether the NEXT attempt would also fail. IsAvailable is
            // deliberately left untouched; this one request answers 502 via the caller.
            return null;
        }

        using var startedProcess = process;
        using var timeoutCts = new CancellationTokenSource(RenderTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        string stdout;
        string stderr;
        try
        {
            // Both streams drained CONCURRENTLY, then exit awaited — reading only one redirected
            // stream risks a full-pipe deadlock if espeak-ng writes enough to the other to fill its
            // OS pipe buffer while blocked on it (review round 2 finding F4).
            var stdoutTask = startedProcess.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderrTask = startedProcess.StandardError.ReadToEndAsync(linkedCts.Token);
            await Task.WhenAll(stdoutTask, stderrTask);
            await startedProcess.WaitForExitAsync(linkedCts.Token);
            stdout = await stdoutTask;
            stderr = await stderrTask;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our own RenderTimeout fired, not the caller's own cancellation — kill on expiry.
            return null;
        }
        finally
        {
            TryKill(startedProcess);
        }

        if (startedProcess.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            // The EVENT, not the text (SPEC F126.5) — exit code and stderr LENGTH only, never
            // stderr's own content (which could itself echo back the respelling).
            logger.LogWarning(
                "espeak-ng derivation failed: exitCode={ExitCode} stderrLength={StderrLength}",
                startedProcess.ExitCode, stderr.Length);
            return null;
        }

        return NormalizeIpa(stdout);
    }

    /// <summary>
    /// Builds the exact <see cref="ProcessStartInfo"/> <see cref="DeriveAsync"/> starts, without
    /// starting anything — factored out (mirrors <c>AubioBpmAnalyzer.BuildDecodeArguments</c>'s own
    /// shape) so the argv-only invocation contract (T278) is exercised directly against a captured
    /// <see cref="ProcessStartInfo"/> in tests, with no real process ever launched: <c>-q</c> (no
    /// audio output — never opens a sound device), <c>--ipa</c> (IPA phoneme mnemonics to stdout),
    /// <c>-v en-us</c> (deterministic voice/language regardless of container locale), <c>--</c> (the
    /// POSIX end-of-options marker — review round 2 finding F1: WITHOUT this, a respelling starting
    /// with <c>-</c> is parsed by espeak-ng as another OPTION, not data — <c>-f/root/appsettings.json</c>
    /// makes it read and speak that file, <c>--phonout=/path</c> makes it truncate that path; WITH
    /// this marker, getopt-family parsing stops and everything after is positional text, proven
    /// in-container both ways), then <paramref name="respelling"/> as the sixth and LAST entry —
    /// appended whole, never split, concatenated, or otherwise composed, so it reaches the process as
    /// exactly one argv element (the security-api <see cref="ProcessStartInfo.ArgumentList"/> rule)
    /// with <see cref="ProcessStartInfo.UseShellExecute"/> <see langword="false"/>.
    /// </summary>
    internal static ProcessStartInfo BuildProcessStartInfo(string respelling)
    {
        var psi = new ProcessStartInfo(ExecutableName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-q");
        psi.ArgumentList.Add("--ipa");
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("en-us");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(respelling);
        return psi;
    }

    /// <summary>
    /// Trims and collapses every internal whitespace run — including the <c>\n</c> espeak-ng emits
    /// between CLAUSES within one <c>--ipa</c> render (a comma or period in the respelling produces
    /// multiple output lines, proven in-container: <c>"hello, world."</c> → two lines) — to a single
    /// space (review round 2 finding F2). Splitting on any whitespace character and rejoining with
    /// one space handles trim + collapse in one pass; an all-whitespace/empty input yields
    /// <see cref="string.Empty"/>, which <see cref="DeriveAsync"/>'s own blank check upstream already
    /// treats as "no usable output".
    /// </summary>
    internal static string NormalizeIpa(string raw) =>
        string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    static bool IsBinaryUnusable(int nativeErrorCode) =>
        nativeErrorCode is ENOENT or EACCES or ENOEXEC;

    static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException)
        {
            // Already exited between the check and the kill, permission denied, or one child in the
            // tree already gone by the time Kill walked it (review round 2 finding F5) — best-effort
            // cleanup only. A finally-thrown exception here would silently replace whatever exception
            // (or successful return) was already propagating out of DeriveAsync.
        }
    }
}
