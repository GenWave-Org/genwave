// STORY-343 — Staged startup: on air before the heavyweights (F136)
//
// BDD specification — xUnit. Two lanes: (a) semantic asserts over `compose.yaml`
// source (the repo-content fact idiom) for the required:false dependency; (b) the
// REAL ./launch.sh via Process in --dry-run (exits before any docker call — the
// Story201 idiom, safe anywhere) for the staged plan and GW_PRESET.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GenWave.Host.Tests.Specs;

public static class FeatureStagedStartup
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107's convention).</summary>
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        if (dir is null) throw new InvalidOperationException("repo root (GenWave.sln) not found");
        return dir.FullName;
    }

    static string ComposeYamlPath => Path.Combine(RepoRoot(), "compose.yaml");

    static string ComposeYamlText => File.ReadAllText(ComposeYamlPath);

    /// <summary>Outcome of <see cref="CheckApiKokoroDependsOnRequiredFalse"/> — a pure predicate result, not
    /// an assertion: <see cref="Satisfied"/> reports pass/fail and <see cref="FailureReason"/> carries enough
    /// detail for the one caller-side Assert to explain itself (the path is the caller's to add).</summary>
    internal readonly record struct KokoroRequiredFalseCheck(bool Satisfied, string FailureReason);

    /// <summary>
    /// Locates the api service's <c>depends_on: kokoro:</c> block in compose.yaml source and
    /// reports whether it carries a <c>required: false</c> line (SPEC F136.2). Targeted line
    /// parser bounded to compose.yaml's current indentation — the Story107/Story151/Story160
    /// repo-content-fact idiom, no YAML package needed. Pure predicate: no Asserts inside, so
    /// the one calling Fact can assert exactly once with a message it composes itself.
    /// </summary>
    internal static KokoroRequiredFalseCheck CheckApiKokoroDependsOnRequiredFalse(string composeYamlText)
    {
        var lines = composeYamlText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var apiServiceStart = Array.FindIndex(lines, line => line == "  api:");
        if (apiServiceStart < 0)
            return new KokoroRequiredFalseCheck(false, "could not locate the '  api:' service block");

        // The api service block runs until the next line at the same (2-space) service indent.
        var apiServiceEnd = lines.Length;
        for (var i = apiServiceStart + 1; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], @"^  \S"))
            {
                apiServiceEnd = i;
                break;
            }
        }

        var kokoroStart = -1;
        for (var i = apiServiceStart + 1; i < apiServiceEnd; i++)
        {
            if (lines[i].Trim() == "kokoro:")
            {
                kokoroStart = i;
                break;
            }
        }
        if (kokoroStart < 0)
            return new KokoroRequiredFalseCheck(false, "could not locate api's depends_on: kokoro: block");

        var kokoroIndent = lines[kokoroStart].Length - lines[kokoroStart].TrimStart().Length;
        for (var i = kokoroStart + 1; i < apiServiceEnd; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;

            var indent = line.Length - line.TrimStart().Length;
            if (indent <= kokoroIndent) break;   // left the kokoro: sub-block

            if (line.Trim() == "required: false") return new KokoroRequiredFalseCheck(true, "");
        }

        return new KokoroRequiredFalseCheck(false,
            "found api's depends_on: kokoro: block but it does not carry 'required: false' (SPEC F136.2)");
    }

    static (int ExitCode, string StdOut, string StdErr) RunLaunch(
        string? gwEnvFile, params string[] args)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(RepoRoot(), "launch.sh"));
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        // Scrub ambient topology inputs from the child's environment: a test-runner shell
        // that happens to export GW_PRESET (or launch.sh's own GW_PREFLIGHT_TOPOLOGY/
        // GW_PREFLIGHT_DEMO — meant to flow parent-launch.sh -> child-preflight only within a
        // single real run) must never silently steer a spec's chosen topology out from under
        // it. ProcessStartInfo.Environment starts as a copy of this process's own environment,
        // so these need an explicit Remove even though the test never sets them itself.
        startInfo.Environment.Remove("GW_PRESET");
        startInfo.Environment.Remove("GW_PREFLIGHT_TOPOLOGY");
        startInfo.Environment.Remove("GW_PREFLIGHT_DEMO");
        if (gwEnvFile is not null) startInfo.Environment["GW_ENV_FILE"] = gwEnvFile;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("failed to start launch.sh");

        // Concurrent reads, not sequential ReadToEnd() + WaitForExit(): a child writing enough
        // to fill both OS pipe buffers at once can deadlock a reader that drains one stream to
        // completion before starting the other, because the child is itself blocked writing to
        // the stream nobody is draining yet.
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        Task.WaitAll(stdOutTask, stdErrTask);
        process.WaitForExit();

        return (process.ExitCode, stdOutTask.Result, stdErrTask.Result);
    }

    static string[] PlanLines(string stdOut) =>
        stdOut.Split('\n').Where(l => l.StartsWith("plan> ", StringComparison.Ordinal)).ToArray();

    /// <summary>Writes a scratch env file (the Gh332 GW_ENV_FILE idiom) — never the real .env.</summary>
    static string WriteEnvFile(params string[] assignments)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("story343-env-").FullName, "test.env");
        File.WriteAllLines(path, assignments);
        return path;
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — core up without kokoro (AC1)
    // ---------------------------------------------------------------------

    [Trait("Category", "Unit")]
    public sealed class ScenarioApiKokoroDependencyIsOptional
    {
        [Fact]
        public void ComposeDeclaresRequiredFalseOnApisKokoroDependency()
        {
            var check = CheckApiKokoroDependsOnRequiredFalse(ComposeYamlText);
            Assert.True(check.Satisfied, $"{check.FailureReason} in {ComposeYamlPath}.");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the staged plan (AC1/AC2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioStagedDryRunPlansCoreBeforeHeavyweights
    {
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunLaunch(null, "--pinned", "--dry-run"));

        static string[] Lines => PlanLines(Run.Value.StdOut);

        static int CorePullIndex => Array.FindIndex(Lines, l =>
            l.Contains("pull", StringComparison.Ordinal) && l.Contains("icecast", StringComparison.Ordinal));

        static int CoreUpIndex => Array.FindIndex(Lines, l =>
            l.Contains("up -d --remove-orphans", StringComparison.Ordinal) && l.Contains("icecast", StringComparison.Ordinal));

        // Stage 2 (F136 review findings F2/F3) is deliberately UNSCOPED — its pull/up lines
        // name no services at all — so "not the core pull/up" (no `icecast`) is what locates
        // them among the plan's other lines.
        static int StageTwoPullIndex => Array.FindIndex(Lines, l =>
            l.Contains("pull", StringComparison.Ordinal) && !l.Contains("icecast", StringComparison.Ordinal));

        static int StageTwoUpIndex => Array.FindIndex(Lines, l =>
            l.Contains("up -d --remove-orphans", StringComparison.Ordinal) && !l.Contains("icecast", StringComparison.Ordinal));

        [Fact]
        public void TheStagedPlanPullsCoreServicesFirst()
        {
            // --dry-run plan> lines: the core pull (db icecast engine api [piper]) precedes
            // stage 2's unscoped pull.
            Assert.True(CorePullIndex >= 0 && StageTwoPullIndex > CorePullIndex,
                $"expected the core pull before stage 2's pull; plan:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void TheStagedPlanBringsCoreUpBeforeStageTwoPulls()
        {
            Assert.True(CoreUpIndex >= 0 && CoreUpIndex < StageTwoPullIndex,
                $"expected the core `up -d` between the core pull and stage 2's pull; plan:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void TheCoreUpLineCarriesNoDeps()
        {
            // F136 review finding F1 (live-daemon-proven, 2026-08-18): a scoped `up` still
            // starts its targets' depends_on set regardless of required:false — that flag
            // only relaxes the health gate, not membership. Without --no-deps here, api's
            // depends_on: kokoro: drags kokoro into the scoped core up, defeating the split.
            Assert.True(CoreUpIndex >= 0 && Lines[CoreUpIndex].Contains("--no-deps", StringComparison.Ordinal),
                $"expected the core `up -d --remove-orphans` line to carry --no-deps; plan:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void TheSecondUpComesAfterStageTwosPull()
        {
            // Nothing already up (the core) restarts, because it's this pull that fetches the
            // new pins the up would otherwise recreate the core onto.
            Assert.True(StageTwoUpIndex >= 0 && StageTwoUpIndex > StageTwoPullIndex,
                $"expected a second `up -d --remove-orphans` after stage 2's pull; plan:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void TheSecondUpLineIsUnscoped()
        {
            // F136 review findings F2/F3 (live-daemon-proven): a scoped up-of-heavyweights-
            // only never reaches services that are neither depended-on nor core-listed at all
            // (caddy, admin_ui, alloy, cloudflared, dockerproxy) — only the ORIGINAL unscoped
            // up converges the full profile-selected set.
            Assert.True(StageTwoUpIndex >= 0 && Lines[StageTwoUpIndex].TrimEnd().EndsWith("--remove-orphans", StringComparison.Ordinal),
                $"expected the second `up -d --remove-orphans` line to be unscoped (no trailing service names); plan:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void TheSecondUpLineDoesNotCarryNoRecreate()
        {
            // F136 review findings F2/F3 (live-daemon-proven): --no-recreate never recreates a
            // service whose PIN changed (only one that's entirely absent) — proven to leave
            // admin_ui/caddy/ollama STALE across an upgrade (the gh-#93 regression class).
            Assert.True(StageTwoUpIndex >= 0 && !Lines[StageTwoUpIndex].Contains("--no-recreate", StringComparison.Ordinal),
                $"expected the second `up -d --remove-orphans` line to NOT carry --no-recreate; plan:\n{Run.Value.StdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — --piper-only pinned stays fully unstaged (F136 review finding F1/F2)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioPiperOnlyPinnedPlanStaysUnstaged
    {
        // compose.piper-only.yaml profile-gates kokoro/ollama/ollama-init off unconditionally,
        // so this topology has nothing to stage behind — it must plan the exact pre-F136
        // shape: one pull, one up, both unscoped. Pinned this way because round 2 shipped an
        // unconditional `--no-deps` on the real invocation (F136 review finding F1) that this
        // dry-run plan never exercised — this scenario is the one that must go red against it.
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunLaunch(null, "--pinned", "--piper-only", "--dry-run"));

        static string[] Lines => PlanLines(Run.Value.StdOut);

        static string[] PullLines =>
            Lines.Where(l => l.Contains("pull", StringComparison.Ordinal)).ToArray();

        static string[] UpLines =>
            Lines.Where(l => l.Contains("up -d --remove-orphans", StringComparison.Ordinal)).ToArray();

        [Fact]
        public void ExitsZero()
        {
            Assert.True(Run.Value.ExitCode == 0,
                $"expected a clean dry-run exit; exit={Run.Value.ExitCode} stderr={Run.Value.StdErr}");
        }

        [Fact]
        public void PlansExactlyOnePullLine()
        {
            Assert.True(PullLines.Length == 1,
                $"expected exactly one pull line (nothing to stage behind); plan:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void PlansExactlyOneUpLine()
        {
            // No stage 2 at all under --piper-only: a second `up -d --remove-orphans` line
            // here would mean the piper-only pinned flow got staged when it must not be.
            Assert.True(UpLines.Length == 1,
                $"expected exactly one `up -d --remove-orphans` line (no stage 2); plan:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void TheUpLineDoesNotCarryNoDeps()
        {
            // The unstaged contract: --no-deps is a staged-flow-only flag. An unconditional
            // --no-deps on the real invocation (F136 review finding F1) would show up here.
            Assert.True(UpLines.Length == 1 && !UpLines[0].Contains("--no-deps", StringComparison.Ordinal),
                $"expected the `up -d --remove-orphans` line to NOT carry --no-deps; plan:\n{Run.Value.StdOut}");
        }

        [Fact]
        public void TheUpLineCarriesNoServiceList()
        {
            Assert.True(UpLines.Length == 1 && UpLines[0].TrimEnd().EndsWith("--remove-orphans", StringComparison.Ordinal),
                $"expected the `up -d --remove-orphans` line to name no services; plan:\n{Run.Value.StdOut}");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the preset persists (AC3)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioGwPresetAloneChoosesTheShape
    {
        // GW_ENV_FILE seam carries GW_PRESET=pinned-piper-only; ./launch.sh --dry-run with
        // no topology flags plans the piper-only pinned shape.
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunLaunch(WriteEnvFile("GW_PRESET=pinned-piper-only"), "--dry-run"));

        [Fact]
        public void ExitsZero()
        {
            Assert.True(Run.Value.ExitCode == 0,
                $"expected a clean dry-run exit; exit={Run.Value.ExitCode} stderr={Run.Value.StdErr}");
        }

        [Fact]
        public void PlansThePiperOnlyPinnedShapeFromGwPresetAlone()
        {
            Assert.Contains(PlanLines(Run.Value.StdOut), l => l.Contains("compose.piper-only.yaml", StringComparison.Ordinal));
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioExplicitFlagOverridesGwPreset
    {
        // GW_PRESET=dev in the env file, but an explicit --pinned flag is given — the flag
        // wins, so the plan shows the pinned/demo-overlay shape, not the dev flow.
        static readonly Lazy<(int ExitCode, string StdOut, string StdErr)> Run =
            new(() => RunLaunch(WriteEnvFile("GW_PRESET=dev"), "--pinned", "--dry-run"));

        [Fact]
        public void ExitsZero()
        {
            Assert.True(Run.Value.ExitCode == 0,
                $"expected a clean dry-run exit; exit={Run.Value.ExitCode} stderr={Run.Value.StdErr}");
        }

        [Fact]
        public void PlansThePinnedDemoOverlayShapeNotTheDevFlow()
        {
            Assert.Contains(PlanLines(Run.Value.StdOut), l => l.Contains("compose.demo.yaml", StringComparison.Ordinal));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the ritual rides along (AC4) + contract holds
    // ---------------------------------------------------------------------

    [Trait("Category", "Unit")]
    public sealed class ScenarioTheHashEpochsAreRepinned
    {
        [Fact]
        public void TheZeroDiffHashGatesAreGreenOnTheEditedCompose()
        {
            // Not a new gate — this spec exists so T316's PR cannot merge with the existing
            // epoch facts red (the T85/T93 ritual, asserted from this story). Reuses the
            // existing pinned-hash fact/constant directly rather than duplicating the hash
            // literal here (FeatureFormatClockGate carries the current epoch's pin).
            FeatureFormatClockGate.ScenarioEngineAndComposeCarryZeroDiffFromMain.ComposeYamlByteMatchesMain();
        }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioUnknownGwPresetFailsLoud
    {
        [Fact]
        public void AnUnrecognizedGwPresetValueExitsWithGuidanceNotSilence()
        {
            var envFile = WriteEnvFile("GW_PRESET=piper-only-pinned");   // pre-F132.5 spelling — not in the closed vocabulary

            var (exitCode, _, stdErr) = RunLaunch(envFile, "--dry-run");

            Assert.True(exitCode != 0 && stdErr.Contains("GW_PRESET", StringComparison.Ordinal),
                $"expected a loud, non-zero exit naming GW_PRESET; exit={exitCode} stderr={stdErr}");
        }
    }
}
