// STORY-483 — One version, read once, shown one way (gh-#9 + gh-#868 · SPEC F211.2 · PLAN T589)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.
// Seam: AdSpotWorker through AdSpotWorkerHarness, IAppVersion replacing AdAppVersion.

namespace GenWave.Ads.Tests.Specs;

public static class FeatureRerenderMarkerSpelling
{
    const string Pending = "pending: T589 — the ad worker injects IAppVersion (STORY-483)";

    public sealed class ScenarioAStaleSpotIsReRendered
    {
        // Given: IAppVersion from "5.13.2+abc1234"; one stale spot; the worker's stale pass swaps its take

        /// <summary>AC7 — the auto re-render marker reads "v5.13.2" (unchanged spelling)</summary>
        [Fact(Skip = Pending)]
        public void MarkerReadsV5132() => Assert.Fail(Pending);
    }
}
