// STORY-057 — Engine request.dynamic pulls from the endpoint (WIRE)
//
// BDD specification — xUnit. engine/genwave.liq: replace `safe = single(SAFE_PATH)` with a
// request.dynamic that calls GET /internal/safe-track via process.read.lines "curl -s ...".
// Delete every SAFE_PATH reference. `main = fallback([q, safe])` shape and outer mksafe
// remain unchanged. Validate with `liquidsoap --check` on savonet/liquidsoap:v2.4.4.
// Mirrors the Story035 (E7) engine-graph spec pattern.

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineRequestDynamicPullsSafeTrack
{
    const string OperatorGated = "Operator-gated — requires savonet/liquidsoap:v2.4.4 container for liquidsoap --check and full stack for drain observation; see docs/PLAN.md Epic K";

    // Path to the engine script, resolved relative to the solution root at test runtime.
    private static string ScriptPath =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "engine", "genwave.liq"));

    // ---------------------------------------------------------------------
    // HAPPY PATH — script shape
    // ---------------------------------------------------------------------

    public sealed class ScenarioSafeIsARequestDynamicSource
    {
        [Fact]
        public void GenwaveLiqDefinesSafeAsARequestDynamic()
        {
            // AC1 — engine/genwave.liq contains `safe = request.dynamic(id="safe_lib", ...)`
            //       whose body invokes an HTTP call to /internal/safe-track (via
            //       process.read.lines "curl -s ..." or equivalent).
            var script = File.ReadAllText(ScriptPath);
            Assert.Contains("safe = request.dynamic(", script);
            Assert.Contains("http://api:8080/internal/safe-track", script);
        }

        [Fact]
        public void GenwaveLiqNoLongerReferencesSafePath()
        {
            // AC2 — no `single(...)` and no SAFE_PATH reference remains in engine/genwave.liq
            //       (grep-based assertion).
            var script = File.ReadAllText(ScriptPath);
            Assert.DoesNotContain("single(", script);
            Assert.DoesNotContain("SAFE_PATH", script);
            Assert.DoesNotContain("back_soon_loop", script);
        }

        [Fact]
        public void MainFallbackShapeIsUnchanged()
        {
            // AC3 — fallback(track_sensitive=false, [main, safe]) remains (SPEC F4.3),
            //       and the outer mksafe wrap is still the leaf backstop.
            var script = File.ReadAllText(ScriptPath);
            Assert.Contains("fallback(track_sensitive=false, [main, safe])", script);
            Assert.Contains("mksafe(", script);
        }

        [Fact]
        public void SmokeTestReferencesToSafePathAreRetired()
        {
            // Bonus — tools/smoke_test.sh must not reference SAFE_PATH or back_soon_loop;
            //         the smoke test does not need a safe source to run.
            var smokeTestPath = Path.GetFullPath(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..", "..", "..", "..", "..",
                    "tools", "smoke_test.sh"));
            var smokeTest = File.ReadAllText(smokeTestPath);
            Assert.DoesNotContain("SAFE_PATH", smokeTest);
            Assert.DoesNotContain("back_soon_loop", smokeTest);
        }
    }

    public sealed class ScenarioLiquidsoapCheckAcceptsTheUpdatedScript
    {
        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void LiquidsoapCheckExitsZeroOnThePinnedImage()
        {
            // AC4 — `liquidsoap --check engine/genwave.liq` inside the pinned
            //       savonet/liquidsoap:v2.4.4 image exits 0 (no type errors, no missing operators).
        }
    }

    public sealed class ScenarioRealDrainAirsTheSafeLibrary
    {
        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void DrainAirsADistinctiveSafeLibraryTrack()
        {
            // AC5 — WIRE — with SafeScope=[N] containing a distinctive track and main drained,
            //       that track airs; verified via output.icecast.metadata track_id round-trip.
            //       Not unit-tests-pass — this is the production binary producing the side effect.
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — empty scope falls through to mksafe
    // ---------------------------------------------------------------------

    public sealed class ScenarioEmptyResponseFallsThroughToMksafe
    {
        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void EmptySafeResponseTriggersMksafeWithoutCrashLoop()
        {
            // AC6 — with the safe endpoint returning 204 (SafeScope=[]), the engine's
            //       request.dynamic sees an empty URI, request.create("") fails silently,
            //       and mksafe engages (F4.4 degraded mode). No crash, no dead-air loop.
        }
    }
}
