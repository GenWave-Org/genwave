using System.Diagnostics;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// Runs the real <c>docker</c> CLI and captures its output, for specs that build and boot the real
/// engine image (STORY-488, gh-#791/#771). Used by <c>Story488_EngineSettingsVerdict</c> and
/// <c>Gh791_SafeResolverSurvivesApiOutage</c>; Story129 still carries its own copy.
/// </summary>
internal static class DockerCli
{
    /// <summary>Runs <c>docker</c> with <paramref name="args"/>, capturing stdout/stderr. Kills the
    /// process tree and throws if it hasn't exited within 5 minutes.</summary>
    public static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var info = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Failed to start docker.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"docker {string.Join(' ', args)} did not finish in 5 minutes.");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>Arrange, not assert: a failed docker step is an environment problem.</summary>
    public static void Arrange(params string[] args)
    {
        var r = Run(args);
        if (r.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker {string.Join(' ', args)} failed (exit {r.ExitCode}): {r.Stderr}");
        }
    }
}
