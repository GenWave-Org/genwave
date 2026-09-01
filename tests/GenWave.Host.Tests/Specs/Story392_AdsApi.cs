// STORY-392 — I manage the Ads library (API half · F162.1 · pending T403)
// Also carries STORY-390 AC9 (the owner's editor gets the same law — validator at save).
// The page half (AC1–AC5 in a browser) is specced in admin-ui/__specs__/ads-page.spec.tsx.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAdsApi
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — through the production surface (WebApplicationFactory)
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheEditorRoundTrips
    {
        [Fact(Skip = "Pending T403 — see docs/PLAN.md")]
        public void AValidDraftPostsAndReadsBackEveryField()
        {
            // POST /api/ads {brand,title,script,voices,seconds,bed} → GET returns it verbatim.
            Assert.Fail("pending T403");
        }

        [Fact(Skip = "Pending T403 — see docs/PLAN.md")]
        public void OwnerTextIsStoredVerbatimNoLlmTouchesIt()
        {
            Assert.Fail("pending T403");
        }
    }

    public sealed class ScenarioVerbsDriveTheStateMachine
    {
        [Fact(Skip = "Pending T403 — see docs/PLAN.md")]
        public void ApproveMovesADraftToApproved()
        {
            Assert.Fail("pending T403");
        }

        [Fact(Skip = "Pending T403 — see docs/PLAN.md")]
        public void RetryMovesAFailedSpotToApproved()
        {
            Assert.Fail("pending T403");
        }

        [Fact(Skip = "Pending T403 — see docs/PLAN.md")]
        public void RetireMovesAReadySpotToRetired()
        {
            Assert.Fail("pending T403");
        }

        [Fact(Skip = "Pending T403 — see docs/PLAN.md")]
        public void TheListPagesByStateOnTheSharedShape()
        {
            // GET /api/ads?state=ready&page=&limit= — the Gardener paging idiom.
            Assert.Fail("pending T403");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheValidatorGuardsTheSave
    {
        [Fact(Skip = "Pending T403 — see docs/PLAN.md")]
        public void AViolatingScriptIs400WithTheRuleId()
        {
            // STORY-390 AC9: a blocklisted brand in an owner script → 400 naming brand-collision.
            Assert.Fail("pending T403");
        }
    }

    public sealed class ScenarioAdminSurfacePosture
    {
        [Fact(Skip = "Pending T403 — see docs/PLAN.md")]
        public void EveryAdsRouteIs404WhileAdminIsDisabled()
        {
            // Admin:Enabled=false: /api/ads* 404s like every admin route (F162.1).
            Assert.Fail("pending T403");
        }
    }
}
