// STORY-226 — The station checks what it actually has (SPEC F87.5, PLAN T89)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration), authored PENDING at
// /plan time. The matcher probes the real catalog: ready + eligible + not-never-play only.

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureRequestMatching
{
    public static class ScenarioTrackAndVibeResolution
    {
        [Fact(Skip = "Pending — PLAN T89 (/build-loop)")]
        public static void AHeldArtistPredicateStoresTheMatchedMediaId() { }

        [Fact(Skip = "Pending — PLAN T89 (/build-loop)")]
        public static void AMissedArtistWithMoodsStaysPendingAsAVibeRequest() { }
    }

    public static class SadPathVetoesAndSilence
    {
        [Fact(Skip = "Pending — PLAN T89 (/build-loop)")]
        public static void ANeverPlayOnlyMatchStoresNoMediaId()
        {
            // Operator vetoes are law at match time (F87.5).
        }

        [Fact(Skip = "Pending — PLAN T89 (/build-loop)")]
        public static void AnIneligibleOnlyMatchStoresNoMediaId() { }

        [Fact(Skip = "Pending — PLAN T89 (/build-loop)")]
        public static void EmptyPredicatesBecomeUnmatchedSilently() { }
    }
}
