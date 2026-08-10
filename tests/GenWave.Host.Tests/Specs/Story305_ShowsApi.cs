// STORY-305 — The show entity & API (F115.1, F115.4, F115.5) — endpoint half
//
// BDD specification — xUnit, PENDING scaffold (planned 2026-08-10). Comment-bodied on
// purpose: /api/shows does not exist until T240 builds it. Entry-point facts drive the
// production surface via WebApplicationFactory (the F79/F90 route posture: AdminSurface
// + Settings policy). The repository half lives in MediaLibrary.Tests/Story305_ShowRepository.cs.

namespace GenWave.Host.Tests.Specs;

using Xunit;

public static class FeatureShowsApi
{
    public sealed class ScenarioCrudThroughTheProductionSurface
    {
        [Fact(Skip = "Pending (T240)")]
        public void CrudRoundTripsThroughTheEndpoints()
        {
            // Given an authenticated admin session
            // When  a show is created, listed, edited, and fetched via /api/shows
            // Then  every field round-trips; the routes sit on AdminSurface behind Settings
        }

        [Fact(Skip = "Pending (T240)")]
        public void UnreferencedShowDeletesClean()
        {
            // Given a show no block, special, or imaging row references
            // When  DELETE /api/shows/{slug} runs
            // Then  204 and the row is gone
        }
    }

    public sealed class ScenarioGuardedDelete
    {
        [Fact(Skip = "Pending (T240)")]
        public void DeleteWithReferencesFails409NamingBlocks()
        {
            // Given a show referenced by schedule blocks
            // When  DELETE runs
            // Then  409 whose body names the referencing blocks (the F104 guard precedent)
        }

        [Fact(Skip = "Pending (T240)")]
        public void ScopedImagingRowsAreNamedAndUnscopedBestEffort()
        {
            // Given a show referenced only by a scoped imaging row (no FK — F117.1)
            // When  DELETE runs
            // Then  the response names the row and the library-connection unscope write is
            //       issued (idempotent second write — F115.4)
        }
    }

    public sealed class ScenarioProvenanceProtection
    {
        [Fact(Skip = "Pending (T240)")]
        public void AuthoredSaveNeverErasesImportedProvenance()
        {
            // Given an imported show
            // When  an authored save targets its slug
            // Then  409 — the ThemeWriteGate two-phase posture (F115.5); imported_from survives
        }
    }
}
