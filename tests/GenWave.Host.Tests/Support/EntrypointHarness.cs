namespace GenWave.Host.Tests.Support;

/// <summary>
/// Shared setup for specs that run the real <c>engine/entrypoint.sh</c>: a scratch PATH with the
/// real <c>curl</c> removed (callers stub it) and a <c>liquidsoap</c> stub that records argv+env
/// and exits 0. Used by Story485 and Story487.
/// </summary>
public static class EntrypointHarness
{
    /// <summary>Builds the scratch PATH described above. <paramref name="liquidsoapRecordedPath"/>
    /// receives the path the liquidsoap stub writes its recorded argv+env to — read it back AFTER
    /// running the script, since the stub only writes it once Liquidsoap has actually been
    /// exec'd.</summary>
    public static string MakeBinDirWithLiquidsoapStub(out string liquidsoapRecordedPath)
    {
        var bin = ScriptProcess.MakeBinDir();
        File.Delete(Path.Combine(bin, "curl"));

        var scratch = TempDir.CreateForProcessLifetime();
        var recorded = Path.Combine(scratch, "liquidsoap.recorded");
        ScriptProcess.AddStub(bin, "liquidsoap", $"""
            printf '%s\n' "$@" > "{recorded}"
            env >> "{recorded}"
            exit 0
            """);

        liquidsoapRecordedPath = recorded;
        return bin;
    }

    /// <summary>Runs entrypoint.sh with an empty starting environment (<see
    /// cref="ScriptProcess.RunWithEmptyEnvironment"/>) and throws on a non-zero exit — a broken
    /// stub, not a scenario under test (every consumer expects the engine to always boot, SPEC
    /// F213.2). Callers that assert the exit code themselves (STORY-487 AC8) call <see
    /// cref="ScriptProcess.RunWithEmptyEnvironment"/> directly instead.</summary>
    public static string RunOrThrow(string bin)
    {
        var run = ScriptProcess.RunWithEmptyEnvironment("engine/entrypoint.sh", bin);
        if (run.ExitCode != 0)
            throw new InvalidOperationException(
                $"engine/entrypoint.sh exited {run.ExitCode}; stderr:\n{run.StdErr}");
        return run.StdErr;
    }
}
