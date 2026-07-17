// STORY-155 — A mount blip never sidelines the library (Epic Z / SPEC F58, closes gitea-#223).
//
// BDD specification — xUnit. Implemented Z2 (2026-07-15). ScanService gains per-path in-memory
// consecutive-miss counters: ready→unavailable only at Library:Scan:MissThreshold (default 2)
// consecutive listing misses; a sighting resets; threshold 1 reproduces today's single-miss
// behavior; a restart resets counts (defers, never accelerates). Idiom mirrors the existing
// ScanService specs (real Postgres via DatabaseFixture, real files via TestMedia — no fake
// repository seam exists for ScanService, which binds to the concrete MediaRepository).

using GenWave.MediaLibrary.Scan;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureScanAvailabilityGrace
{
    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — the grace window
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOneMissDefersNMissesFlip(DatabaseFixture db)
    {
        [Fact]
        public async Task ASingleMissLeavesTheRowAvailable()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "a.flac");
                var (scan, queue) = Harness.Scanner(repo, dir, missThreshold: 2);

                await scan.ScanOnceAsync(CancellationToken.None);
                var id = Assert.Single(Harness.DrainIds(queue));

                File.Delete(path);
                await scan.ScanOnceAsync(CancellationToken.None);   // miss 1 of 2 — deferred

                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task TheThresholdthConsecutiveMissFlipsTheRowUnavailable()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "a.flac");
                var (scan, queue) = Harness.Scanner(repo, dir, missThreshold: 2);

                await scan.ScanOnceAsync(CancellationToken.None);
                var id = Assert.Single(Harness.DrainIds(queue));

                File.Delete(path);
                await scan.ScanOnceAsync(CancellationToken.None);   // miss 1 of 2 — deferred
                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));

                await scan.ScanOnceAsync(CancellationToken.None);   // miss 2 of 2 — flips, exactly as today

                Assert.Equal("unavailable", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task TheFirstDeferredMissIsLogged()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "a.flac");
                var capturingLogger = new CapturingLogger<ScanService>();
                var (scan, queue) = Harness.Scanner(repo, dir, missThreshold: 2, logger: capturingLogger);

                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Single(Harness.DrainIds(queue));

                File.Delete(path);
                await scan.ScanOnceAsync(CancellationToken.None);   // the first deferred miss

                Assert.Contains(capturingLogger.Informational, m => m.Contains(path, StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioASightingResetsTheCount(DatabaseFixture db)
    {
        [Fact]
        public async Task MissSeenMissDoesNotFlipAtThresholdTwo()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "a.flac");
                var (scan, queue) = Harness.Scanner(repo, dir, missThreshold: 2);

                await scan.ScanOnceAsync(CancellationToken.None);
                var id = Assert.Single(Harness.DrainIds(queue));

                File.Delete(path);
                await scan.ScanOnceAsync(CancellationToken.None);   // miss 1 of 2 — deferred
                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));

                // A sighting resets the count — same path reappears (a slightly different tone, so
                // this also exercises the ordinary "changed" re-discovery path, not just the counter).
                TestMedia.CreateTone(dir, "a.flac", seconds: 1.0);
                await scan.ScanOnceAsync(CancellationToken.None);   // seen — counter resets to zero
                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));

                File.Delete(path);
                await scan.ScanOnceAsync(CancellationToken.None);   // miss 1 of 2 again — still deferred

                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioThresholdOneReproducesToday(DatabaseFixture db)
    {
        [Fact]
        public async Task ThresholdOneFlipsOnTheFirstMiss()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "a.flac");
                var (scan, queue) = Harness.Scanner(repo, dir, missThreshold: 1);

                await scan.ScanOnceAsync(CancellationToken.None);
                var id = Assert.Single(Harness.DrainIds(queue));

                File.Delete(path);
                await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Equal("unavailable", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioARestartOnlyDefers(DatabaseFixture db)
    {
        [Fact]
        public async Task AFreshServiceInstanceStartsEveryCounterAtZero()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "a.flac");
                var (scanBeforeRestart, queue) = Harness.Scanner(repo, dir, missThreshold: 2);

                await scanBeforeRestart.ScanOnceAsync(CancellationToken.None);
                var id = Assert.Single(Harness.DrainIds(queue));

                File.Delete(path);
                await scanBeforeRestart.ScanOnceAsync(CancellationToken.None);   // miss 1 of 2 — deferred
                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));

                // Simulate a process restart: a brand-new ScanService instance against the SAME repo
                // and media root. Its in-memory counter starts at zero — if it did not, the very next
                // tick would already be this path's SECOND miss overall and would flip immediately.
                var (scanAfterRestart, _) = Harness.Scanner(repo, dir, missThreshold: 2);

                await scanAfterRestart.ScanOnceAsync(CancellationToken.None);   // miss 1 of 2 (fresh instance) — still deferred
                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));

                await scanAfterRestart.ScanOnceAsync(CancellationToken.None);   // miss 2 of 2 (fresh instance) — flips: deferred, never lost

                Assert.Equal("unavailable", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task RediscoveryOfAnUnavailableRowIsUnchanged()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "a.flac", seconds: 2.0);
                var (scan, queue) = Harness.Scanner(repo, dir, missThreshold: 1);

                await scan.ScanOnceAsync(CancellationToken.None);
                var id = Assert.Single(Harness.DrainIds(queue));

                File.Delete(path);
                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Equal("unavailable", await Harness.StateOfAsync(db, id));

                // Rediscovery: the SAME path reappears with a different size (mtime-change detection,
                // untouched by the F58 grace counter — that logic lives entirely in the missing-diff,
                // never in the per-file new/changed/unchanged branch below it).
                TestMedia.CreateTone(dir, "a.flac", seconds: 5.0);
                await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task AuthoredRowsOutsideMediaRootAreStillNeverMissDiffed()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // The scan root (MediaRoot) is deliberately empty; the authored row lives elsewhere and
            // was never a discovery candidate (F27.7/F27.8) — it must never enter the consecutive-miss
            // counter at all, so it survives arbitrarily many ticks past any threshold untouched.
            var mediaRoot = TestMedia.NewTempDir();
            try
            {
                var id = await repo.InsertDiscoveredAsync(
                    "/authored/z2-probe.wav", "wav", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

                var (scan, _) = Harness.Scanner(repo, mediaRoot, missThreshold: 2);

                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Equal("ready", await Harness.StateOfAsync(db, id));

                await scan.ScanOnceAsync(CancellationToken.None);   // past the threshold — still untouched
                Assert.Equal("ready", await Harness.StateOfAsync(db, id));

                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Equal("ready", await Harness.StateOfAsync(db, id));
            }
            finally
            {
                Directory.Delete(mediaRoot, recursive: true);
            }
        }
    }
}
