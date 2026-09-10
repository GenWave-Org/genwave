// STORY-420 — Stock refill counts only unpaused sponsors (SPEC F173.2/F173.4 · PLAN T440)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture (the Story418_AiringExclusionsSql.cs
// precedent, this same folder). AdBriefRepository.SampleEnabledAsync and
// AdSpotRepository.CountStockGeneratedAsync each grew one JOIN + "and not s.paused" predicate for T440
// (SPEC F173.2, F173.4) — a fake store can only ever restate that predicate in C#, never prove the SQL
// itself runs correctly against real Postgres, so every fact here proves the two queries directly.

using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureStockRefillCountsOnlyUnpausedSponsors
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static NewAdSpot DraftSpot(long sponsorId) =>
        new(sponsorId, "Draft spot", Brief: "A cozy hardware shop", Script: null, AdSource.Llm,
            PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null,
            InitialState: AdState.Draft, FailReason: null);

    // ---------------------------------------------------------------------
    // SampleEnabledAsync — AC3's own store-level half (F173.2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioSampleEnabledAsyncSkipsAPausedSponsorsBriefs(DatabaseFixture db)
    {
        [Fact]
        public async Task ReturnsNullWhenOnlyAPausedSponsorHasEnabledBriefs()
        {
            // Given a paused sponsor with one enabled brief, and no other briefs at all...
            await db.ResetAdsAndShowsAsync();
            var sponsors = Harness.SponsorRepo(db);
            var briefs = Harness.AdBriefRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await sponsors.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);
            await briefs.CreateOwnerAsync(
                sponsorId, premise: "A cozy hardware shop", tone: "warm", structure: null, enabled: true,
                CancellationToken.None);

            // When a brief is sampled...
            var sampled = await briefs.SampleEnabledAsync(CancellationToken.None);

            // Then nothing comes back — the only enabled brief's own sponsor is paused.
            Assert.Null(sampled);
        }

        [Fact]
        public async Task NeverSamplesThePausedSponsorsBriefEvenWhenAnUnpausedOneExists()
        {
            // Given a paused sponsor with an enabled brief, and a SECOND, unpaused sponsor with their
            // own enabled brief...
            await db.ResetAdsAndShowsAsync();
            var sponsors = Harness.SponsorRepo(db);
            var briefs = Harness.AdBriefRepo(db);
            var pausedSponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await sponsors.SetPausedAsync(pausedSponsorId, paused: true, CancellationToken.None);
            await briefs.CreateOwnerAsync(
                pausedSponsorId, premise: "A cozy hardware shop", tone: "warm", structure: null, enabled: true,
                CancellationToken.None);
            var activeSponsorId = await Harness.SeedSponsorAsync(db, "Marsh & Co");
            var activeBrief = await briefs.CreateOwnerAsync(
                activeSponsorId, premise: "Fresh bread daily", tone: "cheerful", structure: null, enabled: true,
                CancellationToken.None);
            Assert.NotNull(activeBrief);

            // When a brief is sampled, repeatedly (SampleEnabledAsync's own "order by random()" —
            // one draw could land on the single legal candidate by chance even with a broken filter)...
            for (var i = 0; i < 20; i++)
            {
                var sampled = await briefs.SampleEnabledAsync(CancellationToken.None);

                // Then it is always the unpaused sponsor's own brief, never the paused sponsor's.
                Assert.NotNull(sampled);
                Assert.Equal(activeBrief.Id, sampled.Id);
            }
        }
    }

    // ---------------------------------------------------------------------
    // CountStockGeneratedAsync — AC1's own store-level half (F173.4)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioCountStockGeneratedAsyncExcludesAPausedSponsorsSpots(DatabaseFixture db)
    {
        [Fact]
        public async Task SixReadySpotsUnderAPausedSponsorCountAsZero()
        {
            // Given a paused sponsor with six ready, llm-sourced spots...
            await db.ResetAdsAndShowsAsync();
            var sponsors = Harness.SponsorRepo(db);
            var spots = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await sponsors.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);
            for (var mediaId = 100; mediaId < 106; mediaId++)
                await Harness.SeedReadySpotAsync(spots, sponsorId, mediaId);

            // When the stock count is asked for...
            var count = await spots.CountStockGeneratedAsync(CancellationToken.None);

            // Then it is zero — none of the paused sponsor's own spots count toward TargetCount.
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task ResumingTheSponsorRestoresTheCountToSix()
        {
            // Given the SAME six ready spots under a paused sponsor, confirmed uncounted...
            await db.ResetAdsAndShowsAsync();
            var sponsors = Harness.SponsorRepo(db);
            var spots = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await sponsors.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);
            for (var mediaId = 100; mediaId < 106; mediaId++)
                await Harness.SeedReadySpotAsync(spots, sponsorId, mediaId);
            Assert.Equal(0, await spots.CountStockGeneratedAsync(CancellationToken.None));

            // When the sponsor is unpaused (no spot itself ever transitions)...
            await sponsors.SetPausedAsync(sponsorId, paused: false, CancellationToken.None);

            // Then the count is restored to six.
            Assert.Equal(6, await spots.CountStockGeneratedAsync(CancellationToken.None));
        }

        [Fact]
        public async Task APausedSponsorsDraftSpotIsAlsoNotCounted()
        {
            // Given a paused sponsor with one llm spot still in draft (never claimed/rendered) —
            // CountStockGeneratedAsync's own draft-through-ready span (gh-#689) still applies, so
            // pause must narrow every one of those states, not only ready...
            await db.ResetAdsAndShowsAsync();
            var sponsors = Harness.SponsorRepo(db);
            var spots = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await sponsors.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);
            await spots.CreateAsync(DraftSpot(sponsorId), CancellationToken.None);

            // When the stock count is asked for...
            var count = await spots.CountStockGeneratedAsync(CancellationToken.None);

            // Then it is zero — the draft counts for nobody while its sponsor is paused.
            Assert.Equal(0, count);
        }
    }
}
