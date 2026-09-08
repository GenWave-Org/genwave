// STORY-414 — The live station migrates cleanly to sponsors (SPEC F172.1/F172.2 · PLAN T431)

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheLiveStationMigratesCleanlyToSponsors
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioMigrationRunsWithTheApiDown
    {
        [Fact]
        public void LaunchShRunsMigrateBeforeTheApiComesUp()
            => Assert.Fail("pending: T431 launch.sh staged order pinned by text — AC1");
    }

    public sealed class ScenarioOneSponsorPerDistinctPackSlugAndFoldedBrand
    {
        [Fact]
        public void TheMixedCaseOwnerTrioBecomesOneOwnerSponsor()
            => Assert.Fail("pending: T431 db/46 backfill fold (DatabaseFixture) — AC2");

        [Fact]
        public void ThePackPairBecomesOnePackSponsor()
            => Assert.Fail("pending: T431 db/46 backfill pack namespace — AC2");

        [Fact]
        public void ExactlyTwoSponsorRowsExist()
            => Assert.Fail("pending: T431 db/46 backfill count — AC2");
    }

    public sealed class ScenarioEarliestCreatedSpellingWins
    {
        [Fact]
        public void TheSponsorNameIsTheFirstCreatedSpelling()
            => Assert.Fail("pending: T431 db/46 backfill name = min(created_at) spelling — AC3");
    }

    public sealed class ScenarioZeroNullSponsorIdAfterMigration
    {
        [Fact]
        public void NoBriefHasANullSponsorId()
            => Assert.Fail("pending: T431 db/46 NOT NULL after backfill — AC4");

        [Fact]
        public void NoSpotHasANullSponsorId()
            => Assert.Fail("pending: T431 db/46 NOT NULL after backfill — AC4");
    }

    public sealed class ScenarioTheFoldExpressionIsTextPinned
    {
        [Fact]
        public void Db46CarriesTheFoldExpressionForNameKey()
            => Assert.Fail("pending: T431 db/46 text pin name_key — AC5");

        [Fact]
        public void Db46CarriesTheFoldExpressionForPremiseKey()
            => Assert.Fail("pending: T431 db/46 text pin premise_key — AC5");
    }

    public sealed class ScenarioIdempotent
    {
        [Fact]
        public void ASecondRunExitsZero()
            => Assert.Fail("pending: T431 db/46 re-run — AC6");

        [Fact]
        public void ASecondRunChangesNoRow()
            => Assert.Fail("pending: T431 db/46 re-run no-op — AC6");
    }
}
