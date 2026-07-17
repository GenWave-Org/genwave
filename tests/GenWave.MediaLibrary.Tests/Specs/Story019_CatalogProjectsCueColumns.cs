// STORY-019 — Catalog reads project cue columns onto MediaReference
//
// BDD specification — xUnit. Integration via DatabaseCollection.
// Specs Skip-pinned until T021 (catalog reads project cue) lands.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureCatalogProjectsCueColumns
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMediaRowExposesNullableCueColumns(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T021 — see docs/PLAN.md")]
        public void MediaRowHasNullableCueInSecProperty()
        {
            // var p = typeof(MediaRow).GetProperty("CueInSec")!;
            // Assert.Equal(typeof(double?), p.PropertyType);
            _ = db;
            Assert.Fail("pending T021");
        }

        [Fact(Skip = "Pending T021 — see docs/PLAN.md")]
        public void MediaRowHasNullableCueOutSecProperty()
        {
            // Assert.Equal(typeof(double?), typeof(MediaRow).GetProperty("CueOutSec")!.PropertyType);
            _ = db;
            Assert.Fail("pending T021");
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGetRandomReadySurfacesCuePoints(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T021 — see docs/PLAN.md")]
        public void ReturnedMediaReferenceCueIsNonNull()
        {
            // Seed a ready row with cue_in_sec=3.45, cue_out_sec=187.20.
            // var result = await catalog.GetRandomReadyAsync(LibraryScope([1]), [], ct);
            // Assert.NotNull(result!.Cue);
            _ = db;
            Assert.Fail("pending T021");
        }

        [Fact(Skip = "Pending T021 — see docs/PLAN.md")]
        public void CueInSecRoundTripsFromTheRow()
        {
            // Assert.Equal(3.45, result!.Cue!.CueInSec);
            _ = db;
            Assert.Fail("pending T021");
        }

        [Fact(Skip = "Pending T021 — see docs/PLAN.md")]
        public void CueOutSecRoundTripsFromTheRow()
        {
            // Assert.Equal(187.20, result!.Cue!.CueOutSec);
            _ = db;
            Assert.Fail("pending T021");
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGetByIdSurfacesCuePoints(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T021 — see docs/PLAN.md")]
        public void GetByIdReturnsTheSameCueValues()
        {
            // var result = await catalog.GetByIdAsync(LibraryScope([1]), id, ct);
            // Assert.Equal(3.45, result!.Cue!.CueInSec);
            _ = db;
            Assert.Fail("pending T021");
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRowsWithNullCueColumnsProjectToNullCue(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T021 — see docs/PLAN.md")]
        public void MediaReferenceCueIsNullForRowWithBothCueColumnsNull()
        {
            // Seed a ready row with cue_in_sec IS NULL AND cue_out_sec IS NULL.
            // var result = await catalog.GetByIdAsync(scope, id, ct);
            // Assert.Null(result!.Cue);
            _ = db;
            Assert.Fail("pending T021");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAsymmetricNullIsTreatedAsCueNull(DatabaseFixture db)
    {
        [Fact(Skip = "Pending T021 — see docs/PLAN.md")]
        public void MediaReferenceCueIsNullWhenOnlyOneCueColumnIsPopulated()
        {
            // Seed cue_in_sec=3.45 but cue_out_sec IS NULL (data-integrity edge).
            // var result = await catalog.GetByIdAsync(scope, id, ct);
            // Assert.Null(result!.Cue);
            _ = db;
            Assert.Fail("pending T021");
        }

        [Fact(Skip = "Pending T021 — see docs/PLAN.md")]
        public void AsymmetricRowEmitsWarnLogEntry()
        {
            // Capture ILogger writes; assert a WARN entry with the offending row id appears.
            _ = db;
            Assert.Fail("pending T021");
        }
    }
}
