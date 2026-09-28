// STORY-487 — The engine reads the station's settings on a fresh up (gh-#879 · SPEC F213.1–F213.3 · PLAN T598, T599)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Entry points: `docker compose config` renders of compose.yaml (+ compose.piper-only.yaml), and
// engine/entrypoint.sh run through ScriptProcess.RunWithEmptyEnvironment with PATH stubs for curl, sleep,
// date (a fake clock) and liquidsoap (records argv + env) — never a real 30 s wait.

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineSettingsOnFreshUp
{
    const string PendingCompose = "pending: T598 — api drops depends_on engine; engine start_period (STORY-487)";
    const string PendingWait = "pending: T599 — entrypoint 30 s wall-clock budget (STORY-487)";

    public sealed class ScenarioComposeRender
    {
        // Given: `docker compose config` of compose.yaml, and of compose.yaml + compose.piper-only.yaml

        /// <summary>AC1 — the api's depends_on has no engine (compose.yaml)</summary>
        [Fact(Skip = PendingCompose)]
        public void ApiDoesNotWaitOnTheEngine() => Assert.Fail(PendingCompose);

        /// <summary>AC1 — the api's depends_on has no engine (piper-only overlay)</summary>
        [Fact(Skip = PendingCompose)]
        public void PiperOnlyApiDoesNotWaitOnTheEngine() => Assert.Fail(PendingCompose);

        /// <summary>AC2 — the engine still waits on icecast service_healthy</summary>
        [Fact(Skip = PendingCompose)]
        public void EngineWaitsOnIcecast() => Assert.Fail(PendingCompose);

        /// <summary>AC3 — the engine healthcheck start_period is at least 45 s</summary>
        [Fact(Skip = PendingCompose)]
        public void HealthcheckCoversTheWait() => Assert.Fail(PendingCompose);
    }

    public sealed class ScenarioASlowApi
    {
        // Given: stub curl fails 4 times, answers GW_XFADE_MIN=3 GW_XFADE_MAX=9 GW_SAFE_GAP_SECONDS=4 on the 5th;
        //        compose fallbacks 2 / 8 / 7.0 in the env; fake clock advances 3 s per try

        /// <summary>AC4 — liquidsoap sees GW_XFADE_MIN=3 (fetched, not the fallback)</summary>
        [Fact(Skip = PendingWait)]
        public void LiquidsoapGetsTheFetchedValues() => Assert.Fail(PendingWait);

        /// <summary>AC4 — no fallback WARN line</summary>
        [Fact(Skip = PendingWait)]
        public void NoFallbackLine() => Assert.Fail(PendingWait);
    }

    public sealed class ScenarioAnImmediateAnswer
    {
        // Given: stub curl answers on the 1st try

        /// <summary>AC5 — exactly one fetch</summary>
        [Fact(Skip = PendingWait)]
        public void OneFetch() => Assert.Fail(PendingWait);

        /// <summary>AC5 — sleep never called</summary>
        [Fact(Skip = PendingWait)]
        public void NeverSleeps() => Assert.Fail(PendingWait);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheApiNeverAnswers
    {
        // Given: stub curl always fails; fake clock advances 3 s per try (2 s timeout + 1 s sleep);
        //        compose fallbacks 2 / 8 / 7.0 in the env

        /// <summary>AC6 — the last try starts before 30 s have elapsed on the fake clock</summary>
        [Fact(Skip = PendingWait)]
        public void TriesUntilTheBudget() => Assert.Fail(PendingWait);

        /// <summary>AC6 — no try starts once 30 s have elapsed</summary>
        [Fact(Skip = PendingWait)]
        public void StopsAtTheBudget() => Assert.Fail(PendingWait);

        /// <summary>AC7 — today's "api unreachable … using fallback env" line prints once</summary>
        [Fact(Skip = PendingWait)]
        public void FallbackLinePrints() => Assert.Fail(PendingWait);

        /// <summary>AC7 — liquidsoap sees the compose fallback GW_XFADE_MIN=2</summary>
        [Fact(Skip = PendingWait)]
        public void LiquidsoapGetsTheFallbacks() => Assert.Fail(PendingWait);

        /// <summary>AC8 — liquidsoap is exec'd (entrypoint exits 0 via the stub)</summary>
        [Fact(Skip = PendingWait)]
        public void TheEngineAlwaysBoots() => Assert.Fail(PendingWait);
    }
}
