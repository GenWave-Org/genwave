// STORY-405 — The wizard installs the first background-music pack (SPEC F165.7 · PLAN T428)
//
// BDD specification — xUnit. Drives the REAL ./setup.sh via ScriptProcess (gh-#776), which
// always starts the child from a sanitized environment (ambient GW_*/SKIP_PREFLIGHT/COMPOSE_*/
// secrets scrubbed by construction — see ScriptProcess.IsStripped) regardless of what the parent
// shell happens to export. Seam choice (R2's own open question, ruled here): the "local api"
// install_first_beds talks to is a scratch Kestrel loopback instance (ApiStub, below), never a
// stubbed `curl` binary — curl itself stays the REAL binary on every scenario's PATH throughout
// this file. Chosen over stubbing curl because that is the idiom every sibling wizard-spec file
// already uses for "the wizard calls out to a local HTTP service it doesn't own" — Story345's own
// MountStub/ArmableMountStub play the identical role for the icecast mount poll, and setup.sh's
// own header already documents GW_API_URL as this step's ONE seam (curl is never swapped out).
// Pointing that seam at a real loopback server proves the ACTUAL curl invocations setup.sh
// emits reach the right method/path/body, rather than merely proving a stub's own argv shape.
//
// Harness: RunSetup spools stdinAnswers to a scratch file and runs a one-line wrapper
// (`exec bash setup.sh "$@" < answers.txt`) through ScriptProcess.Run rather than writing to a
// live pipe — behaviorally identical here since every fact writes its whole answer transcript
// upfront (bash hitting EOF at the end of a redirected file reads the same as a closed pipe).
//
// House rule: one assert per Fact — a couple of facts assert one combined boolean via a single
// Assert.True(...) call where the observation is genuinely one logical fact (several conditions
// that only mean something together), the same idiom Story344/345/346 already use.

