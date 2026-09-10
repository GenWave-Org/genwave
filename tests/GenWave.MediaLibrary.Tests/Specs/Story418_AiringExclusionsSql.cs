// STORY-418 — Pausing a sponsor silences their spots on the very next pick (AC1 · SPEC F173.1/F173.2 ·
// PLAN T439)
//
// BDD specification — xUnit, REAL Postgres via DatabaseFixture (the Story389_AdSpotLifecycleStore.cs
// precedent). AdSpotRepository.ListAiringExclusionsAsync (SPEC F171, F174; PLAN T432) is one SQL query
// — a JOIN and an OR — and a fake store can only ever restate that query in C#, never prove the query
// itself runs correctly against real Postgres; every fact here proves the SQL, not LibraryAdSpotSource's
// own call shape (that lives in GenWave.Ads.Tests/Specs/Story419_AntiRepeatBySponsor.cs against a fake
// store instead — the Story387_ImagingNeverAirsAsMusic.cs / Story388_LibraryAdSpotSource.cs split).

using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeaturePausingASponsorSilencesTheirSpotsOnTheVeryNextPick
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the paused half (AC1)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheExcludeListAddsPausedSponsorsSpots(DatabaseFixture db)
    {
        [Fact]
        public async Task ListAiringExclusionsContainsThePausedSponsorsReadyMedia()
        {
            // Given a sponsor paused, with one ready spot under them...
            await db.ResetAdsAndShowsAsync();
            var sponsors = Harness.SponsorRepo(db);
            var spots = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await sponsors.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);
            await Harness.SeedReadySpotAsync(spots, sponsorId, mediaId: 100);

            // When the exclusion list is asked for with no anti-repeat pressure at all...
            var excluded = await spots.ListAiringExclusionsAsync([], window: 0, CancellationToken.None);

            // Then their ready media id is in it.
            Assert.Contains(100, excluded);
        }

        [Fact]
        public async Task AnUnpausedSponsorsReadyMediaIsAbsent()
        {
            // Given one sponsor paused with a ready spot, and a SECOND, unpaused sponsor with their
            // own ready spot...
            await db.ResetAdsAndShowsAsync();
            var sponsors = Harness.SponsorRepo(db);
            var spots = Harness.AdSpotRepo(db);
            var pausedSponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await sponsors.SetPausedAsync(pausedSponsorId, paused: true, CancellationToken.None);
            await Harness.SeedReadySpotAsync(spots, pausedSponsorId, mediaId: 100);
            var activeSponsorId = await Harness.SeedSponsorAsync(db, "Marsh & Co");
            await Harness.SeedReadySpotAsync(spots, activeSponsorId, mediaId: 200);

            // When the exclusion list is asked for...
            var excluded = await spots.ListAiringExclusionsAsync([], window: 0, CancellationToken.None);

            // Then only the paused sponsor's media is in it — the unpaused sponsor's own ready media
            // never appears.
            Assert.Equal([100], excluded);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the recency half (STORY-419's own rule, proven at the SQL layer here since it's
    // the SAME query as the paused half)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheExcludeListAlsoAddsRecentSponsorsSpots(DatabaseFixture db)
    {
        [Fact]
        public async Task AWindowOfOneExcludesTheRecentSponsorsOtherReadyMediaToo()
        {
            // Given sponsor A with TWO ready spots, and sponsor B with one — neither paused...
            await db.ResetAdsAndShowsAsync();
            var spots = Harness.AdSpotRepo(db);
            var sponsorA = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await Harness.SeedReadySpotAsync(spots, sponsorA, mediaId: 100);
            await Harness.SeedReadySpotAsync(spots, sponsorA, mediaId: 101);
            var sponsorB = await Harness.SeedSponsorAsync(db, "Marsh & Co");
            await Harness.SeedReadySpotAsync(spots, sponsorB, mediaId: 200);

            // When the exclusion list is asked for with A's own media 100 as the sole recent entry,
            // window=1...
            var excluded = await spots.ListAiringExclusionsAsync([100], window: 1, CancellationToken.None);

            // Then B's media is absent, and A's OTHER ready media (101) is present alongside 100 — the
            // exclusion is by SPONSOR, not by the exact recent media id.
            Assert.Equal([100, 101], excluded.OrderBy(id => id));
        }

        [Fact]
        public async Task AWindowOfTwoExcludesBothRecentSponsorsButNotAThird()
        {
            // Given three sponsors, one ready spot each, none paused...
            await db.ResetAdsAndShowsAsync();
            var spots = Harness.AdSpotRepo(db);
            var sponsorA = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await Harness.SeedReadySpotAsync(spots, sponsorA, mediaId: 100);
            var sponsorB = await Harness.SeedSponsorAsync(db, "Marsh & Co");
            await Harness.SeedReadySpotAsync(spots, sponsorB, mediaId: 200);
            var sponsorC = await Harness.SeedSponsorAsync(db, "Tallow & Sons");
            await Harness.SeedReadySpotAsync(spots, sponsorC, mediaId: 300);

            // When the exclusion list is asked for with A's and B's media as the two recent entries,
            // window=2...
            var excluded = await spots.ListAiringExclusionsAsync([100, 200], window: 2, CancellationToken.None);

            // Then C's media is absent; A's and B's are both present.
            Assert.Equal([100, 200], excluded.OrderBy(id => id));
        }

        [Fact]
        public async Task AWindowNarrowerThanTheRecentListOnlyExcludesTheSponsorsWithinIt()
        {
            // Given the same three-sponsor setup as the window=2 fact above...
            await db.ResetAdsAndShowsAsync();
            var spots = Harness.AdSpotRepo(db);
            var sponsorA = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await Harness.SeedReadySpotAsync(spots, sponsorA, mediaId: 100);
            var sponsorB = await Harness.SeedSponsorAsync(db, "Marsh & Co");
            await Harness.SeedReadySpotAsync(spots, sponsorB, mediaId: 200);
            var sponsorC = await Harness.SeedSponsorAsync(db, "Tallow & Sons");
            await Harness.SeedReadySpotAsync(spots, sponsorC, mediaId: 300);

            // When the exclusion list is asked for with BOTH A's and B's media in recentMediaIds, but
            // window=1 — only the FIRST entry falls inside the window...
            var excluded = await spots.ListAiringExclusionsAsync([100, 200], window: 1, CancellationToken.None);

            // Then only A's sponsor is excluded — B's media (second in the list, outside window=1) and
            // C's media (never recent) are both absent.
            Assert.Equal([100], excluded);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — only Ready spots are ever candidates
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOnlyReadySpotsAreEverConsidered(DatabaseFixture db)
    {
        [Fact]
        public async Task ADraftSpotsMediaNeverAppearsInTheExclusionList()
        {
            // Given a paused sponsor with one ready spot AND a separate, still-Draft spot under the
            // same sponsor (a Draft row never carries a media_id — nothing this store exposes can give
            // it one)...
            await db.ResetAdsAndShowsAsync();
            var sponsors = Harness.SponsorRepo(db);
            var spots = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await sponsors.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);
            await Harness.SeedReadySpotAsync(spots, sponsorId, mediaId: 100);
            await spots.CreateAsync(
                new NewAdSpot(sponsorId, "Draft spot", Brief: "A cozy hardware shop", Script: null,
                    AdSource.Llm, PackSlug: null, SpotSeconds: 30, VoicePlan: null, BedMediaId: null,
                    InitialState: AdState.Draft, FailReason: null),
                CancellationToken.None);

            // When the exclusion list is asked for...
            var excluded = await spots.ListAiringExclusionsAsync([], window: 0, CancellationToken.None);

            // Then it holds exactly the one Ready spot's media — the Draft row contributes nothing.
            Assert.Equal([100], excluded);
        }
    }

    // ---------------------------------------------------------------------
    // AC4 (store level) — unpausing restores eligibility with no state transition
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUnpausingRestoresEligibilityAtTheStoreLevel(DatabaseFixture db)
    {
        [Fact]
        public async Task UnpausingTheSponsorRemovesTheirMediaFromTheExclusionList()
        {
            // Given a sponsor paused with one ready spot, confirmed excluded...
            await db.ResetAdsAndShowsAsync();
            var sponsors = Harness.SponsorRepo(db);
            var spots = Harness.AdSpotRepo(db);
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            await sponsors.SetPausedAsync(sponsorId, paused: true, CancellationToken.None);
            var spotId = await Harness.SeedReadySpotAsync(spots, sponsorId, mediaId: 100);
            Assert.Equal(
                [100], await spots.ListAiringExclusionsAsync([], window: 0, CancellationToken.None));

            // When the sponsor is unpaused (STORY-418 AC4's own store-level half — no re-render, no
            // spot transition)...
            await sponsors.SetPausedAsync(sponsorId, paused: false, CancellationToken.None);

            // Then their media leaves the exclusion list...
            var excluded = await spots.ListAiringExclusionsAsync([], window: 0, CancellationToken.None);
            Assert.DoesNotContain(100L, excluded);

            // ...and the spot itself never moved off Ready — SetPausedAsync touches station.sponsor
            // only, never station.ad_spot.
            var spot = await spots.GetByIdAsync(spotId, CancellationToken.None);
            Assert.NotNull(spot);
            Assert.Equal(AdState.Ready, spot.State);
        }
    }
}
