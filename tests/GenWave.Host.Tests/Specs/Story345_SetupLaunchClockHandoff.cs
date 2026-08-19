// STORY-345 — Launch, the clock, the handoff (F132.7–.8)
//
// BDD specification — xUnit. Drives the REAL ./setup.sh via Process; launch.sh and
// the mount probe are scratch-PATH stubs that record their argv and script their
// outputs (the Gh019 idiom) — the wizard's orchestration is under spec here, not
// compose.
//
// Harness: the Story344/Story342 idiom (scratch PATH bin dir of coreutils symlinks, a scratch
// GW_ENV_FILE, ambient GW_*/SKIP_PREFLIGHT scrubbed from the child environment), extended with
// two seams the real launch.sh/Icecast can't be run under this harness at all — the T318 task
// note's own reasoning: the REAL launch.sh would try to talk to a real Docker daemon, and a
// real Icecast isn't something this harness can stand up either. Chosen pair (both documented
// in setup.sh's own header):
//   * GW_LAUNCH_CMD — points at a tiny scripted bash stub (WriteLaunchStub) that records its
//     argv and exits with a scripted code (0 / 4 / anything else) — never a real docker call.
//   * GW_STREAM_URL — points at a scratch Kestrel instance (MountStub, the same
//     WebApplication.CreateEmptyBuilder + UseKestrelCore + Listen(loopback, 0) idiom
//     Story179_SpectatorListenerCount.cs already established for a fake upstream HTTP
//     service) that scripts which poll attempt first returns HTTP 200 + audio bytes.
// Every scenario also passes SKIP_PREFLIGHT=1 — preflight itself is Story342/Story344's own
// suite; this file's concern starts at the "ready to launch" point.
//
// House rule: one assert per Fact — a handful of facts assert one combined boolean via a
// single Assert.True(...) call where the observation is genuinely one logical fact (several
// conditions that only mean something together), the same idiom Story344 already uses.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// A scratch icecast-mount stand-in: Kestrel on port 0 (read back after it starts, the
/// Story179/gh-#329 idiom — no free-port-then-rebind race). Serves 404 with no body until the
/// <paramref name="servesOnAttempt"/>'th request, then HTTP 200 with a small nonzero body
/// forever after — a request count larger than any request this test will ever make (e.g.
/// <see cref="int.MaxValue"/>) means "never serves", the sad-path fixture for the poll-timeout
/// fact.
/// </summary>
file sealed class MountStub : IDisposable
{
    readonly WebApplication app;
    int requestCount;

    public string Url { get; }

    public MountStub(int servesOnAttempt = 1)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();

        app.Run(async ctx =>
        {
            var attempt = Interlocked.Increment(ref requestCount);
            if (attempt < servesOnAttempt)
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "audio/mpeg";
            var bytes = new byte[512];
            Random.Shared.NextBytes(bytes);
            await ctx.Response.Body.WriteAsync(bytes);
        });

        app.Start();
        var baseUrl = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First()
            .TrimEnd('/');
        Url = $"{baseUrl}/stream";
    }

    public void Dispose()
    {
        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// T318 review round-1 BLOCKING finding F1's spec-side requirement: the old MountStub always
/// serves (or doesn't) purely by request COUNT, which can't distinguish "launch.sh already
/// returned before the poll started" from "the poll caught it mid-launch" — the exact
/// distinction that finding needs proven. This stub instead gates on a MARKER FILE's existence:
/// 404 until <paramref name="markerPath"/> appears on disk, HTTP 200 + audio bytes forever after
/// — a launch stub (<see cref="FeatureSetupLaunchClockHandoff.WriteLaunchStubThatArmsMountPartway"/>)
/// can `touch` that marker partway through its own run, letting a fact prove the concurrent
/// poller caught the on-air moment WHILE launch.sh was still running, not after.
/// </summary>
file sealed class ArmableMountStub : IDisposable
{
    readonly WebApplication app;

    public string Url { get; }

    public string MarkerPath { get; }

    public ArmableMountStub()
    {
        MarkerPath = Path.Combine(
            Directory.CreateTempSubdirectory("gw-setup-story345-arm-").FullName, "armed");

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();

        app.Run(async ctx =>
        {
            if (!File.Exists(MarkerPath))
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "audio/mpeg";
            var bytes = new byte[512];
            Random.Shared.NextBytes(bytes);
            await ctx.Response.Body.WriteAsync(bytes);
        });

        app.Start();
        var baseUrl = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First()
            .TrimEnd('/');
        Url = $"{baseUrl}/stream";
    }

    public void Dispose()
    {
        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

public static class FeatureSetupLaunchClockHandoff
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared harness (the Story344 idiom)
    // ─────────────────────────────────────────────────────────────────────────

    // F1 item 4 (round-4 review, BLOCKING) — sending a real terminal-shaped Ctrl-C (SIGINT to
    // the whole foreground process GROUP, not just one PID) needs a raw `kill(2)` call with a
    // negative pid; no managed API exposes that, so this is the one deliberate P/Invoke in the
    // suite. `DllImport` (not the source-generated `LibraryImport`) on purpose — the generator
    // requires `AllowUnsafeBlocks`, a csproj change outside this task's owned files. `EntryPoint`
    // is required — the libc symbol is lowercase `kill`, case-sensitive on Linux, and would not
    // resolve against the PascalCase C# method name otherwise.
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int sig);

    private const int SigInt = 2;

    static readonly string[] RequiredEnvVars =
    [
        "POSTGRES_PASSWORD", "LIBRARY_DB_PASSWORD", "STATION_DB_PASSWORD",
        "ICECAST_SOURCE_PASSWORD", "ICECAST_ADMIN_PASSWORD", "MEDIA_DIR",
    ];

    /// <summary>setup.sh/preflight.sh test seams this suite might otherwise inherit from the
    /// ambient shell — scrubbed so the developer's real .env/exports can never sway a fact.</summary>
    static readonly string[] SeamEnvVars =
    [
        "ADMIN_PASSWORD", "COMPOSE_PROFILES", "GW_PRESET", "GW_ENV_FILE", "GW_MEMINFO_FILE",
        "GW_ARCH", "GW_PREFLIGHT_TOPOLOGY", "GW_PREFLIGHT_DEMO", "GW_CMDLINE_FILE",
        "GW_MOUNTS_FILE", "GW_SS_CMD", "GW_DF_CMD", "GW_FIND_CMD", "GW_DOCKER_ROOT_FALLBACK",
        "SKIP_PREFLIGHT", "GW_LAUNCH_CMD", "GW_STREAM_URL", "GW_ONAIR_TIMEOUT_SECONDS",
    ];

    static readonly string[] BaseTools =
    [
        "bash", "sh", "grep", "sed", "tail", "head", "cut", "seq", "sleep", "awk", "dirname",
        "cat", "paste", "find", "tr", "mktemp", "mv", "rm", "uname", "date", "curl", "hostname",
    ];

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
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

    static string MakeBinDir()
    {
        var dir = Directory.CreateTempSubdirectory("gw-setup-story345-bin-").FullName;
        foreach (var tool in BaseTools)
            File.CreateSymbolicLink(Path.Combine(dir, tool), ResolveTool(tool));
        return dir;
    }

    static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>A bin dir with no `dotnet` at all — Q1's build-your-own path is never offered,
    /// so a virgin interview needs only three answers (music path, topology, admin).</summary>
    static string BinWithoutDotnet() => MakeBinDir();

    static string MakeMediaDir(int flacCount)
    {
        var dir = Directory.CreateTempSubdirectory("gw-setup-story345-media-").FullName;
        for (var i = 0; i < flacCount; i++) File.WriteAllText(Path.Combine(dir, $"track{i}.flac"), "");
        return dir;
    }

    static string ScratchEnvDir() => Directory.CreateTempSubdirectory("gw-setup-story345-env-").FullName;

    static string ScratchEnvPath() => Path.Combine(ScratchEnvDir(), ".env");

    static string ReadEnvValue(string envContent, string key)
    {
        foreach (var rawLine in envContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith(key + "=", StringComparison.Ordinal))
                return line[(key.Length + 1)..];
        }
        throw new InvalidOperationException($"'{key}=' not found in the written .env:\n{envContent}");
    }

    /// <summary>Writes a scripted launch.sh stand-in: records its (bare) argv to
    /// <paramref name="argvLogPath"/> when given, then exits with <paramref name="exitCode"/>.
    /// Never touches docker/compose — that's exactly the point (GW_LAUNCH_CMD's whole reason
    /// to exist under this harness).</summary>
    static string WriteLaunchStub(int exitCode, string? argvLogPath = null)
    {
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("gw-setup-story345-launch-").FullName, "launch-stub.sh");
        var logLine = argvLogPath is null ? "" : $"printf '%s\\n' \"argv:$*\" >> \"{argvLogPath}\"\n";
        File.WriteAllText(path, $"#!/usr/bin/env bash\n{logLine}exit {exitCode}\n");
        MakeExecutable(path);
        return path;
    }

    /// <summary>T318 review F1's spec-side requirement: unlike <see cref="WriteLaunchStub"/>,
    /// which returns instantly, this stub keeps running for <paramref name="totalRuntimeSeconds"/>
    /// total — but touches <paramref name="markerPath"/> (an <see cref="ArmableMountStub"/>'s
    /// gate) after only <paramref name="armAfterSeconds"/>, so the mount starts serving audio
    /// WHILE this stub is still running. A fact asserting the printed M:SS is well under
    /// <paramref name="totalRuntimeSeconds"/> is the proof the poller ran concurrently with
    /// launch.sh rather than only starting once launch.sh had already returned.</summary>
    static string WriteLaunchStubThatArmsMountPartway(
        string markerPath, int armAfterSeconds, int totalRuntimeSeconds, int exitCode = 0)
    {
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("gw-setup-story345-launch-").FullName, "launch-stub.sh");
        var remainingSeconds = totalRuntimeSeconds - armAfterSeconds;
        // `: > markerPath`, not `touch` — a shell builtin, so this never needs `touch` added to
        // BaseTools' deliberately minimal restricted PATH.
        File.WriteAllText(path,
            $"#!/usr/bin/env bash\nsleep {armAfterSeconds}\n: > \"{markerPath}\"\nsleep {remainingSeconds}\nexit {exitCode}\n");
        MakeExecutable(path);
        return path;
    }

    /// <summary>A launch.sh stand-in that keeps running for <paramref name="sleepSeconds"/>
    /// before exiting with <paramref name="exitCode"/> — used where a fact needs to prove
    /// something about what happens WHILE launch.sh is still in flight, not just at its
    /// (near-instant) return under <see cref="WriteLaunchStub"/>.</summary>
    static string WriteDelayedLaunchStub(int sleepSeconds, int exitCode)
    {
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("gw-setup-story345-launch-").FullName, "launch-stub.sh");
        File.WriteAllText(path, $"#!/usr/bin/env bash\nsleep {sleepSeconds}\nexit {exitCode}\n");
        MakeExecutable(path);
        return path;
    }

    /// <summary>F1 item 4 (round-4 review) — a launch.sh stand-in shaped like the reviewer's own
    /// repro: it TRAPS (and so swallows) SIGINT itself, rather than dying by default
    /// disposition, then keeps running for <paramref name="totalRuntimeSeconds"/> before exiting
    /// <paramref name="exitCode"/> — the "compose-like child traps INT and exits 130, launch
    /// swallows it and exits 4" shape, minus the irrelevant inner layer (setup.sh only ever
    /// observes launch.sh's own final exit code).</summary>
    static string WriteLaunchStubThatSwallowsInt(int totalRuntimeSeconds, int exitCode)
    {
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("gw-setup-story345-launch-").FullName, "launch-stub.sh");
        File.WriteAllText(path,
            $"#!/usr/bin/env bash\ntrap '' INT\nsleep {totalRuntimeSeconds}\nexit {exitCode}\n");
        MakeExecutable(path);
        return path;
    }

    /// <summary>Writes a scripted stub binary <paramref name="name"/> into <paramref
    /// name="binDir"/> — the Story344 idiom (duplicated here per F10's pinned-for-Dean's-ruling
    /// three-way harness split, not shared).</summary>
    static void AddStub(string binDir, string name, string body)
    {
        var path = Path.Combine(binDir, name);
        File.WriteAllText(path, "#!/usr/bin/env bash\n" + body + "\n");
        MakeExecutable(path);
    }

    /// <summary>A bin dir whose `dotnet --list-sdks` reports a 10.x SDK — offers Q1's
    /// build-from-source option, the only way to drive IMAGES_MODE=dev (and so GW_PRESET=dev)
    /// through the real interview. Needed by the stale-mount-gate facts below: that gate is
    /// specific to the dev/dev-piper-only presets (launch.sh's dev flow tears the previous stack
    /// down FIRST). Mirrors Story344_SetupWizardInterview.cs's own helper of the same name
    /// (file-scoped there too — F10's pinned duplication, not shared).</summary>
    static string BinWithDotnet10Sdk()
    {
        var bin = MakeBinDir();
        AddStub(bin, "dotnet",
            """if [ "${1:-}" = "--list-sdks" ]; then echo "10.0.100 [/usr/lib/dotnet/sdk]"; exit 0; fi; exit 0""");
        return bin;
    }

    /// <summary>B1 (round-3 review): a `hostname` that exists on PATH (so `command -v hostname`
    /// succeeds) but exits nonzero on any invocation — the busybox/Alpine/macOS shape of
    /// `hostname -I` not being a thing. Overwrites the real symlinked `hostname` from
    /// <see cref="BinWithoutDotnet"/>'s underlying <see cref="MakeBinDir"/> bin dir.</summary>
    static string BinWithBrokenHostname()
    {
        var bin = MakeBinDir();
        File.Delete(Path.Combine(bin, "hostname"));
        AddStub(bin, "hostname", "exit 1");
        return bin;
    }

    /// <summary>N4 (round-3 review): a bin dir with every <see cref="BaseTools"/> entry except
    /// curl — the "no prober available at all" shape wait_for_on_air_bg must degrade honestly
    /// under, per this file's own note that the specs already control PATH via
    /// <see cref="MakeBinDir"/>.</summary>
    static string BinWithoutCurl()
    {
        var bin = Directory.CreateTempSubdirectory("gw-setup-story345-bin-").FullName;
        foreach (var tool in BaseTools.Where(tool => tool != "curl"))
            File.CreateSymbolicLink(Path.Combine(bin, tool), ResolveTool(tool));
        return bin;
    }

    /// <summary>The seam set every scenario in this file needs: preflight skipped (out of
    /// scope here), plus the launch/mount/timeout seams pointed at this run's stubs.</summary>
    static Dictionary<string, string> BaseEnv(string launchCmd, string streamUrl, int onAirTimeoutSeconds) =>
        new()
        {
            ["SKIP_PREFLIGHT"] = "1",
            ["GW_LAUNCH_CMD"] = launchCmd,
            ["GW_STREAM_URL"] = streamUrl,
            ["GW_ONAIR_TIMEOUT_SECONDS"] = onAirTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>Runs the real setup.sh, feeding the given text verbatim to stdin (then closing
    /// it) and returning the whole run's exit code/stdout/stderr — the Gh019/Story344 idiom.
    /// <paramref name="extraEnv"/> is mandatory (not optional/nullable) here on purpose: every
    /// scenario in this file reaches the launch stage, so GW_LAUNCH_CMD must always be pinned
    /// at a stub — an accidental unset would exec the REAL ./launch.sh against a fake PATH.</summary>
    static (int ExitCode, string StdOut, string StdErr) RunSetup(
        string binDir, string envFile, string stdinAnswers, IReadOnlyDictionary<string, string> extraEnv)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), "setup.sh"));

        startInfo.Environment["PATH"] = binDir;
        foreach (var name in RequiredEnvVars) startInfo.Environment.Remove(name);
        foreach (var name in SeamEnvVars) startInfo.Environment.Remove(name);
        startInfo.Environment["GW_ENV_FILE"] = envFile;
        foreach (var (key, value) in extraEnv)
            startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start setup.sh");

        // Concurrent reads, not sequential ReadToEnd() + WaitForExit() (Story343's convention):
        // a child writing enough to fill both OS pipe buffers at once can deadlock a reader that
        // drains one stream to completion before starting the other.
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        // A child that exits before ever reading its next prompt closes its stdin read end
        // without draining the scripted answers — this write racing that exit is a legitimate
        // outcome some facts rely on, not a test failure (the Story344 flake lesson), so a
        // broken pipe here is swallowed rather than thrown.
        try
        {
            process.StandardInput.Write(stdinAnswers);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Child already exited without reading stdin — nothing left to write to.
        }

        Task.WaitAll(stdOutTask, stdErrTask);
        process.WaitForExit();

        return (process.ExitCode, stdOutTask.Result, stdErrTask.Result);
    }

    /// <summary>F1 item 4 (round-4 review) — the reviewer's exact repro shape: setup.sh (and
    /// everything it spawns — the background poller, the launch stub) is run under `setsid` so
    /// it becomes its own new session/process-group leader, then a SIGINT is delivered to that
    /// whole process GROUP (a negative PGID) partway through the run, while the launch stub is
    /// still executing — the same signal shape a real terminal Ctrl-C actually sends (every
    /// foreground process in the group at once), not just one PID. Returns once setup.sh itself
    /// has exited, plus the PGID used, so a fact can confirm nothing was left running in it
    /// (<see cref="ProcessGroupIsEmpty"/>).</summary>
    static (int ExitCode, string StdOut, string StdErr, int Pgid) RunSetupSendingSigintMidLaunch(
        string binDir, string envFile, string stdinAnswers, IReadOnlyDictionary<string, string> extraEnv,
        TimeSpan sendAfter)
    {
        var workDir = Directory.CreateTempSubdirectory("gw-setup-story345-sigint-").FullName;
        var pgidFile = Path.Combine(workDir, "pgid");
        var wrapperPath = Path.Combine(workDir, "wrapper.sh");
        // The wrapper reports its OWN pid to a file, then `exec`s into setup.sh — `exec` replaces
        // the process image in place (no further fork), so the pid stays the one just reported
        // for the rest of the run. By the time this runs, `setsid` has already made this process
        // a new session/process-group leader (pid == pgid), so the reported value IS the pgid.
        File.WriteAllText(wrapperPath,
            $"#!/usr/bin/env bash\necho $$ > \"{pgidFile}\"\nexec bash \"{Path.Combine(RepoRoot(), "setup.sh")}\"\n");
        MakeExecutable(wrapperPath);

        var startInfo = new ProcessStartInfo("setsid")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        // `setsid`'s own default behavior — fork only if ITS caller is already a process group
        // leader, otherwise exec in place — depends on whether the test host's own process tree
        // happens to already be a group leader, which this suite has no control over and can't
        // rely on either way. `--fork` forces the fork unconditionally; `--wait` makes the
        // ORIGINAL setsid invocation (the one this `Process` object actually tracks) block for,
        // and propagate, the real exit code, so `process.ExitCode`/`WaitForExit()` below stay
        // accurate no matter which internal path setsid takes. The forked descendant's own pid
        // (== its pgid) is never assumed to equal `process.Id` — it's read back from the
        // wrapper's own self-report file instead (below).
        startInfo.ArgumentList.Add("--wait");
        startInfo.ArgumentList.Add("--fork");
        startInfo.ArgumentList.Add("bash");
        startInfo.ArgumentList.Add(wrapperPath);

        startInfo.Environment["PATH"] = binDir;
        foreach (var name in RequiredEnvVars) startInfo.Environment.Remove(name);
        foreach (var name in SeamEnvVars) startInfo.Environment.Remove(name);
        startInfo.Environment["GW_ENV_FILE"] = envFile;
        foreach (var (key, value) in extraEnv)
            startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start setup.sh under setsid");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        try
        {
            process.StandardInput.Write(stdinAnswers);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Child already exited without reading stdin — nothing left to write to.
        }

        var pgid = ReadPgidFileOnceWritten(pgidFile);

        Thread.Sleep(sendAfter);
        // round-4 review N5a: a free PID-reuse guard — `--wait` means `process` tracks the
        // whole run (it only returns once the real setup.sh has exited), so if it has ALREADY
        // finished by the time this fires (an unusually fast run), skip the signal rather than
        // risk it landing on some unrelated process the OS has since reused `pgid` for.
        if (!process.HasExited) Kill(-pgid, SigInt);

        Task.WaitAll(stdOutTask, stdErrTask);
        process.WaitForExit();

        return (process.ExitCode, stdOutTask.Result, stdErrTask.Result, pgid);
    }

    /// <summary>Polls briefly for <see cref="RunSetupSendingSigintMidLaunch"/>'s wrapper script to
    /// have written its self-reported pgid, then parses and returns it.</summary>
    static int ReadPgidFileOnceWritten(string pgidFile)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (File.Exists(pgidFile))
            {
                var text = File.ReadAllText(pgidFile).Trim();
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pgid))
                    return pgid;
            }
            Thread.Sleep(20);
        }
        throw new InvalidOperationException($"setup.sh's process group never reported itself at '{pgidFile}'");
    }

    // round-4 review N5b: the Linux errno for "no such process" — the only failure `kill(2)`
    // can report that actually PROVES the group is empty. Any other errno (EPERM in particular —
    // the group exists but the signal couldn't be delivered to it) must never be read as empty.
    private const int Esrch = 3;

    /// <summary>F1 item 4 — polls (briefly) for every process in <paramref name="pgid"/>'s
    /// process group to be gone, via the POSIX null-signal existence check (`kill(pgid, 0)`
    /// returns -1/ESRCH once none remain). A short retry loop rather than a single check: the
    /// OS reaps process-group members asynchronously relative to setup.sh's own EXIT trap having
    /// already run by the time <see cref="RunSetupSendingSigintMidLaunch"/> returns.</summary>
    static bool ProcessGroupIsEmpty(int pgid)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (Kill(-pgid, 0) != 0 && Marshal.GetLastWin32Error() == Esrch) return true;
            Thread.Sleep(100);
        }
        return false;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — wrapped, never re-implemented (AC1)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheWizardWrapsLaunch
    {
        [Fact]
        public void TheWizardInvokesLaunchShWithTheStagedShape()
        {
            // Bare invocation, no topology flags — GW_PRESET (just written to .env) IS the
            // topology (F132.5); the wizard's own contract is a bare ./launch.sh (T317 smoke,
            // the T318 task note's explicit ruling: no --no-launch escape hatch, no flags).
            var argvLog = Path.Combine(
                Directory.CreateTempSubdirectory("gw-setup-story345-argv-").FullName, "argv.log");
            var launchStub = WriteLaunchStub(exitCode: 0, argvLogPath: argvLog);
            // B2 (round-3 review): the stale-mount gate is now universal — a mount already
            // serving on the FIRST poll is indistinguishable from a stale/pre-existing stack, so
            // a genuine happy-path fixture must 404 once before serving (see ScenarioTheStaleMountGate).
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30));

            Assert.Equal("argv:", File.ReadAllText(argvLog).Trim());
        }

        [Fact]
        public void NoComposeInvocationOriginatesFromSetupSh()
        {
            // No `docker` binary anywhere on this run's PATH at all: if any setup.sh code path
            // called docker/compose directly (rather than leaving that entirely to launch.sh),
            // the script would abort under `set -e` on "command not found" and never reach
            // on-air — reaching it here is the proof.
            var launchStub = WriteLaunchStub(exitCode: 0);
            // B2 (round-3 review): the stale-mount gate is now universal — a genuine happy-path
            // fixture must 404 once before serving (see ScenarioTheStaleMountGate).
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (exitCode, stdOut, stdErr) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, 30));

            Assert.True(exitCode == 0 && stdOut.Contains("On air", StringComparison.Ordinal),
                $"expected a clean run with no direct docker/compose call from setup.sh; exit={exitCode} stderr={stdErr} stdout={stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the clock instrument (AC2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheClockInstrument
    {
        [Fact]
        public void FirstAudioPrintsOnAirInMinutesSeconds()
        {
            // The mount stub 404s on the first poll and serves on the second — proves this is
            // real repeated polling, not a one-shot check that happens to pass.
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (_, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, 30));

            Assert.Matches(new Regex(@"🎙️ On air in \d+:\d{2}"), stdOut);
        }

        [Fact]
        public void TheTimingLineIsAppendedToTheSetupLog()
        {
            var launchStub = WriteLaunchStub(exitCode: 0);
            // B2 (round-3 review): the stale-mount gate is now universal — a genuine happy-path
            // fixture must 404 once before serving (see ScenarioTheStaleMountGate).
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envDir = ScratchEnvDir();
            var envFile = Path.Combine(envDir, ".env");

            RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, 30));

            var logLine = File.ReadAllText(Path.Combine(envDir, "setup.log")).Trim();
            Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z on-air=\d+:\d{2} preset=home$"), logLine);
        }

        [Fact]
        public async Task TheClockStartsAtTheFirstPromptNotAtLaunch()
        {
            // A deliberate delay BEFORE the first answer is sent, injected into the real
            // process interaction (not RunSetup's fire-and-forget stdin write) — the printed
            // duration must be at least as long as this delay, proving t0 is stamped at the
            // interview's start rather than at launch or the mount poll.
            const int delaySeconds = 3;
            var launchStub = WriteLaunchStub(exitCode: 0);
            // B2 (round-3 review): the stale-mount gate is now universal — a genuine happy-path
            // fixture must 404 once before serving (see ScenarioTheStaleMountGate).
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var startInfo = new ProcessStartInfo("bash")
            {
                WorkingDirectory = RepoRoot(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), "setup.sh"));
            startInfo.Environment["PATH"] = BinWithoutDotnet();
            foreach (var name in RequiredEnvVars) startInfo.Environment.Remove(name);
            foreach (var name in SeamEnvVars) startInfo.Environment.Remove(name);
            startInfo.Environment["GW_ENV_FILE"] = envFile;
            foreach (var (key, value) in BaseEnv(launchStub, mount.Url, 30))
                startInfo.Environment[key] = value;

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("failed to start setup.sh");

            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));

            try
            {
                process.StandardInput.Write($"{mediaDir}\n1\ny\n");
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Child already exited — nothing left to write to.
            }

            var stdOut = await stdOutTask;
            _ = await stdErrTask;
            process.WaitForExit();

            var match = Regex.Match(stdOut, @"🎙️ On air in (\d+):(\d{2})");
            Assert.True(match.Success, $"expected an On-air clock line; stdout:\n{stdOut}");
            var elapsed = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.True(elapsed >= delaySeconds,
                $"expected the clock to include the {delaySeconds}s pre-answer delay; got {elapsed}s");
        }

        [Fact]
        public void OnAirIsStampedWhileLaunchIsStillRunningNotAfterItReturns()
        {
            // T318 review round-1 BLOCKING finding F1's reproduction, now fixed: a stub that
            // airs the mount at t=armAfterSeconds but doesn't EXIT until totalRuntimeSeconds
            // used to print "On air in 0:0X" measured off the stub's own (much later) return —
            // the old design only started polling once launch.sh had already exited. The
            // printed M:SS must be well under the stub's total runtime, proving the poller ran
            // CONCURRENTLY with launch.sh instead of only starting once it returned.
            using var mount = new ArmableMountStub();
            const int armAfterSeconds = 2;
            const int totalRuntimeSeconds = 6;
            var launchStub = WriteLaunchStubThatArmsMountPartway(mount.MarkerPath, armAfterSeconds, totalRuntimeSeconds);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (_, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30));

            var match = Regex.Match(stdOut, @"🎙️ On air in (\d+):(\d{2})");
            Assert.True(match.Success, $"expected an On-air clock line; stdout:\n{stdOut}");
            var elapsed = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.True(elapsed < totalRuntimeSeconds,
                $"expected the clock to reflect the mid-launch on-air moment ({armAfterSeconds}s), not launch.sh's full {totalRuntimeSeconds}s runtime; got {elapsed}s");
        }

        [Fact]
        public void AProgressLineAppearsAtDetectionAndTheAuthoritativeLineStillPrintsTheStampedSmallerTime()
        {
            // UX addition ratified at the round-2 review: a subordinate progress line from the
            // poller fires the moment audio is first detected — distinct wording from, and
            // strictly BEFORE, the authoritative "🎙️ On air" claim (only main() ever prints
            // that, only once it has confirmed launch.sh's own exit code) — so an owner on a
            // fresh Pi isn't left staring at a pull log for minutes while already live. The
            // authoritative line must still reflect the small stamped-at-detection elapsed, not
            // a later re-measurement off the stub's full runtime.
            using var mount = new ArmableMountStub();
            const int armAfterSeconds = 2;
            const int totalRuntimeSeconds = 6;
            var launchStub = WriteLaunchStubThatArmsMountPartway(mount.MarkerPath, armAfterSeconds, totalRuntimeSeconds);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (_, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30));

            var progressIndex = stdOut.IndexOf("audio detected at", StringComparison.Ordinal);
            var match = Regex.Match(stdOut, @"🎙️ On air in (\d+):(\d{2})");
            var elapsed = match.Success
                ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60
                    + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
                : -1;

            Assert.True(
                progressIndex >= 0 && match.Success && progressIndex < match.Index && elapsed < totalRuntimeSeconds,
                $"expected a detection-time progress line before the authoritative On-air line, still showing the small stamped elapsed; stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the stale-mount gate: a mount already serving before THIS run's own launch
    // even began is never trusted until a non-serving gap is observed — UNIVERSAL across every
    // preset (round-3 review BLOCKING finding B2 — the reviewer reproduced the hole a dev-only
    // gate left open: home preset, mount already serving from a stale/unrelated stack, 20s
    // launch stub -> a fabricated "On air in 0:00" landing straight in setup.log). "Immediate
    // 200 = success" is a claim about TIMING, never about which preset is running: no preset's
    // own flow can have a container THIS run started already serving audio at t≈0 (home's own
    // first act is compose pull -> db -> migrate -> up).
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheStaleMountGate
    {
        [Fact]
        public void ADevPresetLaunchNeverStampsOnAirAgainstAPreviouslyServingMount()
        {
            // dev/dev-piper-only presets run launch.sh's dev flow, which tears the previous
            // stack down FIRST (`compose down --remove-orphans`) before bringing anything back
            // up. A mount already serving before this run's own launch even started can only be
            // evidence of that previous stack — this stub never stops serving (no teardown
            // simulated here), so the only honest outcome is a poll timeout, never "On air".
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 1);   // already serving from the start
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            // "2" -> build-from-source (IMAGES_MODE=dev) -> GW_PRESET=dev with topology "1".
            var (exitCode, stdOut, _) = RunSetup(BinWithDotnet10Sdk(), envFile, $"2\n{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 3));

            Assert.True(
                exitCode != 0 && !stdOut.Contains("On air", StringComparison.Ordinal),
                $"expected a dev-preset launch to never trust a mount already serving before it started; exit={exitCode} stdout={stdOut}");
        }

        [Fact]
        public void AHomePresetNeverStampsAgainstAMountThatWasServingBeforeTheLaunchBegan()
        {
            // B2's actual reproduction: a home preset (pinned images, the wizard's default) has
            // NO teardown-first flow at all, and the OLD dev-only gate treated that as license
            // to trust an immediate 200 unconditionally — but home's own launch.sh flow can no
            // more have started serving audio at t≈0 than dev's can (its first act is compose
            // pull -> db -> migrate -> up). A mount already serving before this run's own launch
            // even started is stale evidence regardless of preset; this stub never stops
            // serving, so the only honest outcome is a poll timeout, never a fabricated "On air".
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 1);   // already serving from the start
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (exitCode, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 3));

            Assert.True(
                exitCode != 0 && !stdOut.Contains("On air", StringComparison.Ordinal),
                $"expected a home preset to never trust a mount already serving before its own launch started; exit={exitCode} stdout={stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the handoff screen (AC3)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheHandoffScreen
    {
        static (string StdOut, string EnvContent) RunHappyPath(string topologyAnswer)
        {
            var launchStub = WriteLaunchStub(exitCode: 0);
            // B2 (round-3 review): the stale-mount gate is now universal — a genuine happy-path
            // fixture must 404 once before serving (see ScenarioTheStaleMountGate).
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (_, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n{topologyAnswer}\ny\n",
                BaseEnv(launchStub, mount.Url, 30));

            return (stdOut, File.ReadAllText(envFile));
        }

        static readonly Lazy<(string StdOut, string EnvContent)> Run = new(() => RunHappyPath(topologyAnswer: "1"));

        [Fact]
        public void TheHandoffShowsTheAdminUrl()
        {
            Assert.Matches(new Regex(@"http://\S+:3000/"), Run.Value.StdOut);
        }

        [Fact]
        public void TheAdminUrlUsesLocalhostExplicitly()
        {
            // T318 review LOW finding F6: the bare short hostname (e.g. "http://thor:3000/")
            // resolves on nobody's machine but this one — localhost must be printed explicitly,
            // not merely SOME host:port string (TheHandoffShowsTheAdminUrl's looser regex would
            // pass on the old bare-hostname bug too).
            Assert.Contains("http://localhost:3000/", Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void TheGeneratedAdminPasswordAppearsExactlyOnce()
        {
            var password = ReadEnvValue(Run.Value.EnvContent, "ADMIN_PASSWORD");

            Assert.Single(Regex.Matches(Run.Value.StdOut, Regex.Escape(password)));
        }

        [Fact]
        public void AnAmbientAdminPasswordEnvVarNeverLeaksIntoTheHandoff()
        {
            // T318 review BLOCKING finding F2: the old code read ADMIN_PASSWORD back off .env
            // via preflight_env_value, whose precedence is process-env-wins — an ambient
            // ADMIN_PASSWORD exported in the CALLER's shell would print instead of the one this
            // run actually generated and wrote. SeamEnvVars scrubs ADMIN_PASSWORD from every
            // other fact in this file; this one sets it back on purpose (extraEnv is applied
            // AFTER the scrub in RunSetup) to prove the ambient value never wins.
            const string ambientPassword = "AMBIENT-VALUE-MUST-NEVER-APPEAR";
            var launchStub = WriteLaunchStub(exitCode: 0);
            // B2 (round-3 review): the stale-mount gate is now universal — a genuine happy-path
            // fixture must 404 once before serving (see ScenarioTheStaleMountGate).
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();
            var extraEnv = new Dictionary<string, string>(BaseEnv(launchStub, mount.Url, 30))
            {
                ["ADMIN_PASSWORD"] = ambientPassword,
            };

            var (_, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n", extraEnv);
            var generatedPassword = ReadEnvValue(File.ReadAllText(envFile), "ADMIN_PASSWORD");

            Assert.True(
                stdOut.Contains(generatedPassword, StringComparison.Ordinal) &&
                !stdOut.Contains(ambientPassword, StringComparison.Ordinal),
                $"expected the handoff to show the GENERATED password, never the ambient one; stdout:\n{stdOut}");
        }

        [Fact]
        public void ThePersonaShelfDeepLinkAppears()
        {
            Assert.Contains("/persona-catalog", Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void WhatIsStillArrivingIsNamed()
        {
            // Default topology answer ("1") -> full/home -> kokoro is what's still arriving.
            // Asserted against print_handoff's own unique wording, not the topology menu's own
            // "kokoro" mention (printed regardless of what this fact is proving).
            Assert.Contains("TTS voice model", Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void WhatIsStillArrivingNamesPiperOnAPiperOnlyPreset()
        {
            // The T316 review lesson, pinned: a piper-only run must never claim kokoro is
            // coming — it must be derived from the CHOSEN preset, never hardcoded.
            var (stdOut, _) = RunHappyPath(topologyAnswer: "2");

            Assert.Contains("Hugging Face model download", stdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void TheExactNextRunCommandsAreShown()
        {
            Assert.True(
                Run.Value.StdOut.Contains("./launch.sh", StringComparison.Ordinal) &&
                Run.Value.StdOut.Contains("./setup.sh", StringComparison.Ordinal),
                $"expected both next-run commands on the handoff; stdout:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void TheSetupRerunLineDoesNotOverclaimVerification()
        {
            // T318 review LOW finding F5: "re-run any time to verify this install" promised a
            // guided verify that setup_adoption_mode does not deliver on this branch (it is
            // STORY-346's stub) — the line must be worded for what it actually does today and
            // point at where the real thing lands.
            Assert.True(
                !Run.Value.StdOut.Contains("re-run any time to verify this install", StringComparison.Ordinal) &&
                Run.Value.StdOut.Contains("STORY-346", StringComparison.Ordinal),
                $"expected the re-run line to be worded truthfully for this slice; stdout:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void KokoroWordingOnACleanExitReflectsThatItIsAlreadyUpNotStillPulling()
        {
            // T318 review BLOCKING finding F3: at handoff time launch.sh HAS already returned —
            // on a clean exit (0) stage 2 actually pulled AND started kokoro, so "still pulling"
            // would be stale news. The honest claim there is warm-up/initialization.
            Assert.Contains("warming up", Run.Value.StdOut, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — B1 (round-3 review): a `hostname` that can't answer `-I` must never abort
    // the handoff before the once-only password print
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioAHostnameThatCannotReportItsLanAddress
    {
        [Fact]
        public void TheHandoffStillPrintsThePasswordAndNextRunLinesAndExitsZero()
        {
            // `hostname -I` is a GNU-ism — on busybox/Alpine/macOS `command -v hostname`
            // succeeds but `-I` exits 1; under `set -euo pipefail` an unguarded
            // `lan_addr="$(primary_lan_address)"` used to trip errexit and kill the script
            // mid-handoff, silently, right after the admin URL line — the password, the
            // persona-shelf link, and the next-run commands never printed, and a fully
            // successful launch reported exit 1 (which T323's install.sh would read as failure).
            var launchStub = WriteLaunchStub(exitCode: 0);
            // B2 (round-3 review): the stale-mount gate is now universal — a genuine happy-path
            // fixture must 404 once before serving (see ScenarioTheStaleMountGate).
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (exitCode, stdOut, stdErr) = RunSetup(BinWithBrokenHostname(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, 30));
            var password = ReadEnvValue(File.ReadAllText(envFile), "ADMIN_PASSWORD");

            Assert.True(
                exitCode == 0 &&
                stdOut.Contains(password, StringComparison.Ordinal) &&
                stdOut.Contains("./launch.sh", StringComparison.Ordinal) &&
                stdOut.Contains("./setup.sh", StringComparison.Ordinal),
                $"expected the handoff to complete (password + next-run lines) and exit 0 even when hostname -I fails; exit={exitCode} stdout={stdOut} stderr={stdErr}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the T316 rider: exit 4 is DEGRADED-BUT-AIRING, not a failure
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioLaunchDegradedButAiring
    {
        static (int ExitCode, string StdOut) RunDegraded(string topologyAnswer, string adminAnswer = "y")
        {
            var launchStub = WriteLaunchStub(exitCode: 4);
            // B2 (round-3 review): the stale-mount gate is now universal — a genuine happy-path
            // fixture must 404 once before serving (see ScenarioTheStaleMountGate).
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (exitCode, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n{topologyAnswer}\n{adminAnswer}\n",
                BaseEnv(launchStub, mount.Url, 30));
            return (exitCode, stdOut);
        }

        static readonly Lazy<(int ExitCode, string StdOut)> Run = new(() => RunDegraded(topologyAnswer: "1"));

        /// <summary>Everything from the "🎙️ On air" line onward — excludes the interview's OWN
        /// static prompt text (Q3's topology menu mentions "kokoro" in both options, which would
        /// otherwise false-positive any "does the output ever mention kokoro" check against the
        /// full transcript).</summary>
        static string PostOnAirSection(string stdOut)
        {
            var index = stdOut.IndexOf("🎙️ On air", StringComparison.Ordinal);
            return index >= 0 ? stdOut[index..] : "";
        }

        [Fact]
        public void OnAirStillPrintsWhenLaunchExitsDegraded()
        {
            Assert.Matches(new Regex(@"🎙️ On air in \d+:\d{2}"), Run.Value.StdOut);
        }

        [Fact]
        public void TheHandoffNamesTheDegradationAndTheCatchUpCommand()
        {
            Assert.True(
                Run.Value.StdOut.Contains("DEGRADED", StringComparison.Ordinal) &&
                Run.Value.StdOut.Contains("./launch.sh", StringComparison.Ordinal),
                $"expected the handoff to name the degradation and the catch-up command; stdout:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void ExitCodeIsFourAfterADegradedButAiringLaunch()
        {
            // T318 review BLOCKING finding F4: the old code accepted launch.sh's exit 4 and
            // then always exited 0 itself (main() ran to completion) — install.sh (T323) execs
            // this script, so a degraded install used to report a clean success. setup.sh's own
            // exit code must propagate launch.sh's.
            Assert.Equal(4, Run.Value.ExitCode);
        }

        [Fact]
        public void TheDegradedBannerNeverNamesOllamaOrCaddy()
        {
            // T318 review BLOCKING finding F3 (+F11 parity): this wizard never stacks
            // compose.demo.yaml (GW_PREFLIGHT_DEMO_VALUE is hardcoded "0" in
            // resolve_preset_and_topology) — ollama and caddy exist only there, so a
            // wizard-driven catch-up banner must never name either, on ANY preset. Scoped to the
            // post-on-air section — Q3's own topology menu text mentions "kokoro", which is not
            // this fact's concern.
            var postOnAir = PostOnAirSection(Run.Value.StdOut);

            Assert.True(
                !postOnAir.Contains("ollama", StringComparison.Ordinal) &&
                !postOnAir.Contains("caddy", StringComparison.Ordinal),
                $"expected the DEGRADED banner to never name ollama/caddy; stdout:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void TheDegradedBannerNamesOnlyPiperWhenTheAdminUiIsDeclinedOnAPiperOnlyPreset()
        {
            // A piper-only preset has no kokoro to catch up on at all (F3) — declining the
            // Admin UI too leaves nothing wizard-composed left to name. Scoped to the
            // post-on-air section for the same reason as TheDegradedBannerNeverNamesOllamaOrCaddy.
            var (_, stdOut) = RunDegraded(topologyAnswer: "2", adminAnswer: "n");
            var postOnAir = PostOnAirSection(stdOut);

            Assert.True(
                !postOnAir.Contains("kokoro", StringComparison.Ordinal) &&
                postOnAir.Contains("nothing beyond the core", StringComparison.Ordinal),
                $"expected a piper-only + no-admin DEGRADED banner to name nothing beyond the core; stdout:\n{stdOut}");
        }

        [Fact]
        public void TheDegradedBannerNamesAdminUiWhenTheProfileIsOn()
        {
            // extras_desc (F3): the only profile-gated extra this wizard can actually select
            // (Q4 is its only profile question) is named when chosen.
            Assert.Contains("admin_ui", Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void KokoroWordingOnADegradedExitReflectsThatItMayNotHaveConverged()
        {
            // T318 review BLOCKING finding F3: on exit 4 the catch-up stage genuinely may not
            // have converged — the "pulling and/or initializing" wording stays accurate there,
            // unlike the exit-0 "already up" wording (see ScenarioTheHandoffScreen's own fact).
            Assert.Contains("pulling and/or initializing", Run.Value.StdOut, StringComparison.Ordinal);
        }

        // -------------------------------------------------------------------
        // N3 (round-3 review): heavyweights_desc/extras_desc were pinned only by NEGATIVE
        // "never ollama/caddy" assertions above — these facts pin the exact POSITIVE joined
        // string setup.sh derives per preset x admin combination, so any drift in that mapping
        // (vs. launch.sh's own HEAVYWEIGHTS_DESC/EXTRAS_DESC, which this script may not edit)
        // fails a fact here instead of drifting silently.
        // -------------------------------------------------------------------

        [Fact]
        public void CatchupDescribesKokoroThenAdminUiOnFullTopologyWithAdminEnabled()
        {
            // Full topology ("1") + Admin UI on ("y", Run's own default) -> heavyweights_desc
            // "kokoro" joined with extras_desc "admin_ui". F5 (round-4 review): scoped to the
            // post-on-air section, like its three siblings above — Q3's own topology menu text
            // mentions "kokoro", which would otherwise false-positive this fact against the
            // interview transcript rather than the DEGRADED banner itself.
            var postOnAir = PostOnAirSection(Run.Value.StdOut);

            Assert.Contains("(kokoro, admin_ui)", postOnAir, StringComparison.Ordinal);
        }

        [Fact]
        public void CatchupDescribesKokoroAloneOnFullTopologyWithAdminDeclined()
        {
            var (_, stdOut) = RunDegraded(topologyAnswer: "1", adminAnswer: "n");
            var postOnAir = PostOnAirSection(stdOut);

            Assert.Contains("(kokoro)", postOnAir, StringComparison.Ordinal);
        }

        [Fact]
        public void CatchupDescribesAdminUiAloneOnPiperOnlyTopologyWithAdminEnabled()
        {
            var (_, stdOut) = RunDegraded(topologyAnswer: "2", adminAnswer: "y");
            var postOnAir = PostOnAirSection(stdOut);

            Assert.Contains("(admin_ui)", postOnAir, StringComparison.Ordinal);
        }

        [Fact]
        public void CatchupDescribesNothingBeyondTheCoreOnPiperOnlyTopologyWithAdminDeclined()
        {
            var (_, stdOut) = RunDegraded(topologyAnswer: "2", adminAnswer: "n");
            var postOnAir = PostOnAirSection(stdOut);

            Assert.Contains("(nothing beyond the core)", postOnAir, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — any other nonzero launch exit is a genuine failure
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioLaunchFailsOutright
    {
        const int FailureExitCode = 7;   // any code other than 0/4 — an arbitrary launch.sh crash

        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run = new(() =>
        {
            var launchStub = WriteLaunchStub(exitCode: FailureExitCode);
            using var mount = new MountStub(servesOnAttempt: 1);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            return RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, 30));
        });

        [Fact]
        public void NoOnAirLineWhenLaunchFailsOutright()
        {
            Assert.DoesNotContain("On air", Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void ExitCodeIsNonzeroAndPointsAtDiagnostics()
        {
            Assert.True(
                Run.Value.ExitCode == FailureExitCode &&
                Run.Value.StdErr.Contains("docker compose", StringComparison.Ordinal),
                $"expected exit {FailureExitCode} with a diagnostics pointer; exit={Run.Value.ExitCode} stderr={Run.Value.StdErr}");
        }

        [Fact]
        public void AHardLaunchFailureKillsThePollerRatherThanWaitingOutItsFullBudget()
        {
            // T318 review BLOCKING finding F1: the poller's own budget (onAirTimeoutSeconds) is
            // set far longer than this launch stub's short runtime — if a hard launch failure
            // didn't kill the background poller, setup.sh would block until the mount poll's own
            // timeout before ever reporting the launch failure. No orphan poller either way.
            const int failureExitCode = 9;
            var launchStub = WriteDelayedLaunchStub(sleepSeconds: 1, exitCode: failureExitCode);
            using var mount = new MountStub(servesOnAttempt: int.MaxValue);   // never serves
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var stopwatch = Stopwatch.StartNew();
            var (exitCode, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 60));
            stopwatch.Stop();

            Assert.True(
                exitCode == failureExitCode && !stdOut.Contains("On air", StringComparison.Ordinal)
                    && stopwatch.Elapsed.TotalSeconds < 30,
                $"expected a prompt failure (not a 60s poll-timeout wait) with no On-air line; exit={exitCode} elapsed={stopwatch.Elapsed.TotalSeconds}s stdout={stdOut}");
        }

        [Fact]
        public void AHardFailureAfterAudioWasAlreadyServingStillPrintsNoOnAirLine()
        {
            // The ordering race T318 review finding F1 explicitly calls out: the mount can
            // satisfy the on-air condition before launch.sh's own hard failure is observed
            // (audio already flowing, then something else in the launch still goes wrong) —
            // even then, no success handoff, no "On air" line, nonzero exit.
            const int failureExitCode = 9;
            var launchStub = WriteDelayedLaunchStub(sleepSeconds: 2, exitCode: failureExitCode);
            using var mount = new MountStub(servesOnAttempt: 1);   // serving well before the failure
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (exitCode, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30));

            Assert.True(
                exitCode == failureExitCode && !stdOut.Contains("On air", StringComparison.Ordinal),
                $"expected no On-air line even though audio was already flowing when launch.sh failed hard; exit={exitCode} stdout={stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — F1 (round-4 review, BLOCKING): the operator's own Ctrl-C must never be blamed
    // on the stack, even when it lands mid-launch and the launch stub itself swallows the signal
    // (the reviewer's own repro shape)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioOperatorAbortsMidLaunch
    {
        [Fact]
        public void ASigintToTheProcessGroupMidLaunchIsAnOperatorAbortNotAStackFailure()
        {
            // A trap installed only AROUND the poller join (after invoke_launch returns) misses
            // a SIGINT that lands while bash is blocked on invoke_launch itself, a FOREGROUND
            // child — bash defers that signal until the child exits, and when the child (like
            // this stub) traps and swallows it rather than dying, the deferred signal used to be
            // consumed by the very NEXT `wait` builtin with no trap yet live to catch it —
            // landing in the poll-timeout branch and blaming the stack for the operator's own
            // Ctrl-C. The fix installs the trap BEFORE invoke_launch. This fact sends SIGINT to
            // the whole process GROUP (the real shape a terminal Ctrl-C delivers) while the
            // launch stub is still mid-sleep, and the stub itself swallows the signal
            // (`trap '' INT`) and exits 4 on its own — exactly the reviewer's "compose-like
            // child traps INT ... launch swallows it and exits 4" shape.
            //
            // round-4 review N1: the mount is scripted to serve from the SECOND poll onward
            // (404 once, per the universal stale-mount gate, then 200) rather than never — the
            // background poller therefore stamps a genuine on-air success within about a second
            // of starting, well BEFORE the signal below fires. This is deliberate: a
            // join-scoped-trap mutant (round-3's own shape) would, in this exact scenario, reach
            // its `wait "$SETUP_POLLER_PID"` call against an ALREADY-finished, already-stamped
            // poller and see a clean join (poller_exit 0) instead of an interruption — producing
            // a fabricated "On air in 0:00" and exit 0. Only code that recognizes the interrupt
            // BEFORE ever reaching that join (this fix's whole point) discards the stamp and
            // reports the abort instead. A mount that never serves at all (the previous shape of
            // this fact) can't tell the two apart, because the poller then never finishes on its
            // own either way. `totalRuntimeSeconds` is generous so the launch stub is still
            // reliably mid-sleep — genuinely "mid-launch" — when the signal fires.
            const int sendAfterSeconds = 4;
            const int totalRuntimeSeconds = 10;
            var launchStub = WriteLaunchStubThatSwallowsInt(totalRuntimeSeconds, exitCode: 4);
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envDir = ScratchEnvDir();
            var envFile = Path.Combine(envDir, ".env");

            var (exitCode, stdOut, stdErr, pgid) = RunSetupSendingSigintMidLaunch(
                BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30),
                sendAfter: TimeSpan.FromSeconds(sendAfterSeconds));

            Assert.True(
                exitCode == 130 &&
                stdErr.Contains("pressed Ctrl-C", StringComparison.Ordinal) &&
                !stdErr.Contains("never proved it's actually broadcasting", StringComparison.Ordinal) &&
                !stdOut.Contains("On air", StringComparison.Ordinal) &&
                !File.Exists(Path.Combine(envDir, "setup.log")) &&
                ProcessGroupIsEmpty(pgid),
                $"expected an operator-abort exit (130), the abort message (not the timeout message), no On-air line, no setup.log entry, and no orphan processes; exit={exitCode} stdout={stdOut} stderr={stdErr}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the mount never serves
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheMountNeverServes
    {
        [Fact]
        public void APollTimeoutReportsHonestlyInsteadOfClaimingAir()
        {
            // No "On air" line; the wizard points at diagnostics and exits nonzero.
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: int.MaxValue);   // never serves
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (exitCode, stdOut, stdErr) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 3));

            Assert.True(
                exitCode != 0 && !stdOut.Contains("On air", StringComparison.Ordinal),
                $"expected a nonzero exit and no On-air line after a poll timeout; exit={exitCode} stdout={stdOut} stderr={stdErr}");
        }

        [Fact]
        public void APollTimeoutStillPointsAtTheEnvFileAndTheAdminUrl()
        {
            // T318 review LOW finding F7: the stack may well be up regardless of an unproven
            // stream — the operator still needs a way in, so the timeout path names where the
            // secrets live and the Admin UI address rather than leaving them stranded.
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: int.MaxValue);   // never serves
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (_, _, stdErr) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 3));

            Assert.True(
                stdErr.Contains(envFile, StringComparison.Ordinal) &&
                stdErr.Contains("http://localhost:3000/", StringComparison.Ordinal),
                $"expected the poll-timeout message to name the .env path and the admin URL; stderr:\n{stdErr}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — N4 (round-3 review): no prober at all is a DIFFERENT fact than "never proved
    // it's airing" — never a fabricated timing claim, and never a hardcoded failure exit that
    // masks launch.sh's own genuine verdict
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioNoProberAvailable
    {
        [Fact]
        public void ACurlLessBoxShowsTheFullHandoffWithoutAnOnAirClaimAndExitsWithLaunchsOwnCode()
        {
            // A box without curl used to exit 1 even when launch.sh's own launch had genuinely
            // succeeded, conflating "no prober available" with "never aired". The fix: the full
            // handoff (secrets, URLs, next steps) still prints, minus any "On air in M:SS" claim
            // and minus the setup.log timing line, and the exit code is launch.sh's own (0 here).
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 1);   // never actually queried — curl is absent
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envDir = ScratchEnvDir();
            var envFile = Path.Combine(envDir, ".env");

            var (exitCode, stdOut, stdErr) = RunSetup(BinWithoutCurl(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, 30));
            var password = ReadEnvValue(File.ReadAllText(envFile), "ADMIN_PASSWORD");
            var setupLogPath = Path.Combine(envDir, "setup.log");

            Assert.True(
                exitCode == 0 &&
                !stdOut.Contains("On air", StringComparison.Ordinal) &&
                stdOut.Contains(password, StringComparison.Ordinal) &&
                stdOut.Contains("./launch.sh", StringComparison.Ordinal) &&
                stdErr.Contains("curl not found", StringComparison.Ordinal) &&
                !File.Exists(setupLogPath),
                $"expected the full handoff minus any On-air claim, launch.sh's own exit code, and no setup.log line; exit={exitCode} stdout={stdOut} stderr={stdErr}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a garbage GW_ONAIR_TIMEOUT_SECONDS fails loudly at startup
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioAGarbageOnAirTimeout
    {
        [Fact]
        public void AGarbageOnAirTimeoutFailsLoudlyBeforeTheInterviewEvenStarts()
        {
            // T318 review MEDIUM finding F8: a SET-but-non-numeric GW_ONAIR_TIMEOUT_SECONDS
            // used to go unvalidated straight into `$(( ))` arithmetic deep inside the poller —
            // it must instead fail loudly at parse time, before a single question is asked.
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 1);
            var envFile = ScratchEnvPath();
            var extraEnv = new Dictionary<string, string>(BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30))
            {
                ["GW_ONAIR_TIMEOUT_SECONDS"] = "not-a-number",
            };

            // No stdin answers at all — if this reached the interview it would hit EOF and
            // abort with a DIFFERENT message than the one this fact is proving.
            var (exitCode, stdOut, stdErr) = RunSetup(BinWithoutDotnet(), envFile, "", extraEnv);

            Assert.True(
                exitCode != 0 &&
                stdErr.Contains("GW_ONAIR_TIMEOUT_SECONDS", StringComparison.Ordinal) &&
                !stdOut.Contains("1) How should GenWave run?", StringComparison.Ordinal),
                $"expected an immediate, loud failure before the interview started; exit={exitCode} stdout={stdOut} stderr={stdErr}");
        }
    }
}
