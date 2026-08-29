// STORY-368 — I can see how healthy my rotation is (SPEC F149.5 · PLAN T371)
//
// BDD specification — xUnit. PENDING until T371. Entry-point discipline: every fact drives the
// REAL production binary (WebApplicationFactory<Program>, the Story345/Story366 factory idiom
// over an ephemeral station+library Postgres — tests/GenWave.Host.Tests/Support/
// EphemeralStationDatabase) seeded with the 10-row rotation fixture the ACs share (6 never aired,
// 3 aired once, 1 aired 6 times, one last aired 91 days ago), read back through GET /api/status,
// GET /api/media, and GET /api/media/42. AC2's dashboard tile itself is a Jest todo in admin-ui,
// not this suite — the fact here only pins that GET /api/status carries the `rotation` property
// the tile reads from.
namespace GenWave.Host.Tests.Specs;

public static class FeatureRotationHealthIsVisible
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — the numbers surface on status, the catalog, and the detail page
    // ---------------------------------------------------------------------

    public sealed class ScenarioStatusCarriesTheRotationCounts
    {
        // Given a station with 10 playable rows (6 never aired, 3 aired once, 1 aired 6 times,
        // one last aired 91 days ago), When GET /api/status is called.
        [Fact(Skip = "pending T371 (STORY-368 AC1)")]
        public void RotationIsNeverAiredSixAiredOnceThreeNotAiredDays90OneWithTheEpoch() => Assert.Fail("pending T371");
    }

    public sealed class ScenarioTheDashboardShowsARotationHealthTile
    {
        // Given the status above, When GET /api/status is called — the tile itself renders from
        // this property in admin-ui (Jest todo there, not here).
        [Fact(Skip = "pending T371 (STORY-368 AC2)")]
        public void TheStatusResponseCarriesARotationProperty() => Assert.Fail("pending T371");
    }

    public sealed class ScenarioNeverAiredFilter
    {
        // Given the catalog above, When GET /api/media?never-aired=true is called.
        [Fact(Skip = "pending T371 (STORY-368 AC3)")]
        public void ExactlyTheSixNeverAiredRowsAreReturned() => Assert.Fail("pending T371");
    }

    public sealed class ScenarioAiredBeforeFilter
    {
        // Given the catalog above, When GET /api/media?aired-before=<today − 90d> is called.
        [Fact(Skip = "pending T371 (STORY-368 AC4)")]
        public void ExactlyTheRowLastAiredNinetyOneDaysAgoIsReturned() => Assert.Fail("pending T371");
    }

    public sealed class ScenarioTheDetailPageShowsRotationFacts
    {
        // Given media 42 with play_count 3, first T1, last T2, When GET /api/media/42 is called.
        [Fact(Skip = "pending T371 (STORY-368 AC5)")]
        public void TheResponseCarriesPlaysThreeFirstAiredAtTOneLastAiredAtTTwo() => Assert.Fail("pending T371");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — unplayable rows never surface in the filters
    // ---------------------------------------------------------------------

    public sealed class ScenarioTheFiltersAreInertForUnplayableRows
    {
        // Given an unavailable row that never aired, When GET /api/media?never-aired=true is called.
        [Fact(Skip = "pending T371 (STORY-368 AC6)")]
        public void ItIsNotReturned() => Assert.Fail("pending T371");
    }
}
