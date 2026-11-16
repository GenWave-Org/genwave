// STORY-552 — Asked and aired on the Memory tab, the api half (gh-#1001 · SPEC F218.40–F218.51 · PLAN slice 3)
//
// BDD specification — xUnit. GET api/personas/{id}/memory through the test host; the row copy is admin-ui/__specs__/personas-memory-asked-aired.spec.tsx.
// Every fact is pending at plan time (2026-11-16); the named task activates its facts, watches them fail,
// then makes them pass.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAskedAndAiredOnTheMemoryTab
{
    // AC1 — the API
    public sealed class ScenarioTheApi
    {
        // Given a memory with 4 offers in the last 28 days and aired count 1

        [Fact(Skip = "pending: T739 — see docs/PLAN.md")]
        public void TheRowCarriesAsked4AndAired1() => Assert.Fail("pending: T739 — the row carries asked 4 and aired 1");
    }
}
