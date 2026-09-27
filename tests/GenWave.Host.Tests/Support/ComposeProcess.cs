using System.Diagnostics;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// The `docker compose` process plumbing every ephemeral-Postgres-per-test fixture needs: run a
/// command and capture its output (throwing with both streams on a non-zero exit), and ask
/// `docker compose port` which host port it assigned to a service that doesn't publish to a fixed
/// one (gh-#569). This is the shared helper (STORY-484, PLAN T591 review F5), written for
/// <c>GenWave.MediaLibrary.Tests.Support.MigrateShDatabase</c> so that fixture didn't have to start
/// life as a THIRD near-identical copy of the same ~25 lines <see cref="EphemeralStationDatabase"/>
/// and <c>GenWave.MediaLibrary.Tests.DatabaseFixture</c> each already carry. Those two keep their own
/// copies for now (unchanged by this task; neither file is owned by STORY-484/T591) — they're due to
/// move onto this shared copy in a follow-up, rather than this class growing a fourth peer.
/// </summary>
internal static class ComposeProcess
{
    public static void Compose(string project, string composeFile, params string[] verbAndArgs)
    {
        var args = new List<string> { "compose", "-p", project, "-f", composeFile };
        args.AddRange(verbAndArgs);
        RunCapture("docker", args);
    }

    public static int DiscoverHostPort(string project, string composeFile, string service, int containerPort)
    {
        var output = RunCapture(
            "docker",
            ["compose", "-p", project, "-f", composeFile, "port", service, containerPort.ToString()]).Trim();
        var lastColon = output.LastIndexOf(':');
        if (lastColon < 0 || !int.TryParse(output[(lastColon + 1)..], out var hostPort))
            throw new InvalidOperationException($"could not parse a host port from 'docker compose port' output: '{output}'");
        return hostPort;
    }

    public static string RunCapture(string file, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {file}");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{file} {string.Join(' ', args)} failed:\n{stderr.Result}{stdout.Result}");
        return stdout.Result;
    }
}
