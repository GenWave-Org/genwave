// gh-#112 — Scan never resurrects an unavailable row when the file returns with an unchanged
// fingerprint.
//
// BDD specification — xUnit. The live repro (demo box, 2026-07-23, the media bind-mount saga): a
// file restored at its old path with size+mtime preserved (mv back from a backup, rsync -a, tar)
// was classified *unchanged* and skipped — the unchanged branch never consulted `state`, so the
// row stayed `unavailable` forever and only touch(1) could revive it. A sighted path whose row is
// `unavailable` now takes the changed branch regardless of fingerprint: reset to discovered,
// re-enqueued for enrichment, ratings/history intact (MarkDiscoveredAsync's existing contract).
//
// Idiom mirrors Story155_ScanAvailabilityGrace (real Postgres via DatabaseFixture, real files via
// TestMedia — no fake repository seam exists for ScanService, which binds to the concrete
// MediaRepository).

using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureScanResurrection
{
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFileReturnsWithIdenticalFingerprint(DatabaseFixture db)
    {
        [Fact]
        public async Task The_returned_file_is_rediscovered_and_reenriched_not_skipped_as_unchanged()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            var parking = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "a.flac", seconds: 2.0);
                var parked = Path.Combine(parking, "a.flac");
                var (scan, queue) = Harness.Scanner(repo, dir, missThreshold: 1);

                await scan.ScanOnceAsync(CancellationToken.None);
                var id = Assert.Single(Harness.DrainIds(queue));

                // Gone → unavailable (threshold 1 flips on the first miss).
                File.Move(path, parked);
                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Equal("unavailable", await Harness.StateOfAsync(db, id));

                // Restored at the SAME path — File.Move preserves size AND mtime, the exact
                // fingerprint-identical shape the unchanged branch used to swallow (gh-#112).
                File.Move(parked, path);
                await scan.ScanOnceAsync(CancellationToken.None);

                // Then the row resurrects: discovered again, re-enqueued for enrichment.
                Assert.Equal("discovered", await Harness.StateOfAsync(db, id));
                Assert.Equal(id, Assert.Single(Harness.DrainIds(queue)));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
                Directory.Delete(parking, recursive: true);
            }
        }

        [Fact]
        public async Task An_available_unchanged_row_is_still_skipped_and_never_reenqueued()
        {
            // The regression guard for the fix itself: the resurrection clause must not widen the
            // changed branch for healthy rows — an untouched, available file stays the no-op
            // common case (opens nothing, enqueues nothing).
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var dir = TestMedia.NewTempDir();
            try
            {
                TestMedia.CreateTone(dir, "a.flac", seconds: 2.0);
                var (scan, queue) = Harness.Scanner(repo, dir, missThreshold: 1);

                await scan.ScanOnceAsync(CancellationToken.None);
                Assert.Single(Harness.DrainIds(queue));

                await scan.ScanOnceAsync(CancellationToken.None);

                Assert.Empty(Harness.DrainIds(queue));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
