// STORY-222 — Tracks get an artwork door that reveals nothing (SPEC F88.2–F88.3, PLAN T84)
//
// BDD specification — xUnit, authored PENDING at /plan time. Entry-point discipline: the
// happy/sad scenarios drive GET /spectator/api/artwork/{token} through the production
// pipeline (WebApplicationFactory), never an internal extractor call.

namespace GenWave.Host.Tests.Specs;

public static class FeatureArtworkEndpoint
{
    public static class ScenarioArtServed
    {
        [Fact(Skip = "Pending — PLAN T84 (/build-loop)")]
        public static void AKnownTokenReturnsAJpegOfTheEmbeddedCover()
        {
            // Given a track with embedded art and its artwork_token
            // When GET /spectator/api/artwork/{token} through the production pipeline
            // Then the body is image/jpeg derived from that cover, ≤500px.
        }

        [Fact(Skip = "Pending — PLAN T84 (/build-loop)")]
        public static void TheResponseCarriesImmutablePublicCacheHeaders()
        {
            // Then Cache-Control is public, max-age=31536000, immutable.
        }

        [Fact(Skip = "Pending — PLAN T84 (/build-loop)")]
        public static void ASecondFetchServesFromTheDiskCache()
        {
            // Then the extraction runs once — the second request never re-invokes ffmpeg.
        }
    }

    public static class SadPathNoOracle
    {
        [Fact(Skip = "Pending — PLAN T84 (/build-loop)")]
        public static void AnUnknownTokenServesTheStationIconWith200()
        {
            // Token guessing learns nothing (F88.3).
        }

        [Fact(Skip = "Pending — PLAN T84 (/build-loop)")]
        public static void AnArtlessTrackTokenServesTheStationIconWith200() { }

        [Fact(Skip = "Pending — PLAN T84 (/build-loop)")]
        public static void NoSpectatorPayloadOrUrlEverCarriesANumericMediaId()
        {
            // F62.9 stays intact with artwork live — disclosure suite extension.
        }
    }
}
