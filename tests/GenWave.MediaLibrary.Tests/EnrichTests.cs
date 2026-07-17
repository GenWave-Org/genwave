using GenWave.Core.Abstractions;
using GenWave.Core.Domain;

namespace GenWave.MediaLibrary.Tests;

/// <summary>Enrichment integration tests (PRD §8, §12): real loudness + tags, and failure isolation.</summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class EnrichTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Enrich_PopulatesLoudnessAndTags()
    {
        await fixture.ResetAsync();
        var dir = TestMedia.NewTempDir();
        try
        {
            var path = TestMedia.CreateTone(dir, "song.flac",
                title: "My Song", artist: "The Artist", album: "The Album", genre: "Rock", year: 2021);

            var repo = Harness.Repo(fixture);
            var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

            await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

            Assert.Equal("ready", await Harness.StateOfAsync(fixture, id));

            // Transitional scope: T003 adds per-library filtering; for now a non-empty scope passes through.
            var scope = new LibraryScope([1L]);
            var reference = await ((IMediaCatalog)repo).GetByIdAsync(scope, id.ToString(), CancellationToken.None);
            Assert.NotNull(reference);
            Assert.True(reference!.Loudness.Measurable);
            Assert.True(reference.DurationMs is > 1500 and < 2500);   // a ~2s tone
            Assert.Equal(44_100, reference.SampleRate);
            Assert.Equal((short)2, reference.Channels);
            Assert.Equal("My Song", reference.Title);
            Assert.Equal("The Artist", reference.Artist);
            Assert.Equal("The Album", reference.Album);
            Assert.Equal("Rock", reference.Genre);
            Assert.Equal(2021, reference.Year);

            // ready + measurable ⇒ now selectable
            var random = await ((IMediaCatalog)repo).GetRandomReadyAsync(scope, [], CancellationToken.None);
            Assert.Equal(id.ToString(), random?.MediaId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Enrich_CorruptFile_Failed_NotCrash()
    {
        await fixture.ResetAsync();
        var dir = TestMedia.NewTempDir();
        try
        {
            var path = TestMedia.CreateCorrupt(dir, "bad.mp3");
            var repo = Harness.Repo(fixture);
            var id = await repo.InsertDiscoveredAsync(path, "mp3", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

            // must NOT throw — a per-file failure is isolated.
            await Harness.Enrichment(repo).EnrichOneAsync(id, CancellationToken.None);

            Assert.Equal("failed", await Harness.StateOfAsync(fixture, id));
            Assert.Null(await ((IMediaCatalog)repo).GetRandomReadyAsync(new LibraryScope([1L]), [], CancellationToken.None));   // not selectable
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
