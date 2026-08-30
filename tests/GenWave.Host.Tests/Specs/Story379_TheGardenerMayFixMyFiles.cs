// STORY-379 — The gardener may fix my files, when I say so (SPEC F154 · PLAN T379/T380/T381)
//
// BDD specification — xUnit. PENDING until T379 (AC2/AC7/AC9/AC10/AC11/AC12/AC14 — the planner/jail,
// driven THROUGH the dry-run endpoint), T380 (AC3/AC4/AC5/AC6/AC13 — the executors), and T381 (AC1/AC8
// and the dry-run/confirm wire for everything else). Entry-point discipline: every fact drives the
// REAL production binary (WebApplicationFactory<Program>, the Story345/Story366 factory idiom over the
// ephemeral Postgres — Support/EphemeralStationDatabase) — POST /api/gardener/file-actions/dry-run and
// …/confirm, AdminOnly, Gardener__FileActions__Enabled=true for every scenario except AC1 (which needs
// it unset). Arrange: a fresh temp directory per scenario as the library root (Library:MediaRoot), with
// a real ffmpeg-authored small mp3 fixture inside it (the Story016/Gh257 idiom — TagLib needs a genuine
// frame to retag) plus a matching media row; a second temp directory stands in as a second library's
// root and a third as an exempt root (Library:Scan:QuarantineExemptRoots) for AC11; a real filesystem
// symlink inside the root pointing outside it for AC10 — never a mocked filesystem, since the jail's
// own canonicalise/symlink-resolve/root-prefix check is the thing under test. A real scan tick
// (ScanService) runs after each confirm for AC3/AC13's zero-drift claim.
namespace GenWave.Host.Tests.Specs;

