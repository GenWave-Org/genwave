// STORY-375 — Dead files are visible the moment they cost a pick (SPEC F153.3–F153.4 · PLAN T372/T373)
//
// BDD specification — xUnit. PENDING until T372 (AC1/AC2) and T373 (AC3/AC4/AC5). Entry-point
// discipline: AC1/AC2 run the real dead_file IGardenerPass against rows arranged directly in the
// ephemeral Postgres (Support/EphemeralStationDatabase, the Story345/Story366 factory idiom over
// WebApplicationFactory<Program>) — state=failed, and a stale unavailable row past
// Library:Scan:MissThreshold. AC3–AC5 drive the PRODUCTION feeder path: the real
// MediaExistencePushGuard wired ahead of ILiquidsoapControl inside the factory's own container,
// pushing a MediaItem whose locator points into a temp media root (Path.GetTempPath()-rooted,
// mirroring Gh612_MediaExistencePushGuard.cs) with the file absent — then present again for AC5's
// resurrection — and the real IDeadFileReporter reporting fire-and-forget into the same rot_finding
// table AC1/AC2 read. AC4's throwing reporter is a scripted IDeadFileReporter substitute swapped into
// the container via services.Replace, timed against the guard's own decline to prove the WARN never
// costs the push a millisecond.
namespace GenWave.Host.Tests.Specs;

public static class FeatureDeadFilesAreVisibleWhenTheyCostAPick
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — failed, long-unavailable, and push-missed rows all surface
    // ---------------------------------------------------------------------

    public sealed class ScenarioFailedRows
    {
        // Given a row with state failed, When the dead_file pass runs.
        [Fact(Skip = "pending T372 (STORY-375 AC1)")]
        public void AnOpenDeadFileFindingExists() => Assert.Fail("pending T372");

        [Fact(Skip = "pending T372 (STORY-375 AC1)")]
        public void TheEvidenceReasonIsFailed() => Assert.Fail("pending T372");
    }

    public sealed class ScenarioLongUnavailableRows
    {
        // Given an unavailable row older than the miss grace, When the pass runs.
        [Fact(Skip = "pending T372 (STORY-375 AC2)")]
        public void AnOpenDeadFileFindingExists() => Assert.Fail("pending T372");

        [Fact(Skip = "pending T372 (STORY-375 AC2)")]
        public void TheEvidenceReasonIsUnavailable() => Assert.Fail("pending T372");
    }

    public sealed class ScenarioAPushMissReportsImmediately
    {
        // Given a ready row whose file is missing on disk, When the feeder pushes it.
        [Fact(Skip = "pending T373 (STORY-375 AC3)")]
        public void ThePushIsDeclinedUnchanged() => Assert.Fail("pending T373");

        [Fact(Skip = "pending T373 (STORY-375 AC3)")]
        public void AFindingExistsWithinOneSecondWithReasonPushMissing() => Assert.Fail("pending T373");
    }

    public sealed class ScenarioTheReporterNeverBlocks
    {
        // Given a reporter that throws, When the feeder pushes a missing file.
        [Fact(Skip = "pending T373 (STORY-375 AC4)")]
        public void TheDeclinesTimingIsUnchanged() => Assert.Fail("pending T373");

        [Fact(Skip = "pending T373 (STORY-375 AC4)")]
        public void ExactlyOneWarnNamesTheReporter() => Assert.Fail("pending T373");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the file comes back
    // ---------------------------------------------------------------------

    public sealed class ScenarioAResurrectedFileResolvesIt
    {
        // Given a push_missing finding and the file back on disk, When the scan sights it and the pass runs.
        [Fact(Skip = "pending T373 (STORY-375 AC5)")]
        public void TheFindingIsResolved() => Assert.Fail("pending T373");
    }
}
