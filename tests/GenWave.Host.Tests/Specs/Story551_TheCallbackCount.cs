// STORY-551 — The callback count, the Status api half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. GET /api/status through the test host with a fake repository; the page half is admin-ui/__specs__/dashboard-callbacks-number.spec.tsx.
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Host.Tests.Specs;

public static class FeatureTheCallbackCount
{
    // AC2 — the Status page
    public sealed class ScenarioTheStatusResponse
    {
        // Given 12 used over 16 DJ-hours

        [Fact(Skip = "pending: T738 — see docs/PLAN.md")]
        public void TheStatusResponseCarriesCallbacksPerDJHour075() => Assert.Fail("pending: T738 — the status response carries callbacks per DJ hour 0.75");
    }

    // AC3 — nothing yet (sad path)
    public sealed class ScenarioNothingYet
    {
        // Given no offers

        [Fact(Skip = "pending: T738 — see docs/PLAN.md")]
        public void TheStatusResponseCarriesNoCallbackRate() => Assert.Fail("pending: T738 — the status response carries no callback rate");
    }
}
