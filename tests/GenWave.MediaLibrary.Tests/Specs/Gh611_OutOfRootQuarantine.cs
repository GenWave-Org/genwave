// gh-#611 — scanner: out-of-root ghosts stay ready forever.
//
// BDD specification — xUnit. Integration via DatabaseCollection (real Postgres). Amends the F27.7
// scope (Story077): the missing-diff's MediaRoot scoping deliberately spares authored rows, but the
// same scoping made rows discovered under a PREVIOUS root configuration immortal — never sighted,
// never judged, `ready` forever. The 2026-08-22 doubled-library incident: a host-side wire run
// catalogued the same 9,112 files under host paths; every pick of one died silently at the engine
// for seven days (the gh-#610 root cause). The quarantine flips such ghosts unavailable after the
// F58 miss grace (a one-scan root misconfiguration must not nuke a catalog), exempts the authored
// carve-out by explicit root list, never judges another library's rows, and hands resurrection to
// the ordinary gh-#112 discovery branch the moment a quarantined path is sighted under a root again.

using Dapper;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureOutOfRootQuarantine
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — ghosts quarantine, after the same grace missing rows get
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGhostRowsQuarantineAfterTheGrace(DatabaseFixture db)
    {
        [Fact]
        public async Task AReadyRowOutsideEveryRootFlipsUnavailableOnlyOnceTheGraceElapses()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var mediaRoot = TestMedia.NewTempDir();
            try
            {
                // A ghost: discovered under some earlier root that no longer exists — the row is
                // real, enriched, ready, and no scan of the CURRENT root can ever sight its path.
                var id = await repo.InsertDiscoveredAsync(
                    "/old-root/ghost.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

                var (scan, _) = Harness.Scanner(repo, mediaRoot, missThreshold: 2);

                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Equal("ready", await Harness.StateOfAsync(db, id));   // grace defers (F58)

                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Equal("unavailable", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(mediaRoot, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the F27.7 authored carve-out survives the quarantine too
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAuthoredRowsStayExempt(DatabaseFixture db)
    {
        [Fact]
        public async Task AnAuthoredRowOutlivesScansBeyondTheGraceWindow()
        {
            // Story077 proved authored rows survive ONE scan; the quarantine must not erode that
            // into "survive MissThreshold scans". Threshold 1 means a non-exempt ghost would flip
            // on the FIRST pass — three passes prove the exemption, not the grace, is the shield.
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var mediaRoot = TestMedia.NewTempDir();
            try
            {
                var id = await repo.InsertDiscoveredAsync(
                    "/authored/gh611-probe.wav", "wav", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

                var (scan, _) = Harness.Scanner(repo, mediaRoot, missThreshold: 1);
                for (var pass = 0; pass < 3; pass++)
                    await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Equal("ready", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(mediaRoot, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — another library's rows are never this scan's to judge
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnotherLibrarysRowsAreNeverJudged(DatabaseFixture db)
    {
        [Fact]
        public async Task AnOutOfRootRowInAnotherLibrarySurvivesEveryScan()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var mediaRoot = TestMedia.NewTempDir();
            try
            {
                var id = await repo.InsertDiscoveredAsync(
                    "/elsewhere/other-library.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

                // Move the row to a freshly minted second library — the quarantine judges only the
                // scanned library's rows (the safe library's assets are the production shape).
                await using (var conn = await db.DataSource.OpenConnectionAsync())
                {
                    var otherLibrary = await conn.ExecuteScalarAsync<long>(
                        "insert into library.library (name) values ('gh611-other') returning id");
                    await conn.ExecuteAsync(
                        "update library.media set library_id = @otherLibrary where id = @id",
                        new { otherLibrary, id });
                }

                var (scan, _) = Harness.Scanner(repo, mediaRoot, missThreshold: 1);
                await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Equal("ready", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(mediaRoot, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH TURNED GOOD — a quarantined ghost resurrects when sighted
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioQuarantinedGhostResurrectsWhenTheRootMovesBack(DatabaseFixture db)
    {
        [Fact]
        public async Task ASightedQuarantinedPathReentersDiscoveryThroughTheOrdinaryBranch()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var rootA = TestMedia.NewTempDir();
            var rootB = TestMedia.NewTempDir();
            try
            {
                // The row's path lives under root B, but the station scans root A → quarantined.
                var pathUnderB = Path.Combine(rootB, "a.flac");
                var id = await repo.InsertDiscoveredAsync(
                    pathUnderB, "flac", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

                var (scanA, _) = Harness.Scanner(repo, rootA, missThreshold: 1);
                await scanA.ScanOnceAsync(CancellationToken.None);
                Assert.Equal("unavailable", await Harness.StateOfAsync(db, id));

                // The root moves back (or the operator fixes the config): the file exists at the
                // exact stored path, a scan of root B sights it, and the gh-#112 resurrection
                // branch re-enters it into discovery for a fresh enrichment pass.
                TestMedia.CreateTone(rootB, "a.flac");
                var (scanB, queue) = Harness.Scanner(repo, rootB, missThreshold: 1);
                await scanB.ScanOnceAsync(CancellationToken.None);

                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));
                Assert.Contains(id, Harness.DrainIds(queue));
            }
            finally
            {
                Directory.Delete(rootA, recursive: true);
                Directory.Delete(rootB, recursive: true);
            }
        }
    }
}
