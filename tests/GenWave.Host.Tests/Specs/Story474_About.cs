// STORY-474 — About (gh-#16 · SPEC F207 · PLAN T561)
//
// BDD specification — xUnit. RED at plan time: every fact is [Fact(Skip = Pending)] with a loud body —
// remove the Skip only in the task that makes it green. Each Given comment names the arrange the scenario needs.

namespace GenWave.Host.Tests.Specs;

public static class FeatureAbout
{
    const string Pending = "pending: T561 — About (STORY-474)";

    public sealed class ScenarioGetAboutWithASession
    {
        // Given: WebApplicationFactory, GET /api/about

        /// <summary>AC1 — version, stationName, tagline, libraryCount, uptimeSeconds, attributions</summary>
        [Fact(Skip = Pending)]
        public void ServesEveryField() => Assert.Fail(Pending);

        /// <summary>AC2 — version equals the assembly informational version</summary>
        [Fact(Skip = Pending)]
        public void MatchesTheAssemblyVersion() => Assert.Fail(Pending);

        /// <summary>AC3 — attributions equal GET /api/attributions</summary>
        [Fact(Skip = Pending)]
        public void ReusesTheAttributionList() => Assert.Fail(Pending);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioGetAboutWithoutASession
    {
        // Given: no cookie

        /// <summary>AC6 — 401</summary>
        [Fact(Skip = Pending)]
        public void RequiresASession() => Assert.Fail(Pending);
    }

}
