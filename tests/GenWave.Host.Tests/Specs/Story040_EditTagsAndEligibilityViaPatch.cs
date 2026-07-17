// STORY-040 — Edit tags + rotation eligibility via PATCH (WIRE)
//
// BDD specification — xUnit. Drives the deployed admin endpoint (PATCH /api/media/{id}) and the
// selection seam (GET /media/random) on the running binary. Integration: needs the live stack +
// auth cookie, like the Story013 level-matching gate (HttpClient against the published port).
//
// Repo-level behavior (UpdateAsync outcomes + eligibility filter on GetRandomReadyAsync) is
// tested with the real Postgres fixture in:
//   tests/GenWave.MediaLibrary.Tests/Specs/Story040_AdminWriteRepoAndEligibilityFilter.cs
//
// The scenarios below require the full running stack (Postgres + ASP.NET Core + auth cookie)
// and are therefore operator-gated. See docs/PLAN.md Epic I.

namespace GenWave.Host.Tests.Specs;

public static class FeatureEditTagsAndEligibilityViaPatch
{
    const string OperatorGated = "Operator-verified live (W2); see docs/PLAN.md Epic I";

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTagEditPersistsAndReachesAir
    {
        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void PatchWithIfMatchUpdatesOnlyTheGivenColumnAndReturns204()
        {
            // PATCH /api/media/{id} { artist:"X" } + current If-Match → 204, only the artist column changed.
            Assert.Fail("operator-gated");
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void EditedTagReachesAirViaThePushAnnotation()
        {
            // After an edit, the next push annotation carries the new tag value (no file modification).
            Assert.Fail("operator-gated");
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void TheFileOnMediaIsByteIdenticalAfterAnEdit()
        {
            // The /media file is unchanged before/after the tag edit (no embedded-tag writeback).
            Assert.Fail("operator-gated");
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void GetMediaByIdReturnsTheRowVersionAsAWeakETag()
        {
            // GET /api/media/{id} response carries ETag = W/"<xmin>".
            Assert.Fail("operator-gated");
        }
    }

    public sealed class ScenarioEligibilityRemovesFromSelection
    {
        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void IneligibleTrackIsNeverReturnedByRandomSelection()
        {
            // After PATCH { eligible:false }, GET /media/random never returns that track.
            Assert.Fail("operator-gated");
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void IneligibleTrackStaysVisibleInTheCatalogAndIsReIncludable()
        {
            // The ineligible row is still listed by GET /api/media and PATCH { eligible:true } re-includes it.
            Assert.Fail("operator-gated");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioWriteSafetyAndDegradation
    {
        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void StaleIfMatchReturns409AndWritesNothing()
        {
            // PATCH with an If-Match that no longer matches xmin → 409 Conflict, no column changed.
            Assert.Fail("operator-gated");
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void WriteWithoutCookieOrWithNonJsonContentTypeIsRejected()
        {
            // With Admin:Password set: no cookie → 401; non-JSON Content-Type → 415.
            Assert.Fail("operator-gated");
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void OutOfScopeIdReturns403AndUnknownIdReturns404()
        {
            // PATCH an out-of-scope media id → 403; an unknown id → 404.
            Assert.Fail("operator-gated");
        }

        [Fact(Skip = OperatorGated), Trait("Category", "Integration")]
        public void AllIneligibleDegradesToSafeRotationNeverSilence()
        {
            // With every selectable row eligible=false, selection returns null and the engine airs
            // the safe-rotation backstop — no silence, no crash.
            Assert.Fail("operator-gated");
        }
    }
}