public static class FeatureTheGardenerMayFixMyFiles
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — dry-run plans, confirm executes, the audit and admin gate hold
    // ---------------------------------------------------------------------

    public sealed class ScenarioDefaultOff
    {
        // Given Gardener:FileActions:Enabled unset, When POST /api/gardener/file-actions/dry-run is called.
        [Fact(Skip = "pending T381 (STORY-379 AC1)")]
        public void TheResponseIsFourOhFour() => Assert.Fail("pending T381");

        [Fact(Skip = "pending T381 (STORY-379 AC1)")]
        public void TheResponseNamesTheEnablingKnob() => Assert.Fail("pending T381");
    }

    public sealed class ScenarioDryRunReturnsAPlanAndAToken
    {
        // Given actions enabled and media 42 at /media/a/x.mp3, When dry-run {mediaId: 42, verb: "rename"} is called.
        [Fact(Skip = "pending T379 (STORY-379 AC2)")]
        public void TheResponseCarriesTheFromPath() => Assert.Fail("pending T379");

        [Fact(Skip = "pending T379 (STORY-379 AC2)")]
        public void TheResponseCarriesTheComputedToPath() => Assert.Fail("pending T379");

        [Fact(Skip = "pending T379 (STORY-379 AC2)")]
        public void TheResponseCarriesAPlanToken() => Assert.Fail("pending T379");
    }

    public sealed class ScenarioConfirmExecutesRenameAndReStamps
    {
        // Given the plan above, When confirm {plan_token} is called.
        [Fact(Skip = "pending T380 (STORY-379 AC3)")]
        public void TheFileIsAtTheNewPath() => Assert.Fail("pending T380");

        [Fact(Skip = "pending T380 (STORY-379 AC3)")]
        public void TheLibraryRowsPathSizeAndMtimeMatchIt() => Assert.Fail("pending T380");

        [Fact(Skip = "pending T380 (STORY-379 AC3)")]
        public void TheNextScanReportsZeroDiscoveredChangedOrMissing() => Assert.Fail("pending T380");
    }

    public sealed class ScenarioRetagWritesTagsNotAudio
    {
        // Given media 42 with catalog artist "A" and file tag artist "B", When retag is confirmed.
        [Fact(Skip = "pending T380 (STORY-379 AC4)")]
        public void TheFilesArtistTagIsA() => Assert.Fail("pending T380");

        [Fact(Skip = "pending T380 (STORY-379 AC4)")]
        public void TheAudioStreamBytesAreUnchanged() => Assert.Fail("pending T380");

        [Fact(Skip = "pending T380 (STORY-379 AC4)")]
        public void TheEnrichmentStampsAreUntouched() => Assert.Fail("pending T380");
    }

    public sealed class ScenarioMoveWithinTheRoot
    {
        // Given a target directory under the same library root, When move is confirmed.
        [Fact(Skip = "pending T380 (STORY-379 AC5)")]
        public void TheFileIsAtTheTargetDirectory() => Assert.Fail("pending T380");

        [Fact(Skip = "pending T380 (STORY-379 AC5)")]
        public void TheRowsPathFollowsIt() => Assert.Fail("pending T380");
    }

    public sealed class ScenarioTheAudit
    {
        // Given any confirmed action, When library.file_action is read.
        [Fact(Skip = "pending T380 (STORY-379 AC6)")]
        public void OneRowCarriesVerbFromToPlanTokenAndOutcome() => Assert.Fail("pending T380");
    }

    public sealed class ScenarioToctou
    {
        // Given a plan token minted before the row was PATCHed (xmin changed), When confirm is called.
        [Fact(Skip = "pending T379 (STORY-379 AC7)")]
        public void TheResponseIsFourOhNine() => Assert.Fail("pending T379");

        [Fact(Skip = "pending T379 (STORY-379 AC7)")]
        public void NothingMoved() => Assert.Fail("pending T379");
    }

    public sealed class ScenarioAdminOnly
    {
        // Given a Curation-only session, When dry-run is called.
        [Fact(Skip = "pending T381 (STORY-379 AC8)")]
        public void TheResponseIsFourOhThree() => Assert.Fail("pending T381");
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the jail refuses, and failures never half-do a move
    // ---------------------------------------------------------------------

    public sealed class ScenarioTraversalIsRefused
    {
        // Given a target of "../../etc/x.mp3", When dry-run is called.
        [Fact(Skip = "pending T379 (STORY-379 AC9)")]
        public void TheResponseIsFourHundredNamingTheRule() => Assert.Fail("pending T379");

        [Fact(Skip = "pending T379 (STORY-379 AC9)")]
        public void TheOffendingPathIsNotEchoed() => Assert.Fail("pending T379");
    }

    public sealed class ScenarioASymlinkEscapeIsRefused
    {
        // Given a directory under the root that is a symlink to outside it, When move targets it.
        [Fact(Skip = "pending T379 (STORY-379 AC10)")]
        public void TheResponseIsFourHundred() => Assert.Fail("pending T379");

        [Fact(Skip = "pending T379 (STORY-379 AC10)")]
        public void TheRefusalHappensBeforeAnyIo() => Assert.Fail("pending T379");
    }

    public sealed class ScenarioAnotherRootIsRefused
    {
        // Given a target under a different library's root or under an exempt root, When dry-run is called.
        [Fact(Skip = "pending T379 (STORY-379 AC11)")]
        public void TheDifferentLibraryRootTargetIsFourHundred() => Assert.Fail("pending T379");

        [Fact(Skip = "pending T379 (STORY-379 AC11)")]
        public void TheExemptRootTargetIsFourHundred() => Assert.Fail("pending T379");
    }

    public sealed class ScenarioNeverOverwrite
    {
        // Given a target that exists, When confirm is called.
        [Fact(Skip = "pending T379 (STORY-379 AC12)")]
        public void TheResponseIsFourOhNine() => Assert.Fail("pending T379");

        [Fact(Skip = "pending T379 (STORY-379 AC12)")]
        public void BothFilesAreUnchanged() => Assert.Fail("pending T379");
    }

    public sealed class ScenarioAFailedDbUpdateRevertsTheMove
    {
        // Given the FS move succeeds and the row update throws, When confirm runs.
        [Fact(Skip = "pending T380 (STORY-379 AC13)")]
        public void TheFileIsBackAtTheOriginalPath() => Assert.Fail("pending T380");

        [Fact(Skip = "pending T380 (STORY-379 AC13)")]
        public void TheAuditRowSaysReverted() => Assert.Fail("pending T380");
    }

    public sealed class ScenarioAnExpiredToken
    {
        // Given a plan token older than 10 minutes, When confirm is called.
        [Fact(Skip = "pending T379 (STORY-379 AC14)")]
        public void TheResponseIsFourOhNine() => Assert.Fail("pending T379");
    }
}
