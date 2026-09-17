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
    // WIRE — live proofs (T507: verified by the stack gate, not a separate live+operator fact)
    // ---------------------------------------------------------------------

    // No safe_lib request-leak warning across an api restart, and the stream staying uninterrupted
    // across it: no gate assertion reads the engine log for that specific warning or measures
    // stream continuity directly — the nearest live proof is stack_gate.sh --chaos api-down's
    // run_api_down_scenario, which stops/starts api against the live engine and only checks that a
    // non-safe track_id reappears within the SPEC F178.8a recovery budget, plus the capture leg's
    // total-silence bound over the same outage window. Was a Skip-pinned gate: fact before T507
    // (former facts EngineLogShowsNoSafeLibRequestLeakWarningAcrossAnApiRestart,
    // TheStreamIsUninterruptedAcrossTheApiRestart).

    // A safe-scope edit reaching the verdict's prefetch depth on a new drain: no gate assertion
    // measures prefetch depth at all — run_api_down_scenario stops/starts api and waits on a
    // non-safe track_id, nothing about a scope edit's reach into the verdict. A drain during the
    // api-down window degrading per F44 without an engine crash IS covered: that same
    // run_api_down_scenario drains the safe path for the outage's full duration and the leg fails
    // if the engine container doesn't come back / a non-safe track_id never reappears within the
    // SPEC F178.8a recovery budget. Was a Skip-pinned gate: fact before T507 (former facts
    // AScopeEditIsReflectedWithinTheVerdictsPrefetchDepthOnANewDrain,
    // ADrainDuringTheApiDownWindowDegradesPerF44WithoutEngineCrash).
}
