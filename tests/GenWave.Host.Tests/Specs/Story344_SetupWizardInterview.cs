// STORY-344 — The wizard interview (F132.1–.6)
//
// BDD specification — xUnit. Drives the REAL ./setup.sh via ScriptProcess with scripted stdin
// answers — the child starts from ScriptProcess's sanitized environment (gh-#776): every
// ambient GW_*/SKIP_*/COMPOSE_* export and .env secret name is scrubbed before setup.sh ever
// sees it, on a scratch-PATH bin dir with coreutils symlinks + scripted docker/dotnet stubs and
// a scratch GW_ENV_FILE. No daemon, safe anywhere. Most scenarios run with SKIP_PREFLIGHT=1 —
// the machine/`.env` preflight itself is Story342's suite; ScenarioPreflightRunsAfterTheEnvWrite
// below is the one place this file proves setup.sh actually wires preflight_docker +
// preflight_env_secrets in after the write, on real (stubbed) machine facts.
//
// One fact — ScenarioTheNoMusicLane.TheRecheckLoopProceedsOnceAudioFilesAppear — needs to write
// a file to disk from THIS process only after observing the child block on its "check again?"
// read, and to send several more answers as the wizard progresses: a genuinely live stdin/
// stdout interleave. ScriptProcess.Run only returns once the child has already exited, so that
// one fact drives setup.sh via ScriptProcess.Start (gh-#776) instead — same sanitized
// environment, but it hands back the live Process so this method can read its stdout as it
// arrives and write each answer only once the prompt it depends on has appeared.

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using GenWave.Host.Tests.Support;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// A scratch icecast-mount stand-in for the two facts in this file that now run the wizard all
/// the way to on-air (T318/STORY-345 landed setup.sh's real launch invocation, which those two
/// facts' success-path assertions depend on) — 404s on the first request, then always answers
/// 200 with a small nonzero body from the second request onward. The one 404 is required by
/// T318's round-3 review BLOCKING finding B2: setup.sh's stale-mount gate is now UNIVERSAL (a
/// mount already serving on the very first poll, before this run's own launch even began, is
/// indistinguishable from a stale/pre-existing stack and is never trusted) — this Kestrel
/// instance is already listening before setup.sh's process even starts, so serving 200
/// unconditionally from request #1 would trip that gate and time out instead of reaching
/// on-air. Same Kestrel-on-port-0-then-read-back idiom as Story179_SpectatorListenerCount.cs
/// (gh-#329: no free-port-then-rebind race) and Story345_SetupLaunchClockHandoff.cs's own
/// (richer, scriptable) MountStub — that one is file-scoped to its own file, so this is a
/// deliberately minimal duplicate rather than a cross-file reuse.
/// </summary>
file sealed class ImmediateMountStub : IDisposable
{
    readonly WebApplication app;
    int requestCount;

    public string Url { get; }

    public ImmediateMountStub()
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        app = builder.Build();

        app.Run(async ctx =>
        {
            if (Interlocked.Increment(ref requestCount) == 1)
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

public static class FeatureSetupWizardInterview
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared harness
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A bin dir with no `dotnet` at all — Q1's build-your-own path must never be offered.</summary>
    static string BinWithoutDotnet() => ScriptProcess.MakeBinDir();

    /// <summary>A bin dir whose `dotnet --list-sdks` reports a 10.x SDK — Q1 must offer both options.</summary>
    static string BinWithDotnet10Sdk()
    {
        var bin = ScriptProcess.MakeBinDir();
        ScriptProcess.AddStub(bin, "dotnet",
            """if [ "${1:-}" = "--list-sdks" ]; then echo "10.0.100 [/usr/lib/dotnet/sdk]"; exit 0; fi; exit 0""");
        return bin;
    }

    /// <summary>docker info + `docker compose version` succeed, `ss`/`df` report nothing bound and
    /// ample headroom — the "nothing to complain about" preflight stub (Story342's convention).</summary>
    static string HealthyPreflightBin()
    {
        var bin = BinWithoutDotnet();
        ScriptProcess.AddStub(bin, "docker",
            """
            if [ "${1:-}" = "info" ]; then exit 0; fi
            if [ "${1:-}" = "compose" ] && [ "${2:-}" = "version" ]; then echo "Docker Compose version v2.24.5"; exit 0; fi
            exit 0
            """);
        ScriptProcess.AddStub(bin, "ss",
            """echo "State   Recv-Q  Send-Q   Local Address:Port   Peer Address:Port  Process" """);
        ScriptProcess.AddStub(bin, "df",
            """
            echo "Filesystem     1024-blocks      Used Available Capacity Mounted on"
            echo "tmpfs            50000000   1000000  49000000       3% /"
            """);
        return bin;
    }

    /// <summary>Every ScriptProcess.MakeBinDir tool except `mv` symlinked (real binaries), then a
    /// stub `mv` that always fails — proves the atomic-write's temp-file cleanup path (T317
    /// review LOW finding). Deleting the real `mv` symlink before stubbing, rather than AddStub-
    /// ing straight over it, avoids writing through that symlink into the real system `mv`.</summary>
    static string BinWithFailingMv()
    {
        var dir = ScriptProcess.MakeBinDir();
        File.Delete(Path.Combine(dir, "mv"));
        ScriptProcess.AddStub(dir, "mv", "exit 1");
        return dir;
    }

    static string ScratchEnvDir() => Directory.CreateTempSubdirectory("gw-setup-story344-env-").FullName;

    static string ScratchEnvPath() => Path.Combine(ScratchEnvDir(), ".env");

    static string WriteExistingEnvFile(string content)
    {
        var path = ScratchEnvPath();
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A fresh scratch directory holding the given count of .flac/.mp3 files (and nothing else).</summary>
    static string MakeMediaDir(int flacCount = 0, int mp3Count = 0)
    {
        var dir = Directory.CreateTempSubdirectory("gw-setup-story344-media-").FullName;
        for (var i = 0; i < flacCount; i++) File.WriteAllText(Path.Combine(dir, $"track{i}.flac"), "");
        for (var i = 0; i < mp3Count; i++) File.WriteAllText(Path.Combine(dir, $"track{i}.mp3"), "");
        return dir;
    }

    static readonly IReadOnlyDictionary<string, string> SkipPreflight =
        new Dictionary<string, string> { ["SKIP_PREFLIGHT"] = "1" };

    /// <summary>Runs the real setup.sh, feeding the given text verbatim to its stdin — the
    /// Gh019/Story342 idiom extended with stdin, since the wizard's answer channel IS stdin.
    /// ScriptProcess.Run (gh-#776) has no stdin parameter, so the answers are spooled to a
    /// scratch file and what actually runs is a one-line wrapper script (`exec bash setup.sh <
    /// answers-file`) — bash hitting EOF at the end of that file behaves identically, from
    /// setup.sh's own `read` calls, to writing the same bytes to a live pipe and then closing
    /// it (including for the mid-interview-abandonment facts below: the next `read` simply
    /// fails at end of input either way, so no broken-pipe handling is needed here).</summary>
    static (int ExitCode, string StdOut, string StdErr) RunSetup(
        string binDir, string envFile, string stdinAnswers,
        IReadOnlyDictionary<string, string>? extraEnv = null)
    {
        var scratchDir = Directory.CreateTempSubdirectory("gw-setup-story344-stdin-").FullName;
        var answersPath = Path.Combine(scratchDir, "answers.txt");
        File.WriteAllText(answersPath, stdinAnswers);

        var wrapperPath = Path.Combine(scratchDir, "run-setup.sh");
        File.WriteAllText(wrapperPath, $"exec bash setup.sh < \"{answersPath}\"\n");

        return ScriptProcess.Run(wrapperPath, binDir, envFile, extraEnv);
    }

    /// <summary>Writes a scripted launch.sh stand-in that exits 0 immediately — T318's on-air
    /// path needs SOME launch to succeed, and the real ./launch.sh would try to talk to a real
    /// Docker daemon this harness never has. Mirrors Story345_SetupLaunchClockHandoff.cs's own
    /// WriteLaunchStub (file-scoped there too, so duplicated rather than shared).</summary>
    static string WriteExitZeroLaunchStub()
    {
        var path = Path.Combine(
            Directory.CreateTempSubdirectory("gw-setup-story344-launch-").FullName, "launch-stub.sh");
        File.WriteAllText(path, "#!/usr/bin/env bash\nexit 0\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return path;
    }

    /// <summary>Every generated-secret key this wizard writes into .env (F132.3) — the five
    /// truly-internal secrets plus ADMIN_PASSWORD, read directly off .env.example's own
    /// change-me placeholders (there are exactly six).</summary>
    static readonly string[] GeneratedSecretKeys =
    [
        "POSTGRES_PASSWORD", "LIBRARY_DB_PASSWORD", "STATION_DB_PASSWORD",
        "ICECAST_SOURCE_PASSWORD", "ICECAST_ADMIN_PASSWORD", "ADMIN_PASSWORD",
    ];

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

    // ---------------------------------------------------------------------
    // HAPPY PATH — the four questions, and only those (AC1)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheInterviewAsksExactlyFourQuestions
    {
        [Fact]
        public void AVirginRunAsksImagesMusicTopologyProfilesInOrder()
        {
            // No .NET SDK on PATH: Q1 still prints its informational header (no build-from-
            // source prompt), so only three answers are needed — music path, topology, admin.
            var mediaDir = MakeMediaDir(flacCount: 1);
            var (_, stdOut, _) = RunSetup(
                BinWithoutDotnet(), ScratchEnvPath(), $"{mediaDir}\n1\ny\n", SkipPreflight);

            var imagesIdx = stdOut.IndexOf("1) How should GenWave run?", StringComparison.Ordinal);
            var musicIdx = stdOut.IndexOf("2) Where is your music library?", StringComparison.Ordinal);
            var topologyIdx = stdOut.IndexOf("3) Topology preset", StringComparison.Ordinal);
            var profilesIdx = stdOut.IndexOf("4) Optional profiles", StringComparison.Ordinal);

            Assert.True(
                imagesIdx >= 0 && imagesIdx < musicIdx && musicIdx < topologyIdx && topologyIdx < profilesIdx,
                $"expected the four questions in order; stdout:\n{stdOut}");
        }

        [Fact]
        public void BuildYourOwnIsOfferedOnlyWhenADotnet10SdkIsDetected()
        {
            var mediaDir = MakeMediaDir(flacCount: 1);
            var (_, stdOut, _) = RunSetup(
                BinWithoutDotnet(), ScratchEnvPath(), $"{mediaDir}\n1\ny\n", SkipPreflight);

            Assert.DoesNotContain("Build from source", stdOut);
        }

        [Fact]
        public void ADetectedDotnet10SdkOffersTheBuildOption()
        {
            var mediaDir = MakeMediaDir(flacCount: 1);
            // dotnet detected -> Q1 IS a real prompt, four answers needed.
            var (_, stdOut, _) = RunSetup(
                BinWithDotnet10Sdk(), ScratchEnvPath(), $"1\n{mediaDir}\n1\ny\n", SkipPreflight);

            Assert.Contains("Build from source", stdOut);
        }

        [Fact]
        public void TheTopologyRecommendationFollowsDetectedRamAndArch()
        {
            // GW_MEMINFO_FILE + GW_ARCH report a 3 GiB x86_64 box -> piper-only recommended; the
            // owner overrides to Full at the prompt (answer "1"). The final .env must honor the
            // OVERRIDE, not the recommendation.
            var meminfo = Path.Combine(Directory.CreateTempSubdirectory("gw-setup-story344-ram-").FullName, "meminfo");
            File.WriteAllText(meminfo, "MemTotal:        3945000 kB\nMemFree:          100000 kB\n");
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (_, _, _) = RunSetup(
                BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n", Merge(SkipPreflight,
                    ("GW_MEMINFO_FILE", meminfo), ("GW_ARCH", "x86_64")));

            var envContent = File.ReadAllText(envFile);
            Assert.Equal("home", ReadEnvValue(envContent, "GW_PRESET"));
        }

        static IReadOnlyDictionary<string, string> Merge(
            IReadOnlyDictionary<string, string> baseEnv, params (string Key, string Value)[] extra)
        {
            var merged = new Dictionary<string, string>(baseEnv);
            foreach (var (key, value) in extra) merged[key] = value;
            return merged;
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — Q3's menu names only services the chosen preset actually composes (T320 wire
    // defect: the menu used to read "kokoro + ollama", but F136.5 means no wizard preset EVER
    // stacks compose.demo.yaml — `docker compose -f compose.yaml -f compose.pinned.yaml config
    // --services` on what "home" resolves to lists no ollama at all. Mirrors the "never names
    // ollama" negative pin Story345_SetupLaunchClockHandoff.cs already carries for the
    // DEGRADED-BUT-AIRING banner (ScenarioLaunchDegradedButAiring.TheDegradedBannerNeverNamesOllamaOrCaddy)
    // — this is the same law, pinned at the one place it was actually being broken.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheTopologyMenuNamesOnlyComposedServices
    {
        [Fact]
        public void TheTopologyMenuNeverMentionsOllama()
        {
            var mediaDir = MakeMediaDir(flacCount: 1);
            var (_, stdOut, _) = RunSetup(
                BinWithoutDotnet(), ScratchEnvPath(), $"{mediaDir}\n1\ny\n", SkipPreflight);

            Assert.True(
                !stdOut.Contains("ollama", StringComparison.Ordinal),
                $"expected Q3's topology menu to never name ollama — it exists only in compose.demo.yaml, which no wizard preset ever stacks; stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the topology recommendation gates arm64 on RAM, never bare arch
    // (T317 review LOW finding)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTopologyRecommendationGatesArm64OnRam
    {
        [Fact]
        public void Arm64WithAmpleKnownRamRecommendsFullNotPiperOnly()
        {
            // A beefy arm64 box (16 GiB) — bare arch used to force piper-only regardless of
            // headroom; the recommendation must now follow the RAM, same as any other arch.
            var meminfo = Path.Combine(Directory.CreateTempSubdirectory("gw-setup-story344-ram-").FullName, "meminfo");
            File.WriteAllText(meminfo, "MemTotal:        16000000 kB\nMemFree:          1000000 kB\n");
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            // Blank topology answer — accepts the RECOMMENDED default, not an explicit
            // override, so this actually exercises recommend_topology's own verdict.
            RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n\ny\n", Merge(SkipPreflight,
                ("GW_MEMINFO_FILE", meminfo), ("GW_ARCH", "aarch64")));

            Assert.Equal("home", ReadEnvValue(File.ReadAllText(envFile), "GW_PRESET"));
        }

        [Fact]
        public void Arm64WithUnreadableRamStillRecommendsPiperOnly()
        {
            // RAM undetectable (no such meminfo path) on arm64 — treated as circumstantial
            // SBC-class evidence, same conservative default as before this finding's fix.
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            // Blank topology answer — accepts the RECOMMENDED default (see the comment above).
            RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n\ny\n", Merge(SkipPreflight,
                ("GW_MEMINFO_FILE", "/nonexistent/meminfo"), ("GW_ARCH", "aarch64")));

            Assert.Equal("home-piper-only", ReadEnvValue(File.ReadAllText(envFile), "GW_PRESET"));
        }

        static IReadOnlyDictionary<string, string> Merge(
            IReadOnlyDictionary<string, string> baseEnv, params (string Key, string Value)[] extra)
        {
            var merged = new Dictionary<string, string>(baseEnv);
            foreach (var (key, value) in extra) merged[key] = value;
            return merged;
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — a missing `find` degrades to "couldn't check", never a verified zero
    // (T317 review MEDIUM finding)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioAudioCountCannotBeVerified
    {
        [Fact]
        public void AMissingFindNeverShowsTheNoMusicLaneOverAnUnverifiableLibrary()
        {
            var mediaDir = MakeMediaDir();   // irrelevant here — find can never see it either way
            var envFile = ScratchEnvPath();
            var extraEnv = new Dictionary<string, string>(SkipPreflight)
            {
                ["GW_FIND_CMD"] = "gw-setup-story344-missing-find",
            };

            var (_, stdOut, _) = RunSetup(
                BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n", extraEnv);

            Assert.DoesNotContain("Jamendo", stdOut);
            Assert.Contains("Could not verify the audio file count", stdOut, StringComparison.Ordinal);
            Assert.Contains("3) Topology preset", stdOut, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — secrets generated, placeholders extinct (AC2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioSecretsAreGenerated
    {
        static readonly Lazy<(string EnvContent, string StdOut)> Run = new(() =>
        {
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();
            var (_, stdOut, _) = RunSetup(
                BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n", SkipPreflight);
            return (File.ReadAllText(envFile), stdOut);
        });

        [Fact]
        public void AllSixInternalSecretsAreGeneratedAtLeast32Chars()
        {
            var envContent = Run.Value.EnvContent;
            var values = GeneratedSecretKeys.Select(k => ReadEnvValue(envContent, k)).ToArray();

            Assert.True(
                values.All(v => v.Length >= 32 && Regex.IsMatch(v, "^[A-Za-z0-9]+$")),
                $"expected all six generated secrets to be >=32 alnum chars; got lengths [{string.Join(",", values.Select(v => v.Length))}]");
        }

        [Fact]
        public void AdminPasswordIsGeneratedForTheOnceOnlyDisplay()
        {
            var value = ReadEnvValue(Run.Value.EnvContent, "ADMIN_PASSWORD");

            Assert.True(value.Length >= 32 && Regex.IsMatch(value, "^[A-Za-z0-9]+$"),
                $"expected a generated ADMIN_PASSWORD; got '{value}'");
        }

        [Fact]
        public void AdminPasswordIsNeverPrintedToStdoutByThisTask()
        {
            // T318 owns the once-only handoff display — this task must never echo it.
            var adminPassword = ReadEnvValue(Run.Value.EnvContent, "ADMIN_PASSWORD");

            Assert.DoesNotContain(adminPassword, Run.Value.StdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void NoChangeMePlaceholderSurvivesInTheWrittenEnv()
        {
            Assert.DoesNotContain("change-me", Run.Value.EnvContent, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — build_env_content is an ALLOWLIST (T317 review findings B1/B2): no
    // .env.example content beyond these exact keys is ever copied into the written .env.
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheWrittenEnvIsAnAllowlist
    {
        static readonly string[] AllowlistedKeys =
        [
            "COMPOSE_PROFILES", "MEDIA_DIR",
            "POSTGRES_PASSWORD", "LIBRARY_DB_PASSWORD", "STATION_DB_PASSWORD",
            "ICECAST_SOURCE_PASSWORD", "ICECAST_ADMIN_PASSWORD", "ADMIN_PASSWORD",
            "GW_PRESET",
        ];

        static readonly Lazy<string> EnvContent = new(() =>
        {
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();
            RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n", SkipPreflight);
            return File.ReadAllText(envFile);
        });

        /// <summary>Every key an UNCOMMENTED assignment line writes — a commented pointer
        /// (`#PUBLIC_HOST=`) contributes no key here.</summary>
        static IReadOnlySet<string> WrittenKeys(string envContent) =>
            envContent.Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0 && l[0] != '#' && l.Contains('=', StringComparison.Ordinal))
                .Select(l => l[..l.IndexOf('=', StringComparison.Ordinal)])
                .ToHashSet(StringComparer.Ordinal);

        [Fact]
        public void OnlyTheAllowlistedKeysAreWritten()
        {
            var keys = WrittenKeys(EnvContent.Value);

            Assert.Equal(
                AllowlistedKeys.OrderBy(k => k, StringComparer.Ordinal),
                keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public void PublicHostAndTunnelTokenAreCommentedPointersNeverFabricatedValues()
        {
            // No fabricated values, ever — the compose.demo.yaml `${VAR:?}` guards stay armed
            // until an operator deliberately opts into the public-appliance overlay.
            var content = EnvContent.Value;

            Assert.Contains("#PUBLIC_HOST=", content, StringComparison.Ordinal);
            Assert.Contains("#TUNNEL_TOKEN=", content, StringComparison.Ordinal);
            Assert.Contains("DEPLOYMENT.md", content, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the T316 rider: .env is written clean (unquoted, LF, no trailing whitespace)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheWrittenEnvIsClean
    {
        static readonly Lazy<string> EnvContent = new(() =>
        {
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();
            RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n", SkipPreflight);
            return File.ReadAllText(envFile);
        });

        [Fact]
        public void TheWrittenEnvContainsNoCarriageReturns()
        {
            // T316 rider: a quoted or CRLF GW_PRESET makes launch.sh exit 2 with the \r
            // invisible in its own error message.
            Assert.DoesNotContain('\r', EnvContent.Value);
        }

        [Fact]
        public void GwPresetLineIsUnquotedFromTheClosedVocabulary()
        {
            Assert.Matches(
                new Regex(@"(?m)^GW_PRESET=(home|home-piper-only|dev|dev-piper-only)$"),
                EnvContent.Value);
        }

        [Fact]
        public void GeneratedSecretValuesCarryNoSurroundingQuotes()
        {
            var value = ReadEnvValue(EnvContent.Value, "POSTGRES_PASSWORD");

            Assert.DoesNotContain('"', value);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — derive, don't record (AC3) + atomicity (AC4)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioDeriveDontRecord
    {
        [Fact]
        public void ARerunOverACompleteEnvVerifiesAndSkipsTheInterview()
        {
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();
            RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n", SkipPreflight);

            // Second run: no answers on stdin at all — if it tried to re-run the interview it
            // would hit EOF at the first prompt and abort with the abandonment message instead.
            var (_, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, "", SkipPreflight);

            Assert.DoesNotContain("1) How should GenWave run?", stdOut);
        }

        [Fact]
        public void NoWizardStateFileExistsAfterAnyRun()
        {
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envDir = ScratchEnvDir();
            var envFile = Path.Combine(envDir, ".env");
            RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n", SkipPreflight);

            var filesLeftBehind = Directory.GetFiles(envDir).Select(f => new FileInfo(f).Name).ToArray();

            Assert.Equal([".env"], filesLeftBehind);
        }

        [Fact]
        public void EnvIsWrittenAtomicallyViaTempAndMove()
        {
            // Mid-interview kill: answer Q1 and Q2 (2 of 4 questions — a real "mid" abandonment,
            // not just the very first prompt), then close stdin before Q3 is answered.
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (exitCode, _, _) = RunSetup(
                BinWithDotnet10Sdk(), envFile, $"1\n{mediaDir}\n", SkipPreflight);

            Assert.True(exitCode != 0 && !File.Exists(envFile),
                $"expected a non-zero exit and no .env written after a mid-interview EOF; exit={exitCode}, exists={File.Exists(envFile)}");
        }

        [Fact]
        public void AnMvFailureDuringTheAtomicWriteLeavesNoStrayTempFileAndNoEnv()
        {
            // The interview completes and build_env_content succeeds, but the final `mv` into
            // place fails (T317 review LOW finding) — the EXIT trap's cleanup half (needs `rm`
            // on PATH) must remove the mktemp'd `.env.setup.*` temp file, and ENV_FILE itself
            // must never have been created.
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envDir = ScratchEnvDir();
            var envFile = Path.Combine(envDir, ".env");

            var (exitCode, _, _) = RunSetup(
                BinWithFailingMv(), envFile, $"{mediaDir}\n1\ny\n", SkipPreflight);

            var filesLeftBehind = Directory.GetFiles(envDir).Select(f => new FileInfo(f).Name).ToArray();

            Assert.True(
                exitCode != 0 && !File.Exists(envFile) && filesLeftBehind.Length == 0,
                $"expected a non-zero exit, no .env, and no stray temp file after an mv failure; exit={exitCode}, envExists={File.Exists(envFile)}, filesLeftBehind=[{string.Join(",", filesLeftBehind)}]");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the no-music lane (AC5)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioTheNoMusicLane
    {
        static readonly Lazy<string> StdOut = new(() =>
        {
            var mediaDir = MakeMediaDir();   // zero audio files
            var envFile = ScratchEnvPath();
            // Q2 path -> no-music lane -> "2" (continue anyway) -> Q3 -> Q4.
            var (_, stdOut, _) = RunSetup(
                BinWithoutDotnet(), envFile, $"{mediaDir}\n2\n1\ny\n", SkipPreflight);
            return stdOut;
        });

        [Fact]
        public void ZeroAudioFilesPrintsTheCuratedCcSourceList()
        {
            Assert.Contains("Jamendo", StdOut.Value);
        }

        [Fact]
        public void TheLanePrintsTheLicensingResponsibilityNote()
        {
            Assert.Contains("responsible for the licensing", StdOut.Value);
        }

        [Fact]
        public void TheLaneNamesTheSupportedFormats()
        {
            Assert.Contains(".flac/.mp3", StdOut.Value);
        }

        [Fact]
        public async Task TheRecheckLoopProceedsOnceAudioFilesAppear()
        {
            // A real mid-run interleave: the wizard is driven to the no-music prompt with a
            // genuinely empty directory, a file is dropped into that SAME directory from this
            // test while the child is blocked on its "check again?" read, and only THEN is the
            // "check again" answer sent — proving the loop re-derives the count from reality
            // rather than remembering the first (zero) answer.
            //
            // T318/STORY-345: this fact's own assertion needs the wizard to finish with exit 0
            // — which, now that setup.sh actually launches, needs a real (stubbed) launch.sh
            // and mount too (the T318 review's cross-file finding).
            var mediaDir = MakeMediaDir();
            var envFile = ScratchEnvPath();
            var launchStub = WriteExitZeroLaunchStub();
            using var mount = new ImmediateMountStub();

            var extraEnv = new Dictionary<string, string>
            {
                ["SKIP_PREFLIGHT"] = "1",
                ["GW_LAUNCH_CMD"] = launchStub,
                ["GW_STREAM_URL"] = mount.Url,
                ["GW_ONAIR_TIMEOUT_SECONDS"] = "30",
            };

            using var process = ScriptProcess.Start(
                "setup.sh", BinWithoutDotnet(), envFile, extraEnv, redirectStdin: true);

            var stdOutBuilder = new StringBuilder();
            var drain = Task.Run(async () =>
            {
                var buffer = new char[4096];
                while (true)
                {
                    var read = await process.StandardOutput.ReadAsync(buffer);
                    if (read == 0) break;
                    lock (stdOutBuilder) stdOutBuilder.Append(buffer, 0, read);
                }
            });
            var stdErrTask = process.StandardError.ReadToEndAsync();

            async Task WaitForOutput(string marker)
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    string snapshot;
                    lock (stdOutBuilder) snapshot = stdOutBuilder.ToString();
                    if (snapshot.Contains(marker, StringComparison.Ordinal)) return;
                    await Task.Delay(20);
                }
                string finalSnapshot;
                lock (stdOutBuilder) finalSnapshot = stdOutBuilder.ToString();
                throw new TimeoutException($"expected output containing '{marker}'; got:\n{finalSnapshot}");
            }

            async Task Send(string line)
            {
                await process.StandardInput.WriteLineAsync(line);
                await process.StandardInput.FlushAsync();
            }

            await WaitForOutput("Absolute path");
            await Send(mediaDir);

            await WaitForOutput("check again");
            File.WriteAllText(Path.Combine(mediaDir, "late.flac"), "");
            await Send("1");

            await WaitForOutput("Topology preset");
            await Send("1");

            await WaitForOutput("Admin UI");
            await Send("y");

            process.StandardInput.Close();
            await drain;
            await stdErrTask;
            var exited = process.WaitForExit(TimeSpan.FromSeconds(15));

            Assert.True(exited && process.ExitCode == 0 && File.Exists(envFile),
                $"expected the wizard to proceed past the re-check and finish; exited={exited}, exitCode={(exited ? process.ExitCode : -1)}, envWritten={File.Exists(envFile)}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — existing boxes route to adoption (AC6)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioExistingBoxesRouteToAdoption
    {
        [Fact]
        public void AnExistingEnvRoutesToVerifyRepairInsteadOfTheInterview()
        {
            var envFile = WriteExistingEnvFile("MARKER-EXISTING-ENV=1\n");

            var (_, stdOut, _) = RunSetup(BinWithoutDotnet(), envFile, "", SkipPreflight);

            Assert.Contains("STORY-346", stdOut);
        }

        [Fact]
        public void RoutingToAdoptionCompletesWithoutCrashing()
        {
            // T319/STORY-346 superseded the routing stub this fact originally pinned (a bare
            // "always exits 0" contract): adoption mode is now the real verify pass, so THIS
            // fixture — a one-line .env, no docker stubbed at all — legitimately reports missing
            // keys and unreachable-daemon UNKNOWNs, i.e. genuine drift, not a clean box.
            //
            // N5 (round-2 review): pinned to the SINGLE deterministic outcome rather than a
            // loose 0/3/5 set. SKIP_PREFLIGHT=1 rules out 3 outright (preflight never runs). B3's
            // fix (round-2 review) narrowed .env completeness' scope to skip overlay-gated keys
            // (PUBLIC_HOST/TUNNEL_TOKEN) that this box's own COMPOSE_FILE doesn't stack — but this
            // fixture is still missing every OTHER .env.example key (COMPOSE_PROFILES, MEDIA_DIR,
            // the six secrets), which are never gated, so verify_env_completeness still WARNs and
            // drift is reported: exit 5, always, on this exact one-line fixture. What this fact
            // still owns is that ROUTING itself completes cleanly through adoption mode's own
            // documented exit code rather than a shell-level crash.
            var envFile = WriteExistingEnvFile("MARKER-EXISTING-ENV=1\n");

            var (exitCode, _, _) = RunSetup(BinWithoutDotnet(), envFile, "", SkipPreflight);

            Assert.Equal(5, exitCode);
        }

        [Fact]
        public void TheInterviewNeverOverwritesAnExistingEnv()
        {
            const string original = "MARKER-EXISTING-ENV=1\nSOME_OTHER_KEY=untouched\n";
            var envFile = WriteExistingEnvFile(original);

            // Even with a full valid answer stream available, routing must happen before a
            // single line of it is ever read.
            RunSetup(BinWithoutDotnet(), envFile, $"{MakeMediaDir(flacCount: 1)}\n1\ny\n", SkipPreflight);

            Assert.Equal(original, File.ReadAllText(envFile));
        }
    }

    // ---------------------------------------------------------------------
    // SAD/HAPPY PATH — preflight is wired in after the write, before "Ready to launch"
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioPreflightRunsAfterTheEnvWrite
    {
        [Fact]
        public void AFailingMachinePreflightStillLeavesTheJustWrittenEnvInPlace()
        {
            // No docker anywhere on this PATH (HealthyPreflightBin's opposite) and no
            // SKIP_PREFLIGHT — preflight_docker must hard-fail (exit 3) AFTER the .env write,
            // per this task's ordering contract, not before it.
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();

            var (exitCode, _, _) = RunSetup(BinWithoutDotnet(), envFile, $"{mediaDir}\n1\ny\n");

            Assert.True(exitCode == 3 && File.Exists(envFile),
                $"expected preflight's hard-fail exit (3) with the .env already written; exit={exitCode}, exists={File.Exists(envFile)}");
        }

        [Fact]
        public void AHealthyMachineReachesReadyToLaunch()
        {
            // T318/STORY-345: setup.sh now actually launches on its way past "Ready to launch"
            // — a real completion needs a real (stubbed) launch.sh and mount, or this would
            // fall through to the REAL ./launch.sh against an intentionally-incomplete docker
            // stub and never reach exit 0 (the T318 review's cross-file finding).
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = ScratchEnvPath();
            var launchStub = WriteExitZeroLaunchStub();
            using var mount = new ImmediateMountStub();

            var (exitCode, stdOut, stdErr) = RunSetup(
                HealthyPreflightBin(), envFile, $"{mediaDir}\n1\ny\n",
                new Dictionary<string, string>
                {
                    ["GW_LAUNCH_CMD"] = launchStub,
                    ["GW_STREAM_URL"] = mount.Url,
                    ["GW_ONAIR_TIMEOUT_SECONDS"] = "30",
                });

            Assert.True(exitCode == 0 && stdOut.Contains("Ready to launch", StringComparison.Ordinal),
                $"expected a clean run reaching 'Ready to launch'; exit={exitCode} stderr={stdErr} stdout={stdOut}");
        }
    }
}
