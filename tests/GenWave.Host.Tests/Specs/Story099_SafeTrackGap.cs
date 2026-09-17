// STORY-099 — Configurable gap between safe tracks (ENGINE WIRE) (Epic R / SPEC F29.6–F29.8, gitea-#182)
//
// BDD specification — xUnit. R4 un-pinned the runnable facts (script/compose file-content
// assertions) after the house `--check` spike (docs/MEMORY.md, "R4 spike verdict"). The gap
// lives in engine/genwave.liq — append() attaches a blank(duration=gap) track directly onto
// the safe branch after every safe track — driven by GW_SAFE_GAP_SECONDS (default 7.0, 0
// disables) plumbed through compose.yaml. Recorded-drain and cutback-latency proofs (T507) have no
// R13-only in-repo Fact and no repeating gate assertion either — the nearest live check is
// stack_gate.sh's api-down chaos scenario (SPEC F178.8a), which bounds total silence by the outage
// duration, not the configured gap seconds or a single switch cycle's latency; the runnable facts
// here pin the script/compose artifacts the way StoryF4/Story068/Story035/Story057's file-content
// facts do.

namespace GenWave.Host.Tests.Specs;

public static class FeatureSafeTrackGap
{
    // Path to the engine script, resolved relative to the solution root at test runtime —
    // the Story035/Story057/Story062 convention.
    static string ScriptPath =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "engine", "genwave.liq"));

    // Path to compose.yaml, resolved the same way — the Story074 convention.
    static string ComposePath =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "compose.yaml"));

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioEngineScriptCarriesTheGap
    {
        readonly string script = File.ReadAllText(ScriptPath);
        readonly string compose = File.ReadAllText(ComposePath);

        [Fact]
        public void ScriptReadsGwSafeGapSecondsFromTheEnvironment()
        {
            Assert.Contains("environment.get(\"GW_SAFE_GAP_SECONDS\")", script);
        }

        [Fact]
        public void ComposePlumbsTheVariableWithDefaultSeven()
        {
            Assert.Matches(@"GW_SAFE_GAP_SECONDS:\s*""\$\{GW_SAFE_GAP_SECONDS:-7(\.0)?\}""", compose);
        }

        [Fact]
        public void GapOperatorAttachesToTheSafeBranchOnly()
        {
            // The append/blank construct wraps `safe` (the safe branch) directly; main's
            // definition (the cross()-based rotation) carries no such reference (F29.7).
            Assert.Contains("append(safe, fun (_) -> blank(duration=gw_safe_gap_seconds))", script);

            var mainLine = script.Split('\n').Single(l => l.TrimStart().StartsWith("main = cross(", StringComparison.Ordinal));
            Assert.DoesNotContain("append(", mainLine);
            Assert.DoesNotContain("blank(", mainLine);
        }
    }

    public sealed class ScenarioLiveDrainGap
    {
        // Recorded drain output showing ≈gap seconds of silence between consecutive safe tracks,
        // and cutback to main within one source-switch cycle mid-gap: no gate assertion measures
        // either directly — stack_gate.sh --capture's api-down chaos scenario (F178.8a) bounds
        // TOTAL silence by the outage duration and checks a non-safe track_id returns within
        // 120 s, not the configured GW_SAFE_GAP_SECONDS value or a single source-switch cycle's
        // latency. Was a Skip-pinned gate: fact before T507 (former facts
        // RecordedDrainShowsTheConfiguredGapBetweenSafeTracks,
        // CutbackToMainHappensWithinOneSourceSwitchCycleMidGap).
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioNeverSilentContractHolds
    {
        readonly string script = File.ReadAllText(ScriptPath);

        [Fact]
        public void MksafeRemainsTheOuterLeaf()
        {
            // fallback([...]) still wraps into mksafe(...) — F4.4 unchanged by the gap (F29.8).
            Assert.Contains("genwave = fallback(track_sensitive=false, [main, safe])", script);
            Assert.Contains("genwave = mksafe(genwave)", script);
        }

        [Fact]
        public void ZeroDisablesTheGap()
        {
            // GW_SAFE_GAP_SECONDS=0 -> the else branch passes `safe` through unwrapped,
            // no append/blank wrapping — the exact pre-R4 graph shape (guarded in-script).
            Assert.Contains(
                "safe =\n  if gw_safe_gap_seconds > 0. then\n" +
                "    append(safe, fun (_) -> blank(duration=gw_safe_gap_seconds))\n" +
                "  else\n" +
                "    safe\n" +
                "  end",
                script);
        }
    }
}
