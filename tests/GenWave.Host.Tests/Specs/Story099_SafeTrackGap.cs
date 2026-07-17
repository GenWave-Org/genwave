// STORY-099 — Configurable gap between safe tracks (ENGINE WIRE) (Epic R / SPEC F29.6–F29.8, gitea-#182)
//
// BDD specification — xUnit. R4 un-pinned the runnable facts (script/compose file-content
// assertions) after the house `--check` spike (docs/MEMORY.md, "R4 spike verdict"). The gap
// lives in engine/genwave.liq — append() attaches a blank(duration=gap) track directly onto
// the safe branch after every safe track — driven by GW_SAFE_GAP_SECONDS (default 7.0, 0
// disables) plumbed through compose.yaml. Recorded-drain and cutback-latency proofs stay
// live/operator-gated for R13; the runnable facts here pin the script/compose artifacts the
// way StoryF4/Story068/Story035/Story057's file-content facts do.

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
        // Operator-gated at R13 (E10→Q12 pattern): recorded drain output shows ≈gap seconds
        // of silence between consecutive safe tracks; --check spike verdict recorded.

        [Fact(Skip = "Pending R13 — live drain recording (operator/scratch stack); see docs/PLAN.md")]
        public void RecordedDrainShowsTheConfiguredGapBetweenSafeTracks()
        {
            Assert.Fail("pending R13");
        }

        [Fact(Skip = "Pending R13 — live drain recording (operator/scratch stack); see docs/PLAN.md")]
        public void CutbackToMainHappensWithinOneSourceSwitchCycleMidGap()
        {
            Assert.Fail("pending R13");
        }
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
