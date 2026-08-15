// STORY-339 — The station's own image (SPEC F131, gh-#15 · PLAN T307)
//
// BDD specification — xUnit. Backend slots only; the authed admin tab-icon swap and
// the login-page exception (F131.3's browser-visible halves) are the T308 wire's
// acceptance. Skip-pinned until T307 lands.

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheStationsOwnImage
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioUploadNormalizesIntoTheSingletonRow
    {
        [Fact(Skip = "Pending T307 — see docs/PLAN.md")]
        public void TheStoredBytesAreAFresh512SquareMetadataFreePng()
        {
            Assert.Fail("pending T307");
        }

        [Fact(Skip = "Pending T307 — see docs/PLAN.md")]
        public void TheTokenRotatesOnEveryWrite()
        {
            Assert.Fail("pending T307");
        }
    }

    public sealed class ScenarioEverySlotFollowsLive
    {
        [Fact(Skip = "Pending T307 — see docs/PLAN.md")]
        public void TheArtworkFallbackServesTheRowBytesWhenSet()
        {
            // No-art/unknown-token/patter fallback = the uploaded image, no restart.
            Assert.Fail("pending T307");
        }

        [Fact(Skip = "Pending T307 — see docs/PLAN.md")]
        public void TheFeederStampsTheTokenVersionedStationUrlWhenCustomized()
        {
            // …/spectator/api/artwork/station/<token> — the mutable-under-immutable
            // favicon lesson made structural; shipped constant URL when absent.
            Assert.Fail("pending T307");
        }

        [Fact(Skip = "Pending T307 — see docs/PLAN.md")]
        public void TheSpectatorFaviconServesTheRowBytesWithShortCache()
        {
            // Stable URL ⇒ ETag/short-cache, never immutable.
            Assert.Fail("pending T307");
        }
    }

    public sealed class ScenarioDeletionReverts
    {
        [Fact(Skip = "Pending T307 — see docs/PLAN.md")]
        public void EverySlotReturnsToTheShippedLogoBytes()
        {
            // Byte-identical to a station that never uploaded (F131.2's upgrade promise).
            Assert.Fail("pending T307");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — no oracle
    // ---------------------------------------------------------------------

    public sealed class ScenarioUnknownStationTokensAreNotAProbe
    {
        [Fact(Skip = "Pending T307 — see docs/PLAN.md")]
        public void AnUnknownStationTokenServesTheCurrentBytesWith200()
        {
            // Current bytes, never history — a replaced image is unrecoverable via old tokens.
            Assert.Fail("pending T307");
        }
    }
}
