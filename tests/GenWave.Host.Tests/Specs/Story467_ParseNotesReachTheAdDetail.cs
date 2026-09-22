// STORY-467 — Parse notes reach the ad detail (gh-#742 · SPEC F200.3 · PLAN T551)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Host.Tests.Specs;

public static class FeatureParsenotesreachtheaddetail
{
    const string Pending = "pending: T551 — Parse notes reach the ad detail (STORY-467)";

    public sealed class ScenarioGetAdById
    {
        // Given: WebApplicationFactory, an ad whose script carried a NARRATOR line, GET /api/ads/{id}

        /// <summary>AC5 — parseNotes contains the note</summary>
        [Fact(Skip = Pending)]
        public void ServesTheNote() => Assert.Fail(Pending);
    }

}
