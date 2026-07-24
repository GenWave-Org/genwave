// STORY-223 — Players see the cover of what's on air (SPEC F88.4–F88.5, PLAN T85)
//
// BDD specification — xUnit, authored PENDING at /plan time. Feeder annotation facts drive
// the production annotation builder; the engine-side icy_metadata line is a static guard on
// genwave.liq (zero-diff epoch deliberately broken here, re-pinned at T93). The live ICY
// observation (F88.5) is T85's compose-stack acceptance, not a unit fact.

namespace GenWave.Host.Tests.Specs;

public static class FeatureArtworkEmission
{
    public static class ScenarioAnnotationsCarryArtworkUrls
    {
        [Fact(Skip = "Pending — PLAN T85 (/build-loop)")]
        public static void AMusicPushCarriesItsTokenArtworkUrl()
        {
            // Given Station:PublicBaseUrl set  When the feeder annotates a music request
            // Then url=<base>/spectator/api/artwork/<token> rides the annotation.
        }

        [Fact(Skip = "Pending — PLAN T85 (/build-loop)")]
        public static void ATtsPushCarriesTheStationIconUrl() { }

        [Fact(Skip = "Pending — PLAN T85 (/build-loop)")]
        public static void TheEngineScriptForwardsUrlInIcyMetadata()
        {
            // genwave.liq's output.icecast icy_metadata list includes "url" — static guard.
        }
    }

    public static class SadPathUnsetBase
    {
        [Fact(Skip = "Pending — PLAN T85 (/build-loop)")]
        public static void AnEmptyPublicBaseUrlEmitsNoUrlAnnotationAnywhere()
        {
            // The default deployment stays byte-identical to pre-F88 annotations.
        }
    }
}
