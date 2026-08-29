// STORY-374 — The gardener tends a self-healing queue (SPEC F153.1–F153.2, F153.9–F153.10 · PLAN T372/T377)
//
// BDD specification — xUnit. PENDING until T372 (AC1/AC2/AC3/AC5/AC6) and T377 (AC4/AC7/AC8/AC10).
// Entry-point discipline: every fact drives the REAL production binary (WebApplicationFactory<Program>,
// the Story345/Story366 factory idiom over the ephemeral Postgres — Support/EphemeralStationDatabase).
// AC1–AC3/AC5 arrange rot_finding + backing library rows directly and run the real dead_file
// IGardenerPass (or a second pass stub for the generic re-open/resolve shape) off the container's own
// GardenerService. AC6 is the one exhibit that needs the SERVICE running unattended inside the
// production binary rather than a direct pass invocation: the factory's host is started (not
// built-and-discarded) with Gardener__IntervalMinutes=1 against either a fake/adjustable clock the
// BackgroundService's own PeriodicTimer reads, or — failing a clock seam — a genuinely short real
// interval, so "two minutes pass" is an honest wait, not a call straight into IGardenerPass.RunAsync.
// AC4/AC7/AC8/AC10 drive the wire: POST /api/gardener/findings/{id}/dismiss, GET /api/gardener/findings,
// GET /api/status. AC9 (the Gardener page renders sections/verbs/dismiss) is a Jest todo elsewhere.
namespace GenWave.Host.Tests.Specs;

public static class FeatureTheGardenerTendsAQueue
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — open, resolve, re-open, dismiss, list, count
    // ---------------------------------------------------------------------

    public sealed class ScenarioAPassOpensAFinding
    {
        // Given a row whose predicate for kind K holds and no finding, When the gardener pass for K runs.
        [Fact(Skip = "pending T372 (STORY-374 AC1)")]
        public void TheFindingIsOpenForThatRowAndKind() => Assert.Fail("pending T372");

        [Fact(Skip = "pending T372 (STORY-374 AC1)")]
        public void TheFindingCarriesEvidence() => Assert.Fail("pending T372");
    }

    public sealed class ScenarioAPassResolvesAFinding
    {
        // Given an open finding whose predicate no longer holds, When the pass runs.
        [Fact(Skip = "pending T372 (STORY-374 AC2)")]
        public void TheStateIsResolved() => Assert.Fail("pending T372");

        [Fact(Skip = "pending T372 (STORY-374 AC2)")]
        public void TheResolvedAtIsSet() => Assert.Fail("pending T372");
    }

    public sealed class ScenarioAResolvedFindingReopens
    {
        // Given a resolved finding whose predicate holds again, When the pass runs.
        [Fact(Skip = "pending T372 (STORY-374 AC3)")]
        public void TheStateIsOpenAgain() => Assert.Fail("pending T372");

        [Fact(Skip = "pending T372 (STORY-374 AC3)")]
        public void TheSameMediaByKindRowIsReusedNotDuplicated() => Assert.Fail("pending T372");
    }

    public sealed class ScenarioDismissIsForever
    {
        // Given an open finding, When POST /api/gardener/findings/{id}/dismiss is called, then the predicate keeps holding through three passes.
        [Fact(Skip = "pending T377 (STORY-374 AC4)")]
        public void TheDismissPostSucceeds() => Assert.Fail("pending T377");

        [Fact(Skip = "pending T377 (STORY-374 AC4)")]
        public void TheStateStaysDismissedThroughThreePasses() => Assert.Fail("pending T377");
    }

    public sealed class ScenarioTheLoopIsBoundedAndResilient
    {
        // Given a pass that throws, When the service ticks.
        [Fact(Skip = "pending T372 (STORY-374 AC5)")]
        public void TheOtherPassesStillRun() => Assert.Fail("pending T372");

        [Fact(Skip = "pending T372 (STORY-374 AC5)")]
        public void OneWarnNamesTheFailedPass() => Assert.Fail("pending T372");

        [Fact(Skip = "pending T372 (STORY-374 AC5)")]
        public void TheNextTickRetries() => Assert.Fail("pending T372");
    }

    public sealed class ScenarioTheServiceRunsInTheProductionBinary
    {
        // Given the api container started with IntervalMinutes 1 and a failed row, When two minutes pass.
        [Fact(Skip = "pending T372 (STORY-374 AC6)")]
        public void ADeadFileFindingExistsForTheFailedRow() => Assert.Fail("pending T372");
    }

    public sealed class ScenarioFindingsAreListedGrouped
    {
        // Given open findings of three kinds, two near-duplicates sharing a group_key, When GET /api/gardener/findings is called.
        [Fact(Skip = "pending T377 (STORY-374 AC7)")]
        public void TheResponseGroupsFindingsByKind() => Assert.Fail("pending T377");

        [Fact(Skip = "pending T377 (STORY-374 AC7)")]
        public void TheDuplicateGroupListsBothMembersWithPathDurationPlaysAndRating() => Assert.Fail("pending T377");
    }

    public sealed class ScenarioStatusCounts
    {
        // Given the findings above, When GET /api/status is called.
        [Fact(Skip = "pending T377 (STORY-374 AC8)")]
        public void TheGardenerSectionCarriesOpenCountsPerKind() => Assert.Fail("pending T377");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the admin surface gates the queue
    // ---------------------------------------------------------------------

    public sealed class ScenarioAdminSurface
    {
        // Given no session, When GET /api/gardener/findings is called.
        [Fact(Skip = "pending T377 (STORY-374 AC10)")]
        public void TheResponseIsFourOhOne() => Assert.Fail("pending T377");
    }
}
