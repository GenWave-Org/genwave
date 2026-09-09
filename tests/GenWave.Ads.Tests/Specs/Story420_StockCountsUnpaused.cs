// STORY-420 — Stock refill counts only unpaused sponsors (SPEC F173.4/F173.5 · PLAN T440)

using GenWave.Ads.Tests.Fakes;
using GenWave.Ads.Tests.Support;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;

namespace GenWave.Ads.Tests.Specs;

public static class FeatureStockRefillCountsOnlyUnpausedSponsors
{
    static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioPausedSponsorsStockIsNotCounted
    {
        [Fact]
        public async Task TheWorkerGeneratesFromOtherSponsorsBriefs()
        {
            // Given TargetCount=1, one ready llm spot under a PAUSED sponsor (99) — the fake
            // FakeAdSpotLifecycleStore.CountStockGeneratedAsync's own paused-sponsor exclusion (wired to
            // FakeSponsorStore.IsPaused by AdSpotWorkerHarness.Build, PLAN T440 ruling) mirrors
            // AdSpotRepository.CountStockGeneratedAsync's real "and not s.paused" (SPEC F173.4), so this
            // spot counts as ZERO toward TargetCount, not one — and an enabled brief for a DIFFERENT,
            // unpaused sponsor...
            var harness = AdSpotWorkerHarness.Build(
                Now, AdSpotWorkerHarness.Settings(targetCount: 1),
                llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(AdSpotWorkerHarness.WellFormedReply));
            harness.Store.AddSpot(1, AdState.Ready, AdSource.Llm, mediaId: 500, sponsorId: 99, sponsorName: "Paused Co");
            harness.Sponsors.Pause(99);
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the worker generates — from the OTHER sponsor's brief, never the paused one's own
            // (which has none sampled here anyway, since only the unpaused sponsor's brief is seeded).
            Assert.Equal(1, harness.Store.CreateCallCount);
            var created = harness.Store.CreateRequests.Single();
            Assert.Equal(harness.Briefs.SponsorIdsByBrand["Cravin's Diner"], created.SponsorId);
        }

        [Fact]
        public async Task TheBelowTargetLineIsLogged()
        {
            // Given the SAME below-target setup as the fact above, but with the tick's own logger
            // captured...
            var logger = new CapturingLogger<AdSpotWorker>();
            var harness = AdSpotWorkerHarness.Build(
                Now, AdSpotWorkerHarness.Settings(targetCount: 1),
                llmHandler: AdSpotWorkerHarness.ServeSameReplyEveryTime(AdSpotWorkerHarness.WellFormedReply),
                workerLogger: logger);
            harness.Store.AddSpot(1, AdState.Ready, AdSource.Llm, mediaId: 500, sponsorId: 99, sponsorName: "Paused Co");
            harness.Sponsors.Pause(99);
            harness.Briefs.AddEnabled("Cravin's Diner", premise: "A retro diner with a twist", tone: "warm and playful");

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then STORY-420 AC1's own literal fires at Information — RefillIfNeededAsync's new log
            // line (PLAN T440) — proving the worker actually SAW the below-target state (the paused
            // sponsor's spot excluded from the count) rather than generating for some unrelated reason.
            Assert.Contains(
                logger.Entries, e => e.Level == LogLevel.Information &&
                    e.Message.Contains("Ad stock below target: generating one", StringComparison.Ordinal));
        }
    }

    public sealed class ScenarioRepairAndGuardianAreUnchanged
    {
        [Fact]
        public async Task RepairReEnablesAPausedSponsorsDisabledMediaRow()
        {
            // Given a ready spot whose sponsor IS paused, its own ready transition moments ago (well
            // inside the guardian grace) and its media row still ineligible — the SAME
            // MarkReadyAsync-committed/SetEligibleAsync-never-ran race Story389's own repair-sweep fact
            // exercises for an unpaused sponsor, here under a paused one instead (SPEC F173.5, PLAN
            // T440 AC2: the repair sweep reads no sponsor pause state at all) — a brief seeds the
            // sponsor so FakeSponsorStore.GetAsync (its name-lookup closure reads
            // FakeAdBriefStore.SponsorIdsByBrand) actually resolves it to a real, paused Sponsor rather
            // than null...
            var harness = AdSpotWorkerHarness.Build(Now);
            harness.Briefs.AddEnabled("Sleepy Sponsor Co", premise: "A sponsor who is taking a break", tone: "quiet");
            var sponsorId = harness.Briefs.SponsorIdsByBrand["Sleepy Sponsor Co"];
            harness.Store.AddSpot(
                1, AdState.Ready, mediaId: 900, stateChangedAt: Now.UtcDateTime.AddMinutes(-1), sponsorId: sponsorId);
            harness.AdminLookup.Add(900, AdSpotWorkerHarness.MakeMediaRow(900, eligible: false), harness.AdsLibraryId);
            harness.Sponsors.Pause(sponsorId);

            // When the stock pass runs...
            await harness.Worker.TickOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

            // Then the repair sweep flips it eligible regardless of the sponsor's paused state — pause
            // narrows the refill/stock-count reads (AC1), never the repair sweep or the guardian (AC2).
            Assert.Contains((900L, true), harness.CatalogWriter.SetEligibleHistory);
        }
    }
}
