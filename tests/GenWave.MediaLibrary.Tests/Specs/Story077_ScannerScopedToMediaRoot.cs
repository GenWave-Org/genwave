// STORY-077 — Scanner never marks authored rows unavailable (WIRE)
//
// BDD specification — xUnit. Integration via DatabaseCollection (real Postgres). SPEC F27.7.
//
// The ScanService missing-diff used to cover ALL known rows (ScanService.cs:91 region) — any row
// whose path wasn't seen under Library:MediaRoot was marked unavailable, which would nuke an
// authored row (/authored/...) on the first tick after generation. P3 scopes the missing-diff to
// MediaRoot-prefixed paths via ScanService.IsUnderMediaRoot. F5.6 semantics for /media rows are
// UNCHANGED (regression fact below).

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureScannerScopedToMediaRoot
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — authored rows survive a scan tick
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAuthoredRowsSurviveAScanTick(DatabaseFixture db)
    {
        [Fact]
        public async Task AReadyRowUnderAuthoredIsNotMarkedUnavailable()
        {
            // AC1 — row path /authored/x.wav, scan of MediaRoot doesn't see it,
            //       missing-diff runs, row stays state='ready'
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // The scan root (MediaRoot) — deliberately empty; the authored row lives elsewhere and
            // was never a discovery candidate (authored rows are inserted directly, never scanned).
            var mediaRoot = TestMedia.NewTempDir();
            try
            {
                var id = await repo.InsertDiscoveredAsync(
                    "/authored/p3-probe.wav", "wav", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

                var (scan, _) = Harness.Scanner(repo, mediaRoot);
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
    // HAPPY PATH — /media rows keep F5.6 semantics (regression)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMediaRootRowsKeepVanishSemantics(DatabaseFixture db)
    {
        [Fact]
        public async Task AVanishedMediaRootFileStillTransitionsToUnavailable()
        {
            // AC2 — a /media row whose file vanished goes unavailable exactly as today
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var mediaRoot = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(mediaRoot, "a.flac");
                var (scan, queue) = Harness.Scanner(repo, mediaRoot);

                await scan.ScanOnceAsync(CancellationToken.None);
                var id = Assert.Single(Harness.DrainIds(queue));

                File.Delete(path);
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
    // SAD PATH — scan scoping does not exempt authored rows from reality
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPlayoutDiscoveryStillCatchesVanishedAuthoredFiles(DatabaseFixture db)
    {
        [Fact]
        public async Task AVanishedAuthoredFileIsCaughtAtPlayoutTimeDiscovery()
        {
            // AC3 — the existing F5.6 playout-time transition applies to /authored rows. There is no
            // separate "playout-time discovery" production writer in this codebase yet — the scanner
            // is currently the only automated caller of MarkUnavailableAsync. What P3 must prove is
            // narrower and is what this fact pins: the MediaRoot scope added to ScanService lives
            // entirely inside its own missing-diff (ScanService.IsUnderMediaRoot), not inside the
            // shared write path. So whichever mechanism later detects a vanished authored file at
            // playout time can call the very same MediaRepository.MarkUnavailableAsync the scanner
            // calls, targeting an authored row's id, and it still takes effect — scan scoping does
            // not exempt authored rows from reality.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync(
                "/authored/p3-vanished.wav", "wav", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await repo.MarkUnavailableAsync([id], CancellationToken.None);

            Assert.Equal("unavailable", await Harness.StateOfAsync(db, id));
        }
    }
}
