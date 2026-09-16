using System.Diagnostics;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// The one shared docker network every fixture container joins (gh-#649, STORY-440, PLAN T477):
/// <c>gw-test</c>, created once and declared <c>external: true</c> by <c>db-compose.yaml</c> and
/// <c>kokoro-compose.yaml</c>, so a full suite run stops allocating a project network per fixture
/// instance and exhausting the daemon's address pools. Linked into GenWave.MediaLibrary.Tests as a
/// compile item — one type, two assemblies.
/// </summary>
internal static class TestNetwork
{
    public const string Name = "gw-test";

    /// <summary>Runs <c>docker network create gw-test</c> through <paramref name="dockerExecutable"/>.
    /// An "already exists" failure is success; any other non-zero exit throws with the stderr text.</summary>
    public static void Ensure(string dockerExecutable = "docker")
    {
        var startInfo = new ProcessStartInfo(dockerExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("network");
        startInfo.ArgumentList.Add("create");
        startInfo.ArgumentList.Add(Name);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start {dockerExecutable}");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdOutTask, stdErrTask);

        if (process.ExitCode == 0) return;

        var stdErr = stdErrTask.Result;
        var stdOut = stdOutTask.Result;
        if (stdErr.Contains("already exists", StringComparison.Ordinal) ||
            stdOut.Contains("already exists", StringComparison.Ordinal))
            return;

        throw new InvalidOperationException(
            $"{dockerExecutable} network create {Name} failed (exit {process.ExitCode}):\n{stdErr}{stdOut}");
    }
}
