// STORY-062 — Engine safe-source resilience: prefetch verdict + retry backoff (WIRE)
//
// BDD specification — xUnit. engine/genwave.liq's safe_lib request.dynamic gains
// retry_delay >= 5. (F22.3) and applies STORY-061's prefetch verdict (F22.1/F22.2).
// Script-shape assertions are runnable and red until M2 lands; the live proofs
// (api restart without RID churn, drain during the api-down window) are
// operator-gated Integration facts per the E10/W7/L8 pattern.
//
// STORY-061 (the spike) has no spec file: its deliverable is a verdict + evidence
// written to docs/MEMORY.md, not shipped code (PLAN M1).

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineSafeSourceResilience
{
    static string ScriptPath =>
        Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "engine", "genwave.liq"));

    // ---------------------------------------------------------------------
    // HAPPY PATH — script shape (runnable, red until M2)
    // ---------------------------------------------------------------------

    public sealed class ScenarioSafeSourceCarriesRetryBackoff
    {
        readonly string script = File.ReadAllText(ScriptPath);

        [Fact]
        public void SafeLibRequestDynamicSetsRetryDelayOfAtLeastFiveSeconds()
        {
            // F22.3 — the safe_lib request.dynamic invocation carries retry_delay=<n>. with n >= 5.
            var safeLine = script
                .Split('\n')
                .Single(l => l.Contains("request.dynamic", StringComparison.Ordinal)
                          && l.Contains("safe_lib", StringComparison.Ordinal));
            Assert.Matches(@"retry_delay\s*=\s*([5-9]|[1-9]\d+)\.", safeLine);
        }
    }

    // ---------------------------------------------------------------------
    // WIRE — live proofs (operator-gated; Skip-pinned per the W7/L8 pattern)
    // ---------------------------------------------------------------------

    [Trait("Category", "Integration")]
    public sealed class ScenarioApiRestartUnderLiveEngine
    {
        const string Skip = "Live stack + operator: needs the full broadcast stack (M2 wire acceptance).";

        [Fact(Skip = Skip)]
        public void EngineLogShowsNoSafeLibRequestLeakWarningAcrossAnApiRestart() { }

        [Fact(Skip = Skip)]
        public void TheStreamIsUninterruptedAcrossTheApiRestart() { }
    }

    [Trait("Category", "Integration")]
    public sealed class ScenarioPrefetchVerdictApplied
    {
        const string Skip = "Live stack + operator: STORY-061 verdict drives this (M1 -> M2).";

        [Fact(Skip = Skip)]
        public void AScopeEditIsReflectedWithinTheVerdictsPrefetchDepthOnANewDrain() { }

        [Fact(Skip = Skip)]
        public void ADrainDuringTheApiDownWindowDegradesPerF44WithoutEngineCrash() { }
    }
}
