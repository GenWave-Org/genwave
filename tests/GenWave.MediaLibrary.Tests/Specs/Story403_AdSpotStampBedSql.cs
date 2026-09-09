// STORY-403 — the ad bed pick's never-overwrite guard, structurally + against real Postgres (SPEC F168 · PLAN T416)
//
// The Story402_AdSpotStampVoicePlanSql precedent, this same directory, one column over: pure text
// assertion first (AdSpotRepository.StampBedSql is the ONE place the coalesce/state guard text
// lives — StampBedIfNullAsync references it directly, never a second inline copy), then a
// real-Postgres round-trip scenario proving the guard's actual BEHAVIOUR against the live schema.

using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureAdSpotStampBedSql
{
    public sealed class ScenarioTheGuardIsPartOfTheSingleUpdateStatement
    {
        // Given AdSpotRepository's own stamp guard text, When it is inspected directly.
        [Fact]
        public void ItNeverOverwritesAnExistingBed() =>
            Assert.Contains("coalesce(bed_media_id,", AdSpotRepository.StampBedSql, StringComparison.Ordinal);

        [Fact]
        public void ItOnlyEverStampsARowCurrentlyRendering() =>
            Assert.Contains("state = 'rendering'", AdSpotRepository.StampBedSql, StringComparison.Ordinal);
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioTheGuardHoldsAgainstRealPostgres(DatabaseFixture db)
    {
        const long FirstBedMediaId = 501;
        const long SecondBedMediaId = 777;

        static NewAdSpot Draft(long sponsorId, long? bedMediaId = null) =>
            new(
                sponsorId, "Draft spot", Brief: "A cozy hardware shop", Script: null, AdSource.Llm,
                PackSlug: null, SpotSeconds: 30, VoicePlan: null, bedMediaId, InitialState: AdState.Approved,
                FailReason: null);

        [Fact]
        public async Task ANullBedOnARenderingRowIsStamped()
        {
            // Given a spot claimed into Rendering with no bed yet...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            Assert.Null(claimed.BedMediaId);

            // When the bed pick is stamped...
            var stamped = await repo.StampBedIfNullAsync(claimed.Id, FirstBedMediaId, CancellationToken.None);

            // Then the row carries the new bed.
            Assert.NotNull(stamped);
            Assert.Equal(FirstBedMediaId, stamped!.BedMediaId);
        }

        [Fact]
        public async Task AnExistingBedOnARenderingRowIsNeverOverwritten()
        {
            // Given a spot claimed into Rendering that already carries a bed (an owner's own explicit
            // pick)...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            await repo.CreateAsync(Draft(sponsorId, FirstBedMediaId), CancellationToken.None);
            var claimed = (await repo.ClaimNextApprovedAsync(CancellationToken.None))!;
            Assert.Equal(FirstBedMediaId, claimed.BedMediaId);

            // When a stamp is attempted anyway...
            var stamped = await repo.StampBedIfNullAsync(claimed.Id, SecondBedMediaId, CancellationToken.None);

            // Then the original bed survives untouched — coalesce won, not the new argument.
            Assert.NotNull(stamped);
            Assert.Equal(FirstBedMediaId, stamped!.BedMediaId);
        }

        [Fact]
        public async Task ARowNotCurrentlyRenderingIsNeverStamped()
        {
            // Given an approved spot never claimed into Rendering...
            await db.ResetAdsAndShowsAsync();
            var sponsorId = await Harness.SeedSponsorAsync(db, "Bramble & Fitch");
            var repo = Harness.AdSpotRepo(db);
            var spot = await repo.CreateAsync(Draft(sponsorId), CancellationToken.None);

            // When a stamp is attempted against it directly...
            var stamped = await repo.StampBedIfNullAsync(spot.Id, FirstBedMediaId, CancellationToken.None);

            // Then nothing is returned — the guarded WHERE matched nothing, and nothing was written.
            Assert.Null(stamped);
        }
    }
}
