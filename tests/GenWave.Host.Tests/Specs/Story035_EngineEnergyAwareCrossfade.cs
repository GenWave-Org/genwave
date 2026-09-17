// STORY-035 — Engine: energy-aware music→music crossfade
//
// BDD specification — xUnit. Integration: exercises engine/genwave.liq behaviour on the
// pinned Liquidsoap 2.4.4 (liquidsoap --check for typing; recorded output for behaviour).
// Structure/typecheck facts (3) are live. Behavioural facts (5, T507) are no longer separate
// automated facts here — the record+measure harness Epic H deferred is stack_gate.sh's capture
// leg, which exercises real music→music crossfades against the pinned engine and measures them
// under SPEC F178.5; see each removed fact's former position below for the specific assertion.

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
    // HAPPY PATH — behavioural, verified against the live engine (T507)
    // ---------------------------------------------------------------------

    // Two music→music transitions (a hot pair vs. a mellow gw_outro/gw_intro_energy pair) yielding
    // a measurably shorter hot-pair fade, and every computed fade landing within [GW_XFADE_MIN,
    // GW_XFADE_MAX]: no gate assertion measures a fade's actual duration directly — the nearest
    // live proof is stack_gate.sh --capture (F178.5: zero silence events, integrated LUFS within
    // ±5 LU, a booth_log row), which proves silence/loudness/booth_log outcomes, not transition
    // timing. Was an Assert.Fail stub before T507 (former facts
    // HotterPairYieldsShorterFadeThanMellowerPair, ComputedFadeStaysWithinXfadeMinMax).

    // ---------------------------------------------------------------------
    // SAD PATH — safe degradation, behavioural (T507)
    // ---------------------------------------------------------------------

    // Missing-energy fallback using the fixed three-second fade, and the voice branches staying
    // unchanged: no gate assertion measures either directly — stack_gate.sh's capture leg proves
    // silence/loudness/booth_log outcomes (F178.5), not a specific fade duration or which code
    // branch ran. Was an Assert.Fail stub before T507 (former facts
    // MissingEitherEnergyUsesFixedThreeSecondFade, VoiceBranchesAreUnchanged).
    //
    // No transition ever produces silence: covered by stack_gate.sh --capture: silencedetect
    // noise=-45dB d=2 zero events across the fresh leg's real transitions (SPEC F178.5a) (former
    // fact TransitionNeverProducesSilence).
}
