// STORY-342 — Preflight that fails before launch, not during (F134)
//
// BDD specification — xUnit. Drives the REAL tools/preflight.sh via Process — the
// Gh019_ScriptPreflight idiom: a scratch-PATH bin dir with coreutils symlinks + scripted
// docker/ss/df stubs, an isolated env file, no daemon, safe on any machine. Unlike Gh019
// (which drives launch.sh/build.sh, the CALLERS), this suite sources tools/preflight.sh
// itself and calls its two entry points directly — the exact functions launch.sh and
// build.sh already call, so what's proven here is proven on their real call path too.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GenWave.Host.Tests.Specs;

public static class FeaturePreflightExpansion
{
    static readonly string[] RequiredEnvVars =
    [
        "POSTGRES_PASSWORD", "LIBRARY_DB_PASSWORD", "STATION_DB_PASSWORD",
        "ICECAST_SOURCE_PASSWORD", "ICECAST_ADMIN_PASSWORD", "MEDIA_DIR",
    ];

    /// <summary>Preflight-only seams this suite might otherwise inherit from the ambient shell.</summary>
    static readonly string[] SeamEnvVars =
    [
        "ADMIN_PASSWORD", "COMPOSE_PROFILES", "GW_PREFLIGHT_TOPOLOGY", "GW_PREFLIGHT_DEMO", "GW_ENV_FILE",
        "GW_CMDLINE_FILE", "GW_MEMINFO_FILE", "GW_MOUNTS_FILE", "GW_SS_CMD", "GW_DF_CMD", "GW_FIND_CMD",
        "GW_DOCKER_ROOT_FALLBACK",
    ];

