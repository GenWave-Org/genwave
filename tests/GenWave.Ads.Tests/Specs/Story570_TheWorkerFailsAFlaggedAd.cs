// STORY-570 — One flow for flagged talk, the worker half (SPEC F220.14 · PLAN T802)
//
// Entry point: AdSpotWorker.TickOnceAsync with the real AdScriptWriter in rules mode, over a flow that reports the ad still flagged.

namespace GenWave.Ads.Tests.Specs;

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;

public static class FeatureFlaggedAdsFailTheSpot
{
    const string Reason = "the re-ask was flagged again (Alpha rule, Bravo rule)";

    // AC5 — an ad still flagged is a failed spot whose reason names every flagged rule
    public sealed class ScenarioAnAdIsStillFlagged : IAsyncLifetime
    {
        AdSpotWorkerHarness.Harness harness = null!;

        public async Task InitializeAsync()
        {
            harness = AdSpotWorkerHarness.Build(
                new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero),
                new Dictionary<string, string?> { ["Station:Ads:TargetCount"] = "1" },
                rulesFlow: new StillFlaggedAdRulesFlow(Reason));
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public void AFailedSpotIsWritten() => Assert.Equal(AdState.Failed, Assert.Single(harness.Store.Spots).State);

        [Fact]
        public void ItsReasonNamesEveryFlaggedRule() => Assert.Equal(Reason, Assert.Single(harness.Store.Spots).FailReason);
    }
}
