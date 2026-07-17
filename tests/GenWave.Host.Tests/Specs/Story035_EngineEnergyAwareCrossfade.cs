// STORY-035 — Engine: energy-aware music→music crossfade
//
// BDD specification — xUnit. Integration: exercises engine/genwave.liq behaviour on the
// pinned Liquidsoap 2.4.4 (liquidsoap --check for typing; recorded output for behaviour).
// Structure/typecheck facts (3) are live. Behavioural facts (5) require a live recorded stream
// and are pinned to E9 — see docs/PLAN.md Epic H.

using System.Diagnostics;

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineEnergyAwareCrossfade
{
    // Path to the engine script, resolved relative to the solution root at test runtime.
    private static string ScriptPath =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "engine", "genwave.liq"));

    // ---------------------------------------------------------------------
    // STRUCTURE / TYPECHECK FACTS — provable without a live stream
    // ---------------------------------------------------------------------

    public sealed class ScenarioBufferWindowAndEnvBounds
    {
        [Fact]
        public void MainCrossUsesGwXfadeMaxAsTheBufferWindow()
        {
            // genwave.liq: main = cross(duration=xfade_max, gw_transition, q).
            // The cross() buffer window must reference xfade_max so it tracks GW_XFADE_MAX.
            var script = File.ReadAllText(ScriptPath);
            Assert.Contains("cross(duration=xfade_max", script);
        }

        [Fact]
        public void FadeBoundsAreReadFromEnvironmentWithDefaults()
        {
            // GW_XFADE_MIN / GW_XFADE_MAX come from environment with defaults 2. / 8.
            var script = File.ReadAllText(ScriptPath);
            Assert.Contains("environment.get(\"GW_XFADE_MIN\")", script);
            Assert.Contains("environment.get(\"GW_XFADE_MAX\")", script);
            // Defaults: float_of_string(default=2., ...) and float_of_string(default=8., ...)
            Assert.Contains("default=2.,", script);
            Assert.Contains("default=8.,", script);
        }

        [Fact, Trait("Category", "Integration")]
        public void ScriptTypechecksUnderLiquidsoapCheck()
        {
            // `liquidsoap --check engine/genwave.liq` exits 0 on the pinned 2.4.4 image.
            // Verifies all transition branches type-unify (music→music energy path + fallback
            // must return the same source type as music→voice and voice→* branches).
            var psi = new ProcessStartInfo("docker")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--rm");
            psi.ArgumentList.Add("-v");
            psi.ArgumentList.Add($"{ScriptPath}:/genwave.liq:ro");
            psi.ArgumentList.Add("savonet/liquidsoap:v2.4.4");
            psi.ArgumentList.Add("liquidsoap");
            psi.ArgumentList.Add("--check");
            psi.ArgumentList.Add("/genwave.liq");

            using var proc = Process.Start(psi);
            Assert.NotNull(proc);
            proc.WaitForExit(60_000);
            Assert.Equal(0, proc.ExitCode);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — behavioural, requires live recorded stream (pinned to E9)
    // ---------------------------------------------------------------------

    public sealed class ScenarioEnergyPairMapsToClampedMonotonicDuration
    {
        [Fact(Skip = "Deferred (operator-verified live, E9/E10) - automated record+measure harness is a follow-up; see docs/PLAN.md Epic H"), Trait("Category", "Integration")]
        public void HotterPairYieldsShorterFadeThanMellowerPair()
        {
            // Two music→music transitions: a hot pair (high gw_outro/gw_intro_energy) and a mellow pair.
            // Measured hot-pair fade < mellow-pair fade.
            Assert.Fail("pending E9");
        }

        [Fact(Skip = "Deferred (operator-verified live, E9/E10) - automated record+measure harness is a follow-up; see docs/PLAN.md Epic H"), Trait("Category", "Integration")]
        public void ComputedFadeStaysWithinXfadeMinMax()
        {
            // Every computed fade is within [GW_XFADE_MIN, GW_XFADE_MAX] (defaults 2.0 / 8.0).
            Assert.Fail("pending E9");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — safe degradation, behavioural (pinned to E9)
    // ---------------------------------------------------------------------

    public sealed class ScenarioMissingEnergyFallsBack
    {
        [Fact(Skip = "Deferred (operator-verified live, E9/E10) - automated record+measure harness is a follow-up; see docs/PLAN.md Epic H"), Trait("Category", "Integration")]
        public void MissingEitherEnergyUsesFixedThreeSecondFade()
        {
            // Either gw_*_energy absent/unparseable → cross.simple(fade_in=3., fade_out=3.).
            Assert.Fail("pending E9");
        }

        [Fact(Skip = "Deferred (operator-verified live, E9/E10) - automated record+measure harness is a follow-up; see docs/PLAN.md Epic H")]
        public void VoiceBranchesAreUnchanged()
        {
            // music→voice overlay-duck and voice→* butt-splice behave exactly as before.
            Assert.Fail("pending E9");
        }

        [Fact(Skip = "Deferred (operator-verified live, E9/E10) - automated record+measure harness is a follow-up; see docs/PLAN.md Epic H"), Trait("Category", "Integration")]
        public void TransitionNeverProducesSilence()
        {
            // The fallback/mksafe dead-air backstop is intact; no transition outcome yields silence.
            Assert.Fail("pending E9");
        }
    }
}