    /// <summary>Coreutils tools/preflight.sh itself needs — everything else is deliberately absent
    /// so each scenario's ss/df/find stub (or lack of one) is the only thing driving that check.</summary>
    static readonly string[] BaseTools =
        ["bash", "sh", "grep", "sed", "tail", "head", "cut", "seq", "sleep", "awk", "dirname", "cat", "paste", "find"];

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
        var dir = Directory.CreateTempSubdirectory("gw-preflight-story342-bin-").FullName;
        foreach (var tool in BaseTools)
            File.CreateSymbolicLink(Path.Combine(dir, tool), ResolveTool(tool));
        return dir;
    }

    static void AddStub(string binDir, string name, string body)
    {
        var path = Path.Combine(binDir, name);
        File.WriteAllText(path, "#!/usr/bin/env bash\n" + body + "\n");
        // bash targets only — these specs only ever run on the Linux dev/CI hosts (guard exists
        // to satisfy CA1416, not to support Windows).
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    /// <summary>docker info succeeds and `docker compose version` reports a floor-clearing version —
    /// the default "nothing to complain about" stub every scenario starts from.</summary>
    static string HealthyDockerBin()
    {
        var bin = MakeBinDir();
        AddStub(bin, "docker",
            """
            if [ "${1:-}" = "info" ]; then exit 0; fi
            if [ "${1:-}" = "compose" ] && [ "${2:-}" = "version" ]; then echo "Docker Compose version v2.24.5"; exit 0; fi
            exit 0
            """);
        return bin;
    }

    static string WriteEnvFile(params string[] assignments)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("gw-preflight-story342-env-").FullName, "test.env");
        File.WriteAllLines(path, assignments);
        return path;
    }

    /// <summary>The six required secrets + MEDIA_DIR, optionally with an extra ADMIN_PASSWORD line.</summary>
    static string CompleteEnvFile(string mediaDir, string? adminPasswordLine = null)
    {
        List<string> lines =
        [
            "POSTGRES_PASSWORD=x", "LIBRARY_DB_PASSWORD=x", "STATION_DB_PASSWORD=x",
            "ICECAST_SOURCE_PASSWORD=x", "ICECAST_ADMIN_PASSWORD=x", $"MEDIA_DIR={mediaDir}",
        ];
        if (adminPasswordLine is not null) lines.Add(adminPasswordLine);
        return WriteEnvFile([.. lines]);
    }

    /// <summary>A fresh scratch directory holding the given count of .flac/.mp3 files (and nothing else).</summary>
    static string MakeMediaDir(int flacCount = 0, int mp3Count = 0)
    {
        var dir = Directory.CreateTempSubdirectory("gw-preflight-story342-media-").FullName;
        for (var i = 0; i < flacCount; i++) File.WriteAllText(Path.Combine(dir, $"track{i}.flac"), "");
        for (var i = 0; i < mp3Count; i++) File.WriteAllText(Path.Combine(dir, $"track{i}.mp3"), "");
        return dir;
    }

    /// <summary>The default one-shot invocation: source tools/preflight.sh, run the exact two
    /// entry points launch.sh and build.sh call. N3 (post-v5.3.0 gate review): a caller that
    /// needs a DIFFERENT bash script (e.g. calling preflight_print_report a second time to prove
    /// idempotency) passes its own `script` to RunPreflight below rather than hand-rolling a
    /// second copy of this whole method.</summary>
    const string DefaultPreflightScript =
        "set -euo pipefail; . tools/preflight.sh; preflight_docker; preflight_env_secrets";

    /// <summary>Sources the real tools/preflight.sh and calls the exact two entry points launch.sh
    /// and build.sh call, under the caller's own set -euo pipefail discipline.</summary>
    static (int ExitCode, string StdOut, string StdErr) RunPreflight(
        string binDir, string? envFile = null, IReadOnlyDictionary<string, string>? extraEnv = null,
        string script = DefaultPreflightScript)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);

        startInfo.Environment["PATH"] = binDir;
        foreach (var name in RequiredEnvVars) startInfo.Environment.Remove(name);
        foreach (var name in SeamEnvVars) startInfo.Environment.Remove(name);
        if (envFile is not null)
            startInfo.Environment["GW_ENV_FILE"] = envFile;
        if (extraEnv is not null)
            foreach (var (key, value) in extraEnv)
                startInfo.Environment[key] = value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start tools/preflight.sh");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdOut, stdErr);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the password posture (AC1)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioAdminPasswordPosture
    {
        [Fact]
        public void ChangeMePlaceholderHardFailsNamingTheVariable()
        {
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1), "ADMIN_PASSWORD=change-me-admin-ui");

            var (_, _, stdErr) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.Contains("ADMIN_PASSWORD", stdErr);
        }

        [Fact]
        public void EmptyAdminPasswordPasses()
        {
            // Empty = the documented appliance posture — exit code stays success.
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (exitCode, _, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void EmptyAdminPasswordWarnsWithTheFailClosedExplanation()
        {
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (_, stdOut, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.Contains("fail-closed", stdOut);
        }

        [Fact]
        public void SetAdminPasswordWithAdminProfileOffExplainsWhyItIsCheckedAnyway()
        {
            // Finding 4 (post-v5.3.0 gate run): Dean's own reaction to a no-admin wizard run —
            // "it mentions that ADMIN_PASSWORD was set, but I didn't enable the Admin UI, so
            // not sure why it bothered?" The check itself stays correct (F134.1: the secret
            // has to be healthy NOW so the handoff's own "add 'admin' and re-run" enable-later
            // path works without a placeholder/empty surprise at that point) — the row just has
            // to say why. No COMPOSE_PROFILES line at all here, the same shape a declined-admin
            // wizard run leaves.
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1), "ADMIN_PASSWORD=a-real-admin-password-0123456789");

            var (exitCode, stdOut, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.True(
                exitCode == 0 && stdOut.Contains("Admin UI is off", StringComparison.Ordinal),
                $"expected the PASS row to explain itself when the admin profile isn't selected; stdout:\n{stdOut}");
        }

        [Fact]
        public void SetAdminPasswordWithAdminProfileOnKeepsTheUnqualifiedCopy()
        {
            // The flip side: admin genuinely selected (COMPOSE_PROFILES=admin) keeps the
            // original, unqualified "Set (value not shown)" — no unnecessary caveat when the
            // surface the password guards is actually on.
            var mediaDir = MakeMediaDir(flacCount: 1);
            var envFile = WriteEnvFile(
                "POSTGRES_PASSWORD=x", "LIBRARY_DB_PASSWORD=x", "STATION_DB_PASSWORD=x",
                "ICECAST_SOURCE_PASSWORD=x", "ICECAST_ADMIN_PASSWORD=x", $"MEDIA_DIR={mediaDir}",
                "ADMIN_PASSWORD=a-real-admin-password-0123456789", "COMPOSE_PROFILES=admin");

            var (exitCode, stdOut, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.True(
                exitCode == 0 &&
                stdOut.Contains("Set (value not shown)", StringComparison.Ordinal) &&
                !stdOut.Contains("Admin UI is off", StringComparison.Ordinal),
                $"expected the unqualified copy when the admin profile IS selected; stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the compose floor (AC2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioComposeVersionFloor
    {
        [Fact]
        public void ComposeOlderThan224HardFailsCitingTheOverrideResetFloor()
        {
            var bin = MakeBinDir();
            AddStub(bin, "docker",
                """
                if [ "${1:-}" = "info" ]; then exit 0; fi
                if [ "${1:-}" = "compose" ] && [ "${2:-}" = "version" ]; then echo "Docker Compose version v2.20.0"; exit 0; fi
                exit 0
                """);

            var (exitCode, _, _) = RunPreflight(bin);

            Assert.Equal(3, exitCode);
        }

        [Fact]
        public void ComposeAtOrAbove224Passes()
        {
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (exitCode, _, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.Equal(0, exitCode);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — ports before launch (AC3)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioPortAvailability
    {
        const string SsBoundPort8080 =
            """
            cat <<'OUT'
            State   Recv-Q  Send-Q   Local Address:Port   Peer Address:Port  Process
            LISTEN  0       128            0.0.0.0:8080        0.0.0.0:*      users:(("dotnet",pid=4242,fd=23))
            OUT
            """;

        static string BinWithPort8080Bound()
        {
            var bin = HealthyDockerBin();
            AddStub(bin, "ss", SsBoundPort8080);
            return bin;
        }

        /// <summary>ss reports port 8000 bound; docker ps reports a container publishing that
        /// exact port — the F134.3b "restart/upgrade on a broadcasting box" case.</summary>
        static string BinWithOwnPort8000Bound()
        {
            var bin = MakeBinDir();
            AddStub(bin, "docker",
                """
                if [ "${1:-}" = "info" ]; then exit 0; fi
                if [ "${1:-}" = "compose" ] && [ "${2:-}" = "version" ]; then echo "Docker Compose version v2.24.5"; exit 0; fi
                if [ "${1:-}" = "ps" ]; then echo "0.0.0.0:8000->8000/tcp, :::8000->8000/tcp"; exit 0; fi
                exit 0
                """);
            AddStub(bin, "ss",
                """
                cat <<'OUT'
                State   Recv-Q  Send-Q   Local Address:Port   Peer Address:Port  Process
                LISTEN  0       128            0.0.0.0:8000        0.0.0.0:*      users:(("docker-proxy",pid=111,fd=7))
                OUT
                """);
            return bin;
        }

        [Fact]
        public void ABoundRequiredPortFailsNamingThePort()
        {
            var (_, _, stdErr) = RunPreflight(BinWithPort8080Bound());

            Assert.Contains("8080", stdErr);
        }

        [Fact]
        public void ABoundRequiredPortFailureNamesTheOwningProcess()
        {
            var (_, _, stdErr) = RunPreflight(BinWithPort8080Bound());

            Assert.Contains("dotnet", stdErr);
        }

        [Fact]
        public void AForeignPortConflictExitsWithCodeThree()
        {
            // Exit-code pin (not just the message) — docker ps (HealthyDockerBin) reports no
            // published ports, so 8080 bound by an unrelated "dotnet" process is a genuine
            // conflict and must hard-fail.
            var (exitCode, _, _) = RunPreflight(BinWithPort8080Bound());

            Assert.Equal(3, exitCode);
        }

        [Fact]
        public void APortHeldByThisStacksOwnContainerDoesNotBlock()
        {
            // F134.3b: a port bound by THIS compose project's own container is a PASS, not a
            // conflict — restarts and --pinned upgrades on a broadcasting box must sail through.
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (exitCode, _, _) = RunPreflight(BinWithOwnPort8000Bound(), envFile);

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void PortsOutsideTheSelectedTopologyAreNotChecked()
        {
            // piper-only (no admin profile) doesn't check the admin-ui port (3000) — a stub
            // that binds ONLY 3000, with no COMPOSE_PROFILES=admin in play, must still pass.
            var bin = HealthyDockerBin();
            AddStub(bin, "ss",
                """
                cat <<'OUT'
                State   Recv-Q  Send-Q   Local Address:Port   Peer Address:Port  Process
                LISTEN  0       128            0.0.0.0:3000        0.0.0.0:*      users:(("node",pid=999,fd=10))
                OUT
                """);
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (exitCode, _, _) = RunPreflight(bin, envFile);

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void ThePassRowStatesItIsALocalCheckAndNamesTheFirewallCaveat()
        {
            // Finding 2 (post-v5.3.0 gate run): "8000 8080 8081 3000 free (or published by a
            // Docker container)" reads as a reachability guarantee it never was — this is a
            // local bind check only (this machine's own sockets, or already Docker-owned).
            // External reachability is the operator's host/cloud firewall's call, not this
            // script's — an hour lost on Hetzner to exactly that misreading. An `ss` stub
            // (nothing bound) is required to reach the clean PASS branch at all — the dev/CI
            // host running this suite has no `ss` on PATH, which would otherwise degrade to the
            // (unrelated) "not checked" WARN this fact isn't about.
            var bin = HealthyDockerBin();
            AddStub(bin, "ss",
                """
                cat <<'OUT'
                State   Recv-Q  Send-Q   Local Address:Port   Peer Address:Port  Process
                OUT
                """);
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (_, stdOut, _) = RunPreflight(bin, envFile);

            Assert.True(
                stdOut.Contains("available locally", StringComparison.Ordinal) &&
                stdOut.Contains("host/cloud firewall", StringComparison.Ordinal),
                $"expected the ports PASS row to scope itself to a local check and name the firewall caveat; stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — resources vs topology (AC4)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioResourceChecks
    {
        [Fact]
        public void DiskUnderTheTopologyConstantReportsTheThreshold()
        {
            var bin = HealthyDockerBin();
            AddStub(bin, "df",
                """
                echo "Filesystem     1024-blocks      Used Available Capacity Mounted on"
                echo "tmpfs             2000000    1000000    900000       53% /"
                """);
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            // GW_PREFLIGHT_TOPOLOGY unset (the caller's default, per F134.3a) — defaults to the
            // full-stack (~12 GiB) constant.
            var (_, stdOut, _) = RunPreflight(bin, envFile);

            Assert.Contains("12 GiB", stdOut);
        }

        [Fact]
        public void DiskUnderThePiperOnlyTopologyReportsTheLighterThreshold()
        {
            // GW_PREFLIGHT_TOPOLOGY=piper-only — the caller-resolved input (F134.3a) — must
            // drive the lighter (~4 GiB) constant instead of the full-stack one.
            var bin = HealthyDockerBin();
            AddStub(bin, "df",
                """
                echo "Filesystem     1024-blocks      Used Available Capacity Mounted on"
                echo "tmpfs              500000     300000    200000       60% /"
                """);
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (_, stdOut, _) = RunPreflight(
                bin, envFile,
                extraEnv: new Dictionary<string, string> { ["GW_PREFLIGHT_TOPOLOGY"] = "piper-only" });

            Assert.Contains("4 GiB", stdOut);
        }

        [Fact]
        public void DockerRootDirFallbackFailureStillWarnsOnTheDirectCallPath()
        {
            // F1 regression: preflight_docker_root_dir used to return its target on stdout via
            // `target="$(preflight_docker_root_dir)"` — a command substitution that runs the
            // function in a subshell, so the fallback's preflight_record WARN appended to that
            // subshell's own copy of the row arrays and vanished the instant it exited. Proven
            // here by failing BOTH `docker info --format` and the conventional /var/lib/docker
            // fallback (GW_DOCKER_ROOT_FALLBACK points at a path that does not exist) — the WARN
            // must still reach the rendered summary table.
            var bin = MakeBinDir();
            AddStub(bin, "docker",
                """
                if [ "${1:-}" = "info" ] && [ "${2:-}" = "--format" ]; then exit 1; fi
                if [ "${1:-}" = "info" ]; then exit 0; fi
                if [ "${1:-}" = "compose" ] && [ "${2:-}" = "version" ]; then echo "Docker Compose version v2.24.5"; exit 0; fi
                exit 0
                """);
            AddStub(bin, "df",
                """
                echo "Filesystem     1024-blocks      Used Available Capacity Mounted on"
                echo "tmpfs             2000000    1000000    900000       53% /"
                """);
            var missingFallback = Path.Combine(
                Directory.CreateTempSubdirectory("gw-preflight-story342-nodockerroot-").FullName, "does-not-exist");
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (_, stdOut, _) = RunPreflight(
                bin, envFile,
                extraEnv: new Dictionary<string, string> { ["GW_DOCKER_ROOT_FALLBACK"] = missingFallback });

            Assert.Contains("Could not determine Docker's storage root", stdOut);
        }

        [Fact]
        public void RamUnderTheFullTopologyConstantSuggestsPiperOnly()
        {
            var meminfo = Path.Combine(Directory.CreateTempSubdirectory("gw-preflight-story342-ram-").FullName, "meminfo");
            File.WriteAllText(meminfo, "MemTotal:        3945000 kB\nMemFree:          100000 kB\n");
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (_, stdOut, _) = RunPreflight(
                HealthyDockerBin(), envFile,
                extraEnv: new Dictionary<string, string> { ["GW_MEMINFO_FILE"] = meminfo });

            Assert.Contains("piper-only", stdOut);
        }

        [Fact]
        public void PiWithoutCgroupMemoryWarnsThatMemLimitsAreDiscarded()
        {
            // cmdline.txt probe (test seam points at a scratch file) missing
            // cgroup_enable=memory → WARN with the HARDWARE.md pointer.
            var cmdline = Path.Combine(Directory.CreateTempSubdirectory("gw-preflight-story342-cmdline-").FullName, "cmdline.txt");
            File.WriteAllText(cmdline, "console=serial0,115200 root=PARTUUID=xyz rootfstype=ext4 rootwait\n");
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (_, stdOut, _) = RunPreflight(
                HealthyDockerBin(), envFile,
                extraEnv: new Dictionary<string, string> { ["GW_CMDLINE_FILE"] = cmdline });

            Assert.Contains("HARDWARE.md", stdOut);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — MEDIA_DIR deep checks (AC5)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioMediaDirDeepChecks
    {
        [Fact]
        public void AnUnreadableMediaDirFails()
        {
            var mediaDir = MakeMediaDir();
            // bash targets only — these specs only ever run on the Linux dev/CI hosts (guard exists
            // to satisfy CA1416, not to support Windows).
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(mediaDir, UnixFileMode.None);
            var envFile = CompleteEnvFile(mediaDir);

            var (exitCode, _, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.Equal(3, exitCode);
        }

        [Fact]
        public void ZeroAudioFilesReportsTheNoMusicRoute()
        {
            var envFile = CompleteEnvFile(MakeMediaDir());

            var (_, stdOut, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.Contains("no-music", stdOut);
        }

        [Fact]
        public void UppercaseExtensionFilesCountTowardTheTotal()
        {
            // The scanner (ScanService.cs) lowercases the extension before matching — an
            // uppercase Track01.FLAC must count here exactly as it counts to the scanner.
            var mediaDir = MakeMediaDir();
            File.WriteAllText(Path.Combine(mediaDir, "Track01.FLAC"), "");
            var envFile = CompleteEnvFile(mediaDir);

            var (_, stdOut, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.Contains("1 .flac/.mp3 files found", stdOut);
        }

        [Fact]
        public void AnNfsMediaDirPrintsTheStaleInodeAndCaseNotes()
        {
            var mediaDir = MakeMediaDir(flacCount: 1);
            var mounts = Path.Combine(Directory.CreateTempSubdirectory("gw-preflight-story342-mounts-").FullName, "mounts");
            File.WriteAllLines(mounts,
            [
                "/dev/sda1 / ext4 rw 0 0",
                $"nas:/export/media {mediaDir} nfs4 rw 0 0",
            ]);
            var envFile = CompleteEnvFile(mediaDir);

            var (_, stdOut, _) = RunPreflight(
                HealthyDockerBin(), envFile,
                extraEnv: new Dictionary<string, string> { ["GW_MOUNTS_FILE"] = mounts });

            Assert.Contains("stale inode", stdOut);
        }

        [Fact]
        public void ALocalMediaDirNamesTheDetectedFilesystemType()
        {
            // Finding 6 (post-v5.3.0 gate run): the old copy read "local (ext4)" — good — but
            // fell back to the self-contradictory "local (unknown)" once the type couldn't be
            // determined (see the next fact). Capitalized and reworded to "Local disk (...)"
            // either way, matching the neighboring "NFS-mounted (...)" row's own capitalization.
            var mediaDir = MakeMediaDir(flacCount: 1);
            var mounts = Path.Combine(Directory.CreateTempSubdirectory("gw-preflight-story342-mounts-").FullName, "mounts");
            File.WriteAllLines(mounts, [$"/dev/sda1 {mediaDir} ext4 rw 0 0"]);
            var envFile = CompleteEnvFile(mediaDir);

            var (_, stdOut, _) = RunPreflight(
                HealthyDockerBin(), envFile,
                extraEnv: new Dictionary<string, string> { ["GW_MOUNTS_FILE"] = mounts });

            Assert.Contains("Local disk (ext4)", stdOut, StringComparison.Ordinal);
        }

        [Fact]
        public void AMediaDirWithNoMatchingMountEntrySaysTypeNotIdentifiedNotUnknown()
        {
            // Finding 6: an unreadable/no-match mount table used to print "local (unknown)" —
            // "local" itself read as though IT were unverified, when only the TYPE is. Honest
            // wording now, and the parenthetical stays reserved for an actual detected type.
            var mediaDir = MakeMediaDir(flacCount: 1);
            var mounts = Path.Combine(Directory.CreateTempSubdirectory("gw-preflight-story342-mounts-").FullName, "mounts");
            File.WriteAllLines(mounts, ["/dev/sda1 / ext4 rw 0 0"]);   // no entry matching mediaDir
            var envFile = CompleteEnvFile(mediaDir);

            var (_, stdOut, _) = RunPreflight(
                HealthyDockerBin(), envFile,
                extraEnv: new Dictionary<string, string> { ["GW_MOUNTS_FILE"] = mounts });

            Assert.True(
                stdOut.Contains("Local disk (type not identified)", StringComparison.Ordinal) &&
                !stdOut.Contains("unknown)", StringComparison.Ordinal),
                $"expected honest 'type not identified' wording, never the self-contradictory 'local (unknown)'; stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — one table, one escape (AC6)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioReportingAndEscape
    {
        [Fact]
        public void ResultsRenderAsOnePassWarnFailTable()
        {
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));

            var (_, stdOut, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.Contains("==> Preflight summary", stdOut);
        }

        [Fact]
        public void SkipPreflightStillBypassesEverything()
        {
            // No docker anywhere on PATH — with every check bypassed, the two entry points
            // return cleanly instead of ever reaching a check that would fail on its absence.
            var (exitCode, _, _) = RunPreflight(
                MakeBinDir(), extraEnv: new Dictionary<string, string> { ["SKIP_PREFLIGHT"] = "1" });

            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void CallingPreflightPrintReportTwiceRendersTheTableOnlyOnce()
        {
            // Finding 5 (post-v5.3.0 gate run): Dean's vmtest transcript showed the full
            // preflight table TWICE — once from the real launch.sh's own subprocess preflight
            // pass (a redundant re-check setup.sh now skips via SKIP_PREFLIGHT=1 on that one
            // call) and again from setup.sh's own EXIT trap at true process exit, after the
            // handoff. setup.sh now also calls preflight_print_report explicitly, right after
            // the checks that populate it — this pins the underlying guard that makes calling
            // it more than once in the SAME process a no-op after the first render, exactly
            // the shape setup.sh's explicit call + its EXIT trap's own call now both hit.
            var envFile = CompleteEnvFile(MakeMediaDir(flacCount: 1));
            const string script =
                "set -euo pipefail; . tools/preflight.sh; preflight_docker; preflight_env_secrets; " +
                "preflight_print_report; preflight_print_report";

            var (_, stdOut, stdErr) = RunPreflight(HealthyDockerBin(), envFile, script: script);

            var occurrences = Regex.Matches(stdOut, Regex.Escape("==> Preflight summary")).Count;
            Assert.True(occurrences == 1,
                $"expected exactly one render across two explicit calls; occurrences={occurrences} stderr={stdErr} stdout:\n{stdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the existing contract survives the expansion
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioExistingChecksAreUntouched
    {
        [Fact]
        public void TheSixRequiredEnvVarsStillHardFailWhenMissing()
        {
            // The Gh019 suite's contract holds byte-for-byte — expansion adds, never relaxes.
            var envFile = WriteEnvFile(
                "POSTGRES_PASSWORD=x", "STATION_DB_PASSWORD=x",
                "ICECAST_SOURCE_PASSWORD=x", "ICECAST_ADMIN_PASSWORD=x",
                $"MEDIA_DIR={MakeMediaDir(flacCount: 1)}");

            var (exitCode, _, _) = RunPreflight(HealthyDockerBin(), envFile);

            Assert.Equal(3, exitCode);
        }
    }
}
