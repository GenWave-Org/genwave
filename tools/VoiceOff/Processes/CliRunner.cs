using System.ComponentModel;
using System.Diagnostics;

namespace GenWave.VoiceOff.Processes;

/// <summary>Runs one program with an argument list (argv, no shell) and captures what it printed.</summary>
internal static class CliRunner
{
    /// <summary>
    /// Runs <paramref name="program"/> to completion and returns its exit code and both output streams.
    /// Null means the program is not installed. Cancelling kills the process tree, waits for it to go, then throws.
    /// </summary>
    internal static async Task<(int ExitCode, string Stdout, string Stderr)?> RunAsync(
        string program, IEnumerable<string> arguments, CancellationToken ct)
    {
        ProcessStartInfo psi = new(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using Process process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {program}.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                throw;
            }

            return (process.ExitCode, await stdout, await stderr);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