using System.Globalization;
using System.Net;
using System.Text.Json;
using GenWave.Host.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// A scratch icecast-mount stand-in — the Story345 MountStub idiom, copied verbatim (T318's own
/// harness-dedup rider): Kestrel on port 0, 404 until the <paramref name="servesOnAttempt"/>'th
/// request, HTTP 200 + a small nonzero body forever after.
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
/// A scratch stand-in for the two admin endpoints install_first_beds calls — Kestrel on port 0
/// (the same MountStub idiom above), a single dispatch-by-path handler. Records the call ORDER
/// and the password login actually received (never a pinned literal: apply_generate_secrets
/// always mints a fresh random ADMIN_PASSWORD per run, so a fact can only compare what this stub
/// captured against whatever THAT SAME run's own .env holds afterwards). The install call
/// answers with a configurable status/asset count so one class covers every response shape
/// install_first_beds branches on.
/// </summary>
file sealed class ApiStub : IDisposable
{
    readonly WebApplication app;
    readonly object gate = new();
    readonly List<string> calls = [];
    readonly int installStatus;
    readonly int installAssetCount;

    public string? CapturedPassword { get; private set; }

    public IReadOnlyList<string> Calls
    {
        get { lock (gate) return calls.ToArray(); }
    }

    public string Url { get; }

    public ApiStub(int installStatus = 200, int installAssetCount = 3)
    {
        this.installStatus = installStatus;
        this.installAssetCount = installAssetCount;

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();

        app.Run(async ctx =>
        {
            var path = ctx.Request.Path.Value ?? "";

            if (path == "/api/auth/login")
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                using var doc = JsonDocument.Parse(body);
                lock (gate) calls.Add("login");
                CapturedPassword = doc.RootElement.GetProperty("password").GetString();
                ctx.Response.StatusCode = 200;
                return;
            }

            if (path == "/api/jingle-packs/gw-first-beds/install")
            {
                lock (gate) calls.Add("install");
                ctx.Response.StatusCode = this.installStatus;
                if (this.installStatus == 200)
                {
                    ctx.Response.ContentType = "application/json";
                    var assets = string.Join(",", Enumerable.Range(0, this.installAssetCount)
                        .Select(i => $$"""{"file":"seed-{{i}}.wav","role":"bed","mediaId":{{i + 1}}}"""));
                    await ctx.Response.WriteAsync(
                        $$"""{"slug":"gw-first-beds","packName":"First Beds","assets":[{{assets}}]}""");
                }
                return;
            }

            // Anything else (a wrong path/method) never counts as either named call above —
            // a URL-shape regression in install_first_beds shows up as a missing "login"/
            // "install" entry in Calls, not as a false positive here.
            ctx.Response.StatusCode = 404;
        });

        app.Start();
        Url = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First()
            .TrimEnd('/');
    }

    public void Dispose()
    {
        app.StopAsync().GetAwaiter().GetResult();
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

public static class FeatureTheWizardInstallsTheFirstBeds
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared harness (gh-#776: setup.sh runs through ScriptProcess, which owns the scratch PATH
    // and sanitized-environment construction — see ScriptProcess.cs)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A bin dir with every <see cref="ScriptProcess.MakeBinDir"/> default tool except
    /// curl — the "curl missing from this machine entirely" shape fact (e) needs. Also drives
    /// wait_for_on_air_bg's own no-prober (poller_exit 2) degrade, which — per Story345's own
    /// N4 fact — still falls through to main()'s tail rather than exiting early, which is
    /// exactly the path this file's fact (e) needs to reach install_first_beds at all.
    /// ScriptProcess.MakeBinDir's default toolset includes curl (gh-#776's shared superset
    /// across every migrated spec), so this fixture deletes the symlink after building rather
    /// than omitting it during construction.</summary>
    static string BinWithoutCurl()
    {
        var dir = ScriptProcess.MakeBinDir();
        File.Delete(Path.Combine(dir, "curl"));
        return dir;
    }

    /// <summary>A bin dir whose `curl` is a wrapper script (round-2 review F1's own pin, fact
    /// (g)): it appends its own argv, one arg per line, to the file named by the TEST-ONLY
    /// env var GW_TEST_CURL_ARGV_LOG — read by the wrapper alone, never by setup.sh itself —
    /// and then `exec`s the REAL curl binary (its path captured off the default bin dir's own
    /// curl symlink before replacing it), so the ApiStub loopback below still receives every
    /// call for real. Every other tool is <see cref="ScriptProcess.MakeBinDir"/>'s usual
    /// real-binary symlink.</summary>
    static string MakeBinDirWithCurlArgvLogger()
    {
        var dir = ScriptProcess.MakeBinDir();
        var curlLink = Path.Combine(dir, "curl");
        var realCurl = File.ResolveLinkTarget(curlLink, returnFinalTarget: true)?.FullName
            ?? throw new InvalidOperationException("curl symlink target not found");
        File.Delete(curlLink);

        ScriptProcess.AddStub(dir, "curl", $$"""
            if [ -n "${GW_TEST_CURL_ARGV_LOG:-}" ]; then
              for arg in "$@"; do printf '%s\n' "$arg" >> "$GW_TEST_CURL_ARGV_LOG"; done
              printf -- '---\n' >> "$GW_TEST_CURL_ARGV_LOG"
            fi
            exec "{{realCurl}}" "$@"
            """);
        return dir;
    }

    /// <summary>A bin dir whose `mktemp` fails (exit 1, nothing on stdout) once <paramref
    /// name="markerFile"/> exists on disk, and execs the REAL mktemp otherwise (round-2 review
    /// F3's own pin, fact (i)). Because setup.sh's own on-air stamp file (:2230) runs ITS mktemp
    /// before invoke_launch is ever called, and <see cref="WriteLaunchStubTouchingMarker"/> only
    /// creates the marker once the launch stub itself runs, the stamp mktemp always succeeds —
    /// only install_first_beds's own cookie-jar mktemp, which runs later still, ever sees the
    /// marker and fails.</summary>
    static string MakeBinDirWithFailingMktemp(string markerFile)
    {
        var dir = ScriptProcess.MakeBinDir();
        var mktempLink = Path.Combine(dir, "mktemp");
        var realMktemp = File.ResolveLinkTarget(mktempLink, returnFinalTarget: true)?.FullName
            ?? throw new InvalidOperationException("mktemp symlink target not found");
        File.Delete(mktempLink);

        ScriptProcess.AddStub(dir, "mktemp", $$"""
            if [ -f "{{markerFile}}" ]; then
              exit 1
            fi
            exec "{{realMktemp}}" "$@"
            """);
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

    static string MakeMediaDir(int flacCount)
    {
        var dir = Directory.CreateTempSubdirectory("gw-setup-story405-media-").FullName;
        for (var i = 0; i < flacCount; i++) File.WriteAllText(Path.Combine(dir, $"track{i}.flac"), "");
        return dir;
    }

    static string ScratchEnvDir() => Directory.CreateTempSubdirectory("gw-setup-story405-env-").FullName;

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

    /// <summary>A scripted launch.sh stand-in that returns instantly with <paramref
    /// name="exitCode"/> — never touches docker/compose (GW_LAUNCH_CMD's whole reason to exist
    /// under this harness). This file never inspects launch.sh's own argv, so unlike Story345's
    /// sibling it takes no optional argv-log path.</summary>
    static string WriteLaunchStub(int exitCode)
    {
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("gw-setup-story405-launch-").FullName, "launch-stub.sh");
        File.WriteAllText(path, $"#!/usr/bin/env bash\nexit {exitCode}\n");
        MakeExecutable(path);
        return path;
    }

    /// <summary>Fact (i)'s own launch stub: touches <paramref name="markerFile"/> before exiting,
    /// which is what makes <see cref="MakeBinDirWithFailingMktemp"/>'s wrapper start failing —
    /// this stub runs strictly after setup.sh's own on-air stamp file mktemp (:2230), so that
    /// one call is never affected, only install_first_beds's later cookie-jar mktemp is.</summary>
    static string WriteLaunchStubTouchingMarker(int exitCode, string markerFile)
    {
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("gw-setup-story405-launch-").FullName, "launch-stub.sh");
        // `: > "$file"` (a no-op builtin plus a redirect), not `touch` — kept even though
        // ScriptProcess.MakeBinDir's default toolset now includes touch, since this form needs
        // no PATH lookup at all.
        File.WriteAllText(path, $"#!/usr/bin/env bash\n: > \"{markerFile}\"\nexit {exitCode}\n");
        MakeExecutable(path);
        return path;
    }

    /// <summary>The seam set every full-first-run scenario in this file needs to reach on-air —
    /// SKIP_PREFLIGHT itself is baked into <see cref="RunSetup"/> instead (every fact here wants
    /// it, none tests preflight).</summary>
    static Dictionary<string, string> BaseEnv(string launchCmd, string streamUrl, int onAirTimeoutSeconds) =>
        new()
        {
            ["GW_LAUNCH_CMD"] = launchCmd,
            ["GW_STREAM_URL"] = streamUrl,
            ["GW_ONAIR_TIMEOUT_SECONDS"] = onAirTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>Runs the real setup.sh, feeding the given text verbatim to its stdin — via a
    /// scratch wrapper script that redirects stdin from a spooled answers file (gh-#776:
    /// ScriptProcess.Run has no live-stdin support, but every fact here writes its whole answer
    /// transcript upfront, so a file redirect is behaviorally identical to the old live-pipe
    /// write-then-close), with Story346's own trailing CLI-args array (fact (d) needs
    /// `--offline`). SKIP_PREFLIGHT=1 always rides along as a default (extraEnv is merged on
    /// top, so a scenario could still override it, though none here do) — preflight itself is
    /// Story342/344's own suite.</summary>
    static (int ExitCode, string StdOut, string StdErr) RunSetup(
        string binDir, string envFile, string stdinAnswers, IReadOnlyDictionary<string, string> extraEnv,
        params string[] args)
    {
        var scratchDir = Directory.CreateTempSubdirectory("gw-setup-story405-stdin-").FullName;
        var answersPath = Path.Combine(scratchDir, "answers.txt");
        File.WriteAllText(answersPath, stdinAnswers);

        var wrapperPath = Path.Combine(scratchDir, "run-setup.sh");
        File.WriteAllText(wrapperPath, $"exec bash setup.sh \"$@\" < \"{answersPath}\"\n");
        MakeExecutable(wrapperPath);

        var mergedEnv = new Dictionary<string, string> { ["SKIP_PREFLIGHT"] = "1" };
        foreach (var (key, value) in extraEnv)
            mergedEnv[key] = value;

        return ScriptProcess.Run(wrapperPath, binDir, envFile, mergedEnv, args);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH (a) — login then install, in order, password from the .env FILE
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheHappyPath
    {
        [Fact]
        public void LoginThenInstallRunInOrderWithTheFilesPasswordAndReportTheFileCount()
        {
            using var api = new ApiStub(installStatus: 200, installAssetCount: 3);
            var launchStub = WriteLaunchStub(exitCode: 0);
            // The stale-mount gate (Story345 B2) is universal — a genuine happy-path fixture
            // must 404 once before serving.
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var env = BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30);
            env["GW_API_URL"] = api.Url;
            // T318 F2's own poison idiom: an ambient ADMIN_PASSWORD the wizard must never use —
            // apply_generate_secrets mints its own fresh random secret regardless of this value,
            // so if install_first_beds ever read the ambient copy instead of ${ENV_FILE}, the
            // stub would capture "wrong" rather than the real generated secret.
            env["ADMIN_PASSWORD"] = "wrong";

            var (exitCode, stdOut, _) = RunSetup(ScriptProcess.MakeBinDir(), envFile, $"{mediaDir}\n1\ny\n", env);

            var filePassword = ReadEnvValue(File.ReadAllText(envFile), "ADMIN_PASSWORD");

            // Round-2 review R6: install_first_beds now runs AFTER print_handoff, so the
            // once-only password line must always precede the pack's own success line in stdout
            // — never the other way round. "(shown once —" is print_handoff's own fixed marker
            // for that line (the password value itself is random per run, so it can't be the
            // needle); the success line marker is the literal file-count sentence above.
            var passwordLineIndex = stdOut.IndexOf("(shown once —", StringComparison.Ordinal);
            var successLineIndex = stdOut.IndexOf(
                "Background music installed (3 files).", StringComparison.Ordinal);

            Assert.True(
                exitCode == 0 &&
                api.Calls.SequenceEqual(["login", "install"]) &&
                api.CapturedPassword == filePassword &&
                api.CapturedPassword != "wrong" &&
                successLineIndex >= 0 &&
                passwordLineIndex >= 0 &&
                passwordLineIndex < successLineIndex,
                $"expected login then install (in order) against the stub, the password read from " +
                $"{envFile} (never the ambient 'wrong'), the file-count success line, and the " +
                $"once-only password line printed BEFORE that success line (the pack install runs " +
                $"after the handoff); exit={exitCode} calls=[{string.Join(",", api.Calls)}] " +
                $"captured={api.CapturedPassword} filePassword={filePassword} " +
                $"passwordLineIndex={passwordLineIndex} successLineIndex={successLineIndex} " +
                $"stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (b) — a non-happy install status never turns the wizard itself unsuccessful
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioANonHappyInstallStatus
    {
        [Theory]
        [InlineData(409)]
        [InlineData(500)]
        public void PrintsTheHonestLineAndNeverChangesTheWizardsExitCode(int installStatus)
        {
            using var api = new ApiStub(installStatus: installStatus);
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var env = BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30);
            env["GW_API_URL"] = api.Url;

            var (exitCode, stdOut, _) = RunSetup(ScriptProcess.MakeBinDir(), envFile, $"{mediaDir}\n1\ny\n", env);

            Assert.True(
                exitCode == 0 &&
                stdOut.Contains(
                    $"Background music pack not installed ({installStatus}). Install it later from the catalog page.",
                    StringComparison.Ordinal),
                $"expected the honest not-installed line and the wizard's own (unchanged) exit code; " +
                $"exit={exitCode} stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (c) — a 404 on the install route means the admin surface itself is off
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheAdminSurfaceIsOff
    {
        [Fact]
        public void A404InstallResponseNamesEveryPossibleCauseRatherThanGuessingOne()
        {
            // Round-2 review R7: a bare 404 on the install route also means the catalog
            // kill-switch is on or the slug isn't on the catalog yet (JinglePackController.cs
            // :82-84,179) — not only the admin surface being off. Naming all three honestly
            // matters today specifically: gw-first-beds is not yet on catalog main (PR #76 open),
            // so a fresh operator's most likely real-world 404 cause is "not in the catalog yet",
            // and the old single-cause wording would have misdiagnosed it.
            using var api = new ApiStub(installStatus: 404);
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var env = BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30);
            env["GW_API_URL"] = api.Url;

            var (_, stdOut, _) = RunSetup(ScriptProcess.MakeBinDir(), envFile, $"{mediaDir}\n1\ny\n", env);

            Assert.Contains(
                "Background music pack not installed (404: the pack is not in the catalog yet, or the admin surface is off). Install it later from the catalog page.",
                stdOut, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (d) — --offline skips the step outright, the api is never touched
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioOfflineFlag
    {
        [Fact]
        public void SkipsTheStepAndNeverCallsTheApiAtAll()
        {
            using var api = new ApiStub();
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var env = BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30);
            env["GW_API_URL"] = api.Url;

            var (exitCode, stdOut, _) = RunSetup(
                ScriptProcess.MakeBinDir(), envFile, $"{mediaDir}\n1\ny\n", env, "--offline");

            Assert.True(
                exitCode == 0 &&
                api.Calls.Count == 0 &&
                stdOut.Contains("Background music pack: skipped (offline).", StringComparison.Ordinal),
                $"expected the skipped line and zero calls to the stub api; exit={exitCode} " +
                $"calls=[{string.Join(",", api.Calls)}] stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (e) — curl absent from this machine entirely
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioCurlIsAbsent
    {
        [Fact]
        public void PrintsTheHonestCantInstallLineAndExitsWithTheWizardsOwnCode()
        {
            // No ApiStub needed at all here — curl's own absence is caught before any URL is
            // ever built, matching Story345's own ScenarioNoProberAvailable idiom (curl-less
            // also drives wait_for_on_air_bg to its honest poller_exit=2 "no prober" branch,
            // which still falls through to main()'s tail rather than exiting early).
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 1);   // never actually queried — curl is absent
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (exitCode, stdOut, _) = RunSetup(
                BinWithoutCurl(), envFile, $"{mediaDir}\n1\ny\n",
                BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30));

            Assert.True(
                exitCode == 0 &&
                stdOut.Contains(
                    "Background music pack: can't install automatically (curl not found on this machine) — install it later from the catalog page.",
                    StringComparison.Ordinal),
                $"expected the honest can't-install line and the wizard's own (unchanged) exit code; " +
                $"exit={exitCode} stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (f) — adoption mode never reaches this step at all
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioAdoptionMode
    {
        [Fact]
        public void AnExistingEnvFileNeverRunsTheInstallStep()
        {
            // setup_adoption_mode's own control flow (main(): `if [ -f "$ENV_FILE" ]; then
            // setup_adoption_mode; fi`) always calls `exit` — verify-clean (0), drift-found (5),
            // or repair-remaining (5) — before main() ever reaches the interview, the launch, or
            // this step's own call site further down. The .env content here is deliberately
            // minimal: only its EXISTENCE matters for this fact, not what it verifies to.
            using var api = new ApiStub();
            var envFile = ScratchEnvPath();
            File.WriteAllText(envFile, "MEDIA_DIR=/tmp\n");

            var (_, stdOut, _) = RunSetup(
                ScriptProcess.MakeBinDir(), envFile, "", new Dictionary<string, string> { ["GW_API_URL"] = api.Url });

            Assert.True(
                api.Calls.Count == 0 &&
                !stdOut.Contains("Background music", StringComparison.Ordinal) &&
                stdOut.Contains("A .env already exists here", StringComparison.Ordinal),
                $"expected adoption mode to exit before ever reaching the background-music install " +
                $"step; calls=[{string.Join(",", api.Calls)}] stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (g) — round-2 review F1: the password never travels on curl's own argv
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioThePasswordNeverAppearsOnCurlsArgv
    {
        [Fact]
        public void ThePasswordTravelsOnStdinNeverOnCurlsOwnCommandLine()
        {
            // /proc/<pid>/cmdline is world-readable for the life of a call, so the admin
            // password must never be an argument on curl's own argv. This wrapper execs the REAL
            // curl (the ApiStub loopback below still receives the real request) but first logs
            // its own argv, one arg per line, to a file named by a TEST-ONLY env var the wrapper
            // alone reads — setup.sh itself never sees or sets GW_TEST_CURL_ARGV_LOG.
            using var api = new ApiStub(installStatus: 200, installAssetCount: 3);
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();
            var argvLog = Path.Combine(ScratchEnvDir(), "curl-argv.log");

            var env = BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30);
            env["GW_API_URL"] = api.Url;
            env["GW_TEST_CURL_ARGV_LOG"] = argvLog;

            var (exitCode, stdOut, _) = RunSetup(
                MakeBinDirWithCurlArgvLogger(), envFile, $"{mediaDir}\n1\ny\n", env);

            var filePassword = ReadEnvValue(File.ReadAllText(envFile), "ADMIN_PASSWORD");
            var argvLog2 = File.Exists(argvLog) ? File.ReadAllText(argvLog) : "";

            Assert.True(
                exitCode == 0 &&
                api.CapturedPassword == filePassword &&
                argvLog2.Contains("@-", StringComparison.Ordinal) &&
                !argvLog2.Contains(filePassword, StringComparison.Ordinal),
                $"expected curl's own argv to carry '@-' (the stdin-body marker) and NEVER the " +
                $"real password '{filePassword}', while the body still reached the stub for real " +
                $"(CapturedPassword={api.CapturedPassword}); exit={exitCode}\nargv log:\n{argvLog2}\n" +
                $"stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (h) — round-2 review F2: an ambient proxy must never intercept the loopback calls
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioAnAmbientProxyNeverInterceptsTheLoopbackCalls
    {
        [Fact]
        public void TheInstallStillReachesTheStubWithADeadProxyExported()
        {
            // T318 review LOW finding F9 (the same law mount_serves_audio already carries): a
            // stray HTTP_PROXY/http_proxy must never route this loopback call elsewhere — with
            // F1's fix in place that would mean the plaintext password itself leaving the box.
            // Port 9 (the "discard" port) has nothing listening, so a missing --noproxy '*' here
            // makes curl fail through the dead proxy (000) rather than merely misbehave quietly.
            using var api = new ApiStub(installStatus: 200, installAssetCount: 3);
            var launchStub = WriteLaunchStub(exitCode: 0);
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var env = BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30);
            env["GW_API_URL"] = api.Url;
            const string deadProxy = "http://127.0.0.1:9";
            env["http_proxy"] = deadProxy;
            env["HTTP_PROXY"] = deadProxy;
            env["https_proxy"] = deadProxy;
            env["ALL_PROXY"] = deadProxy;

            var (exitCode, stdOut, _) = RunSetup(ScriptProcess.MakeBinDir(), envFile, $"{mediaDir}\n1\ny\n", env);

            Assert.True(
                exitCode == 0 &&
                api.Calls.SequenceEqual(["login", "install"]) &&
                stdOut.Contains("Background music installed (3 files).", StringComparison.Ordinal),
                $"expected login+install to still reach the stub, and the success line to still " +
                $"print, despite a dead proxy on every *_PROXY variable; exit={exitCode} " +
                $"calls=[{string.Join(",", api.Calls)}] stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (i) — round-2 review F3: no writable temp dir is honest, never fatal
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioNoWritableTempDirForThePackInstallStep
    {
        [Fact]
        public void SkipsHonestlyWithoutKillingTheWizardOrHidingTheHandoff()
        {
            // The T318 round-2 defect class (docs/PLAN.md:1068): an unguarded `mktemp`
            // assignment under `set -e` used to kill the whole wizard AFTER the on-air line but
            // BEFORE the once-only password. The marker file makes mktemp fail ONLY for
            // install_first_beds's own cookie jar — the earlier on-air stamp file's mktemp
            // (:2230) still succeeds, since the marker is only touched once the launch stub runs.
            using var api = new ApiStub(installStatus: 200, installAssetCount: 3);
            var markerFile = Path.Combine(ScratchEnvDir(), "mktemp-should-fail-from-here");
            var launchStub = WriteLaunchStubTouchingMarker(exitCode: 0, markerFile);
            using var mount = new MountStub(servesOnAttempt: 2);
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var env = BaseEnv(launchStub, mount.Url, onAirTimeoutSeconds: 30);
            env["GW_API_URL"] = api.Url;

            var (exitCode, stdOut, _) = RunSetup(
                MakeBinDirWithFailingMktemp(markerFile), envFile, $"{mediaDir}\n1\ny\n", env);

            Assert.True(
                exitCode == 0 &&
                api.Calls.Count == 0 &&
                stdOut.Contains("(shown once —", StringComparison.Ordinal) &&
                stdOut.Contains(
                    "Background music pack not installed (no writable temp dir). Install it later from the catalog page.",
                    StringComparison.Ordinal),
                $"expected the wizard to exit 0, the handoff's once-only password line to still " +
                $"print, and the honest no-writable-temp-dir line — never a script death; " +
                $"exit={exitCode} calls=[{string.Join(",", api.Calls)}] stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH (j) — round-2 review F5: -h/--help actually names --offline
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioHelpOutput
    {
        [Fact]
        public void NamesTheOfflineFlag()
        {
            // usage() only dumps this file's own leading comment block; --offline used to live
            // outside it (only in the unknown-argument arm), so `--help` never named a flag
            // `--bogus` did. -h/--help exits main() before any interview/launch machinery runs,
            // so no launch stub, media dir, or api stub is needed here at all.
            var (exitCode, stdOut, _) = RunSetup(
                ScriptProcess.MakeBinDir(), ScratchEnvPath(), "", new Dictionary<string, string>(), "--help");

            Assert.True(
                exitCode == 0 && stdOut.Contains("--offline", StringComparison.Ordinal),
                $"expected ./setup.sh --help to name --offline; exit={exitCode} stdout:\n{stdOut}");
        }
    }
}
