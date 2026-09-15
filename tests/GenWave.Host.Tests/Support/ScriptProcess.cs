using System.Diagnostics;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// The ONE way a Host.Tests spec may start ./launch.sh, ./build.sh, or any other repo script
/// (gh-#776, STORY-438, PLAN T473). Before this type, nine spec files each carried their own
/// copy of "run bash with a scratch PATH" — every copy trusting the developer's actual shell
/// environment to not leak a stray <c>GW_*</c>/<c>SKIP_*</c>/<c>COMPOSE_*</c> export, or a real
/// secret, into the child. <see cref="Run"/> strips that risk out by construction: it removes
/// every environment variable <see cref="IsStripped"/> names before the child ever starts, so a
/// spec's assertions describe the script's OWN behaviour, not whatever happened to be exported in
/// the terminal that ran `dotnet test`.
/// </summary>
public static class ScriptProcess
{
    /// <summary>
    /// Coreutils (plus <c>bash</c>/<c>sh</c> themselves) that at least one script-under-test spec
    /// needs on its scratch PATH — the union of every file-local <c>BaseTools</c> list this type
    /// replaces. <see cref="MakeBinDir"/> symlinks all of these by default; callers add anything
    /// scenario-specific (a scripted `docker`/`dotnet` stub, say) via <c>extraTools</c> or
    /// <see cref="AddStub"/>.
    /// </summary>
    static readonly string[] BaseTools =
    [
        "bash", "sh", "grep", "sed", "tail", "head", "cut", "seq", "sleep", "awk", "dirname",
        "cat", "paste", "find", "tr", "mktemp", "mv", "rm", "uname", "date", "sort", "curl",
        "hostname", "wc", "touch", "env",
    ];

    /// <summary>
    /// Exact environment variable names to strip beyond <see cref="StrippedPrefixes"/>: the
    /// scripts' own <c>BUILD</c>/<c>CONFIG</c> seams, plus every .env secret preflight checks for
    /// (<c>ADMIN_PASSWORD</c>, <c>MEDIA_DIR</c>, and the five Postgres/Icecast passwords).
    /// </summary>
    static readonly string[] StrippedNames =
    [
        "BUILD", "CONFIG", "ADMIN_PASSWORD", "MEDIA_DIR", "POSTGRES_PASSWORD",
        "LIBRARY_DB_PASSWORD", "STATION_DB_PASSWORD", "ICECAST_SOURCE_PASSWORD",
        "ICECAST_ADMIN_PASSWORD",
    ];

    static readonly string[] StrippedPrefixes = ["GW_", "SKIP_", "COMPOSE_"];

    /// <summary>True for every launch/build seam and .env secret name a script child must never
    /// inherit from the parent test process: <c>GW_*</c>, <c>SKIP_*</c>, <c>COMPOSE_*</c>, and the
    /// exact names in <see cref="StrippedNames"/>. Everything else (HOME, LANG/LC_*, TMPDIR,
    /// DOTNET_*, USER, TERM, ...) passes through untouched.</summary>
    public static bool IsStripped(string name) =>
        StrippedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)) ||
        StrippedNames.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// Runs <paramref name="script"/> under bash with a sanitized environment: every variable
    /// <see cref="IsStripped"/> names is removed, <c>PATH</c> is set to <paramref name="binDir"/>
    /// alone, <c>GW_ENV_FILE</c> is set from <paramref name="envFile"/> when given, then
    /// <paramref name="extraEnv"/> is applied on top (so a scenario can restore exactly the one
    /// seam it needs, e.g. <c>SKIP_PREFLIGHT=1</c>, without reopening every other one). Reads
    /// stdout and stderr concurrently — a script that writes a lot to stderr before it writes
    /// anything to stdout would otherwise deadlock a caller that reads them one at a time.
    /// <paramref name="script"/> is repo-relative, or absolute for a scratch copy of the repo.
    /// <paramref name="binDir"/> is usually a <see cref="MakeBinDir"/> result; a full
    /// <c>PATH</c> string is legal when a scenario needs the real toolchain.
    /// </summary>
    public static (int ExitCode, string StdOut, string StdErr) Run(
        string script, string binDir, string? envFile = null,
        IReadOnlyDictionary<string, string>? extraEnv = null, params string[] args)
    {
        var repoRoot = RepoRootLocator.Find(AppContext.BaseDirectory);

        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(repoRoot, script));
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        foreach (var name in startInfo.Environment.Keys.ToList())
        {
            if (IsStripped(name))
                startInfo.Environment.Remove(name);
        }

        startInfo.Environment["PATH"] = binDir;
        if (envFile is not null)
            startInfo.Environment["GW_ENV_FILE"] = envFile;
        if (extraEnv is not null)
            foreach (var (key, value) in extraEnv)
                startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"failed to start {script}");

        // Start draining both streams before WaitForExit — a script that fills the stderr pipe
        // buffer while we block synchronously on stdout (or vice versa) would otherwise hang.
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdOutTask, stdErrTask);

        return (process.ExitCode, stdOutTask.Result, stdErrTask.Result);
    }

    static string ResolveTool(string tool)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            var candidate = Path.Combine(dir, tool);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new InvalidOperationException($"required tool not on PATH: {tool}");
    }

    /// <summary>A scratch bin dir with <see cref="BaseTools"/> (plus any <paramref name="extraTools"/>)
    /// symlinked in and nothing else — a scenario adds a scripted stub on top with <see cref="AddStub"/>.</summary>
    public static string MakeBinDir(params string[] extraTools)
    {
        var dir = Directory.CreateTempSubdirectory("gw-script-bin-").FullName;
        foreach (var tool in BaseTools.Concat(extraTools).Distinct(StringComparer.Ordinal))
            File.CreateSymbolicLink(Path.Combine(dir, tool), ResolveTool(tool));
        return dir;
    }

    /// <summary>Writes an executable bash script named <paramref name="name"/> into <paramref name="binDir"/>,
    /// shadowing any real tool of the same name on the scratch PATH — how a scenario scripts a
    /// fake `docker`/`dotnet`/`git` response.</summary>
    public static void AddStub(string binDir, string name, string body)
    {
        var path = Path.Combine(binDir, name);
        File.WriteAllText(path, "#!/usr/bin/env bash\n" + body + "\n");
        // The scripts under test are bash — these specs only ever run on a Unix host (CI + dev
        // are both Linux); the guard exists to satisfy CA1416, not to support Windows.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
