namespace GenWave.Host.Tests.Support;

/// <summary>
/// Minimal adoption-mode harness for the F216 specs (Story514, Story515): a healthy home-box
/// .env, a scripted `docker` stand-in answering every verify probe with the "healthy" answer,
/// and a runner that spools stdin answers to setup.sh the way Story346 does. Deliberately
/// smaller than Story346's own WriteDockerStub — these facts only need a box with nothing to
/// repair except what each scenario perturbs.
/// </summary>
internal static class HomeSetupHarness
{
    public static string ScratchEnvPath() => Path.Combine(TempDir.CreateForProcessLifetime(), ".env");

    /// <summary>Every uncommented .env.example key, each with a real non-placeholder value, plus
    /// the given COMPOSE_FILE.</summary>
    public static Dictionary<string, string> HealthyEnvValues(string composeFile) => new()
    {
        ["COMPOSE_PROFILES"] = "admin",
        ["MEDIA_DIR"] = Path.GetTempPath(),
        ["COMPOSE_FILE"] = composeFile,
        ["POSTGRES_PASSWORD"] = "a-real-postgres-secret-0123456789",
        ["LIBRARY_DB_PASSWORD"] = "a-real-library-secret-0123456789",
        ["STATION_DB_PASSWORD"] = "a-real-station-secret-0123456789",
        ["ICECAST_SOURCE_PASSWORD"] = "a-real-icecast-source-0123456789",
        ["ICECAST_ADMIN_PASSWORD"] = "a-real-icecast-admin-0123456789",
        ["ADMIN_PASSWORD"] = "a-real-admin-ui-secret-0123456789",
    };

    public static void WriteEnvFile(string path, IReadOnlyDictionary<string, string> values) =>
        File.WriteAllText(path, string.Join("\n", values.Select(kv => $"{kv.Key}={kv.Value}")) + "\n");

    static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>A `docker` stand-in for GW_DOCKER_CMD. <paramref name="composeArgs"/> is the
    /// `-f …` string setup.sh derives from the box's COMPOSE_FILE (the `--env-file` pair is
    /// stripped first, as Story346 does). Every probe answers "healthy": db reachable, every
    /// migration marker present, no settings override, two services with two running
    /// containers, nothing reclaimable.</summary>
    public static string WriteDockerStub(string composeArgs)
    {
        var path = Path.Combine(TempDir.CreateForProcessLifetime(), "docker");
        var script =
            "#!/usr/bin/env bash\n" +
            "if [ \"${1:-}\" = compose ] && [ \"${2:-}\" = --env-file ]; then set -- \"$1\" \"${@:4}\"; fi\n" +
            "case \"$*\" in\n" +
            $"  {Quote($"compose {composeArgs} ps -q db")}) echo cid123 ;;\n" +
            "  *\"to_regclass(\"*) echo t ;;\n" +
            "  *\"station.settings\"*) echo '' ;;\n" +
            $"  {Quote($"compose {composeArgs} config --services")}) printf '%s\\n' db api ;;\n" +
            $"  {Quote($"compose {composeArgs} config --format json")}) printf '%s\\n' '{{\"name\": \"genwave\"}}' ;;\n" +
            $"  {Quote($"compose {composeArgs} config")}) printf '%s\\n' 'services:' '  db:' '    image: postgres' '  api:' '    image: genwave/api' ;;\n" +
            "  \"ps -a --filter label=com.docker.compose.project=genwave --format {{.Label \\\"com.docker.compose.service\\\"}}|{{.Names}}|{{.State}}\")\n" +
            "    printf '%s\\n' 'db|genwave-db-1|running' 'api|genwave-api-1|running' ;;\n" +
            "  \"system df\")\n" +
            "    printf '%s\\n' 'TYPE            TOTAL     ACTIVE    SIZE      RECLAIMABLE' 'Images          2         2         1GB       0B (0%)' ;;\n" +
            "  *) exit 0 ;;\n" +
            "esac\n";
        File.WriteAllText(path, script);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return path;
    }

    /// <summary>Runs the real repo setup.sh in adoption mode (the .env exists) with
    /// <paramref name="stdinAnswers"/> spooled to stdin, SKIP_PREFLIGHT=1 and the given docker
    /// stub on GW_DOCKER_CMD.</summary>
    public static (int ExitCode, string StdOut, string StdErr) RunSetup(
        string envFile, string dockerStub, string stdinAnswers, params string[] args)
    {
        var scratchDir = TempDir.CreateForProcessLifetime();
        var answersPath = Path.Combine(scratchDir, "answers.txt");
        File.WriteAllText(answersPath, stdinAnswers);

        var scriptPath = Path.Combine(RepoRootLocator.Find(AppContext.BaseDirectory), "setup.sh");
        var wrapperPath = Path.Combine(scratchDir, "run-setup.sh");
        File.WriteAllText(wrapperPath, $"exec bash \"{scriptPath}\" \"$@\" < \"{answersPath}\"\n");

        var extraEnv = new Dictionary<string, string>
        {
            ["SKIP_PREFLIGHT"] = "1",
            ["GW_DOCKER_CMD"] = dockerStub,
        };
        return ScriptProcess.Run(wrapperPath, ScriptProcess.MakeBinDir(), envFile, extraEnv, args);
    }
}
