// STORY-245 — See the art we already broadcast (gh-#159, SPEC F93.3, PLAN T125/T126)
//
// BDD specification — xUnit, pending. The card render (AC2) and icon fallback render (AC3's
// visual half) are T126 browser acceptance per the T92 precedent — no fake unit tests here.
// These facts pin the wire contract the page consumes.

namespace GenWave.Host.Tests.Specs;

public static class FeatureArtworkOnTheCard
{
    public sealed class ScenarioArtworkUrlOnTrackState
    {
        // Given a track with art on air and Station:PublicBaseUrl set (F93.3).

        [Fact(Skip = "Pending (T125)")]
        public void TrackStateCarriesTheF88TokenUrl() { }
    }

    public sealed class ScenarioFallbacksAreTheStationIcon
    {
        // Sad path — art-less track and patter (F93.3).

        [Fact(Skip = "Pending (T125)")]
        public void ArtLessTrackCarriesNullArtworkUrl() { }

        [Fact(Skip = "Pending (T125)")]
        public void PatterStateCarriesNoArtworkUrl() { }

        [Fact(Skip = "Pending (T126): browser acceptance — card renders art with station-icon loading/fallback")]
        public void CardRenderIsBrowserAcceptance() { }
    }
}
