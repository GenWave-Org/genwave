// STORY-129 — Safe plays air level-matched with honest gainDb (Epic U / SPEC F37, closes gitea-#200)
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-13, house rule since Epic S):
// every fact is Skip-pinned to the task that will prove it and carries an Assert.Fail body so an
// accidentally-unskipped run is loud, never silently green. U3 lands the two genwave.liq edits
// (safe-branch amplify + replay_gain export) and converts the repo-content facts to real greps;
// U7 proves the live halves against U1's recorded baselines on a scratch stack.

using System.Diagnostics;

namespace GenWave.Host.Tests.Specs;

public static class FeatureSafePathLevelMatching
{
    /// <summary>Repo root, resolved relative to the test assembly's build output (Story074/102/107's convention).</summary>
    static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    static string EngineMetadataSourceText =>
        File.ReadAllText(Path.Combine(RepoRoot, "src", "GenWave.Core", "Domain", "EngineMetadata.cs"));

    static string EngineScriptText =>
        File.ReadAllText(Path.Combine(RepoRoot, "engine", "genwave.liq"));

    /// <summary>The `settings.encoder.metadata.export` append list's own text — sliced narrowly so a
    /// membership check can't false-positive against the other, unrelated `replay_gain` mentions
    /// (the override= call, code comments) elsewhere in the script.</summary>
    static string ExportAppendListText(string script)
    {
        const string marker = "list.append(settings.encoder.metadata.export(),";
        var start = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "settings.encoder.metadata.export list.append(...) call not found in genwave.liq");
        var end = script.IndexOf("])", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "closing '])' for the export list.append(...) call not found in genwave.liq");
        return script[start..(end + 2)];
    }

    public sealed class ScenarioEngineGraphAppliesGainOnTheSafeBranch
    {
        const string AmplifyLine = "safe = amplify(1., override=\"replay_gain\", safe)";
        const string GapAppendLine = "append(safe, fun (_) -> blank(duration=gw_safe_gap_seconds))";

        [Fact]
        public void SafeSourceIsWrappedByAmplifyWithTheReplayGainOverride()
        {
            // F37.1: the safe_lib request.dynamic source (rebound as `safe`) is wrapped by the same
            // amplify(1., override="replay_gain", …) operator the main queue already uses — mirrored
            // verbatim so safe plays level-match main-rotation plays.
            Assert.Contains(AmplifyLine, EngineScriptText, StringComparison.Ordinal);
        }

        [Fact]
        public void AmplifySitsBeforeTheGapAppendSoBlankGapsCarryNoGain()
        {
            // F37.1: placed BEFORE the F29.6 gap append block — the blank(duration=...) gap track has
            // no replay_gain annotation of its own, so it must never pass through the amplify wrapper.
            var script = EngineScriptText;
            var amplifyIndex = script.IndexOf(AmplifyLine, StringComparison.Ordinal);
            var gapAppendIndex = script.IndexOf(GapAppendLine, StringComparison.Ordinal);

            Assert.True(amplifyIndex >= 0, "safe-branch amplify line not found in genwave.liq");
            Assert.True(gapAppendIndex >= 0, "F29.6 gap append block not found in genwave.liq");
            Assert.True(amplifyIndex < gapAppendIndex,
                "amplify must appear before the gap append so the blank gap carries no gain");
        }

        // T507: rewritten as a real Integration fact — a container makes the check repeatable
        // instead of a one-time recorded run (S8/T11's own evidence-pinned idiom, superseded here).
        [Fact]
        [Trait("Category", "Integration")]
        public void TheScriptTypechecksOnPinnedLiquidsoap()
        {
            // savonet/liquidsoap:v2.4.4 is the exact base the pinned engine/Dockerfile builds FROM —
            // compose.yaml's `engine` service has no top-level `image:`, it's a `build:` context, so
            // this is the pinned tag itself, not a proxy for it. No -e env dummies are needed: every
            // environment.get(...) call in the script already carries a default=. House R4/T11
            // convention: silence on stdout/stderr + exit code 0 = pass (F37.1).
            //
            // gh-#817: the pull is its OWN step. `docker run` writes its image-pull progress
            // ("Unable to find image '<tag>' locally …") to stderr, and the assertion below reads
            // stderr as liquidsoap's — so on a cold runner a passing check (exit 0, no liquidsoap
            // output) still went red. Pulling first and then running --pull=never guarantees the
            // checked run's streams carry nothing but liquidsoap's own words.
            const string image = "savonet/liquidsoap:v2.4.4";
            var engineDir = Path.Combine(RepoRoot, "engine");

            static (int ExitCode, string Stdout, string Stderr) Docker(params string[] args)
            {
                var info = new ProcessStartInfo("docker")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };

                foreach (var arg in args)
                {
                    info.ArgumentList.Add(arg);
                }

                using var process = Process.Start(info)
                    ?? throw new InvalidOperationException("Failed to start docker.");

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return (process.ExitCode, stdout, stderr);
            }

            // Arrange, not assert: a failed pull is an environment problem, so it throws with the
            // reason rather than reporting itself as a liquidsoap type error.
            var pull = Docker("pull", image);
            if (pull.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"docker pull {image} failed (exit {pull.ExitCode}): {pull.Stderr}");
            }

            var check = Docker(
                "run", "--rm", "--pull", "never",
                "-v", $"{engineDir}:/engine:ro",
                "--entrypoint", "liquidsoap",
                image,
                "--check", "/engine/genwave.liq");

            Assert.True(
                check.ExitCode == 0 && check.Stdout.Length == 0 && check.Stderr.Length == 0,
                $"liquidsoap --check failed (exit {check.ExitCode}): stdout=[{check.Stdout}] stderr=[{check.Stderr}]");
        }
    }

    public sealed class ScenarioReplayGainReachesTheOutputMetadata
    {
        [Fact]
        public void ReplayGainIsInTheEncoderExportAppendList()
        {
            // F37.2: "replay_gain" joins the settings.encoder.metadata.export append list so
            // engine-initiated plays' frames carry it — scoped to the append array's own text so an
            // unrelated `replay_gain` mention elsewhere in the script can't false-positive this fact.
            var exportListText = ExportAppendListText(EngineScriptText);
            Assert.Contains("replay_gain", exportListText, StringComparison.Ordinal);
        }

        // U7-rewritten (2026-07-13, u7smoke) — the live half this Skip text used to defer to U7.
        const string Skip = "manual: U7 (2026-07-13, u7smoke): PUT Station:SafeScope:LibraryIds=[1] so the "
            + "drain pulled Quiet Track (stamped gain 20.10 dB, NOT peak-capped — the target-reachable "
            + "row U3's own live half flagged as still owed). GET /api/now-playing during the drain: "
            + "mediaId=1, gainDb=20.1 — exactly Quiet Track's own stamped replay_gain — against U1(b)'s "
            + "gainDb:0 pre-fix baseline. Evidence: scratchpad u7/partb_now_playing.json, "
            + "partb_quiet_row.json.";

        [Trait("Category", "Integration")]
        [Fact(Skip = Skip)]
        public void EngineInitiatedPlaysReportTheStampedGainDbWithZeroCsharpChanges()
        {
            // U7 proved live: gainDb=20.1 matches Quiet Track's stamped replay_gain, zero C# changes
            // (F37.2–F37.3).
        }
    }

    public sealed class ScenarioDrainAirsAtTargetLoudness
    {
        // The recorded drain window landing at the configured target within tolerance (F37.3):
        // covered by stack_gate.sh --capture's ebur128 loudness measurement against the station's
        // own configured target (SPEC F178.5(b)) — the same live LUFS proof U7's one-time recording
        // (110s against a SafeScope=[1] drain rotation, -16.4/-16.3 LUFS against the -16 ±2.5 LU
        // band) stood in for before stack_gate.sh's capture leg existed (former fact
        // TheRecordedDrainWindowLandsAtTheConfiguredTargetWithinTolerance).
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioFalseDocumentationIsCorrected
    {
        [Fact]
        public void EngineMetadataXmlDocsNoLongerClaimAmplifyConsumesTheKey()
        {
            // Real, always-run, non-Skip repo-content assertion (Story102/107/S8's grep-assert
            // idiom) — no live stack needed. F37.4: neither the "consumed by amplify" claim (main-
            // queue tracks) nor the "bypass … survives to the output" claim (safe tracks) may
            // remain — both were falsified by the 2026-07-13 v2.4.4 source pass: amplify only READS
            // its override key and never deletes it; the missing key was always the
            // settings.encoder.metadata.export filter, not amplify consumption/bypass. Pinned on
            // distinctive OLD-text substrings being ABSENT and a distinctive NEW-text substring
            // (the export-list mechanism) being PRESENT.
            var source = EngineMetadataSourceText;

            Assert.DoesNotContain("have it consumed by", source, StringComparison.Ordinal);
            Assert.DoesNotContain("bypass the", source, StringComparison.Ordinal);
            Assert.DoesNotContain("survives to the output", source, StringComparison.Ordinal);
            Assert.Contains("settings.encoder.metadata.export", source, StringComparison.Ordinal);
        }
    }
}
