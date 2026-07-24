// STORY-229 — The spectator page grows a request line (SPEC F87.11, PLAN T92)
//
// BDD specification — xUnit, authored PENDING at /plan time. The page is api-served static
// (F63) — served-markup facts here; hide/show and 429 handling are client JS proven in a
// real browser (T92's acceptance). Plan-time decision: the page learns the toggle from a
// `requestsEnabled` field on the about projection (pinned contract addition, re-pinned at T93).

namespace GenWave.Host.Tests.Specs;

public static class FeatureSpectatorRequestForm
{
    public static class ScenarioFormServed
    {
        [Fact(Skip = "Pending — PLAN T92 (/build-loop)")]
        public static void ThePageBundleContainsTheWishFormWithClientSideLengthCap() { }

        [Fact(Skip = "Pending — PLAN T92 (/build-loop)")]
        public static void TheAboutProjectionCarriesRequestsEnabled()
        {
            // The one new pinned public field this epic adds (F87.11 mechanism).
        }
    }

    public static class SadPathQuietWhenOff
    {
        [Fact(Skip = "Pending — PLAN T92 (/build-loop)")]
        public static void RequestsDisabledMeansAboutSaysSoAndTheFormLogicHidesIt()
        {
            // Served JS keys visibility off requestsEnabled — browser half verified at T92.
        }
    }
}
