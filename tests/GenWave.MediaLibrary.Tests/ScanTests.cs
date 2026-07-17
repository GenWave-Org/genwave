namespace GenWave.MediaLibrary.Tests;

/// <summary>Discovery integration tests (PRD §5.1, §12): new / unchanged / changed / missing.</summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class ScanTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Scan_NewFiles_Discovered()
    {
        await fixture.ResetAsync();
        var dir = TestMedia.NewTempDir();
        try
        {
            TestMedia.CreateTone(dir, "a.flac");
            TestMedia.CreateTone(dir, "b.mp3");

            var repo = Harness.Repo(fixture);
            var (scan, queue) = Harness.Scanner(repo, dir);

            await scan.ScanOnceAsync(CancellationToken.None);

            var fingerprints = await repo.ListFingerprintsAsync(CancellationToken.None);
            Assert.Equal(2, fingerprints.Count);
            Assert.All(fingerprints, f => Assert.Equal("discovered", f.State));
            Assert.Equal(2, Harness.DrainIds(queue).Count);   // both enqueued for enrichment
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Scan_UnchangedFiles_Skipped()
    {
        await fixture.ResetAsync();
        var dir = TestMedia.NewTempDir();
        try
        {
            TestMedia.CreateTone(dir, "a.flac");
            var repo = Harness.Repo(fixture);
            var (scan, queue) = Harness.Scanner(repo, dir);

            await scan.ScanOnceAsync(CancellationToken.None);
            Assert.Single(Harness.DrainIds(queue));            // first scan: discovered + enqueued

            await scan.ScanOnceAsync(CancellationToken.None);
            Assert.Empty(Harness.DrainIds(queue));             // second scan: unchanged ⇒ nothing enqueued
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Scan_ChangedMtime_ReEnriched()
    {
        await fixture.ResetAsync();
        var dir = TestMedia.NewTempDir();
        try
        {
            var path = TestMedia.CreateTone(dir, "a.flac");
            var repo = Harness.Repo(fixture);
            var (scan, queue) = Harness.Scanner(repo, dir);

            await scan.ScanOnceAsync(CancellationToken.None);
            var id = Assert.Single(Harness.DrainIds(queue));

            // bump mtime to a clearly different second
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(10));

            await scan.ScanOnceAsync(CancellationToken.None);

            Assert.Equal(id, Assert.Single(Harness.DrainIds(queue)));   // same row re-enqueued
            Assert.Equal("discovered", await Harness.StateOfAsync(fixture, id));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Scan_MissingFile_MarkedUnavailable()
    {
        await fixture.ResetAsync();
        var dir = TestMedia.NewTempDir();
        try
        {
            var path = TestMedia.CreateTone(dir, "a.flac");
            var repo = Harness.Repo(fixture);
            var (scan, queue) = Harness.Scanner(repo, dir);

            await scan.ScanOnceAsync(CancellationToken.None);
            var id = Assert.Single(Harness.DrainIds(queue));

            File.Delete(path);
            await scan.ScanOnceAsync(CancellationToken.None);

            Assert.Equal("unavailable", await Harness.StateOfAsync(fixture, id));   // not hard-deleted
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
