using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests;

/// <summary>Catalog query integration tests (PRD §6, §7, §12) — the IMediaCatalog read seam.</summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class CatalogTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Catalog_GetRandomReady_OnlyReadyMeasurable()
    {
        await fixture.ResetAsync();
        var repo = Harness.Repo(fixture);

        // discovered (not yet enriched) — must never be selected
        await repo.InsertDiscoveredAsync("/media/d.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        // ready but NOT measurable (silent/gated) — must never be selected
        var notMeasurable = await repo.InsertDiscoveredAsync("/media/nm.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(notMeasurable, Harness.ReadyResult(measurable: false), CancellationToken.None);
        // ready + measurable — the only selectable one
        var ok = await repo.InsertDiscoveredAsync("/media/ok.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(ok, Harness.ReadyResult(measurable: true), CancellationToken.None);

        // Transitional scope: T003 adds per-library filtering; for now a non-empty scope passes through.
        var scope = new LibraryScope([1L]);
        var catalog = (IMediaCatalog)repo;
        for (var i = 0; i < 10; i++)
        {
            var reference = await catalog.GetRandomReadyAsync(scope, [], CancellationToken.None);
            Assert.Equal(ok.ToString(), reference?.MediaId);
        }
    }

    [Fact]
    public async Task Catalog_GetRandomReady_ExcludesIds()
    {
        await fixture.ResetAsync();
        var repo = Harness.Repo(fixture);

        var a = await repo.InsertDiscoveredAsync("/media/a.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(a, Harness.ReadyResult(measurable: true), CancellationToken.None);
        var b = await repo.InsertDiscoveredAsync("/media/b.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(b, Harness.ReadyResult(measurable: true), CancellationToken.None);

        // Transitional scope: T003 adds per-library filtering; for now a non-empty scope passes through.
        var scope = new LibraryScope([1L]);
        var catalog = (IMediaCatalog)repo;
        for (var i = 0; i < 10; i++)
        {
            // excluding a ⇒ only b can come back (verifies the `id <> all(@exclude)` array handling)
            var reference = await catalog.GetRandomReadyAsync(scope, [a.ToString()], CancellationToken.None);
            Assert.Equal(b.ToString(), reference?.MediaId);
        }
    }

    [Fact]
    public async Task Catalog_GetById_RoundTrips()
    {
        await fixture.ResetAsync();
        var repo = Harness.Repo(fixture);

        var id = await repo.InsertDiscoveredAsync("/media/x.flac", "flac", 123, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, new EnrichmentResult(
            DurationMs: 180_000, SampleRate: 44_100, Channels: 2, BitrateKbps: 1000,
            Title: "T", Artist: "A", Album: "Al", AlbumArtist: "AA", Genre: "G", TrackNo: 5, Year: 2019,
            IntegratedLufs: -14.2, TruePeakDbtp: -1.3, Measurable: true,
            CueInSec: null, CueOutSec: null, CueAnalyzedAt: DateTime.UtcNow,
            IntroEnergy: null, OutroEnergy: null, EnergyAnalyzedAt: DateTime.UtcNow,
            Bpm: null, BpmAnalyzedAt: DateTime.UtcNow), CancellationToken.None);

        // Transitional scope: T003 adds per-library filtering; for now a non-empty scope passes through.
        var reference = await ((IMediaCatalog)repo).GetByIdAsync(new LibraryScope([1L]), id.ToString(), CancellationToken.None);

        Assert.NotNull(reference);
        Assert.Equal("/media/x.flac", reference!.Locator);
        Assert.Equal("T", reference.Title);
        Assert.Equal("A", reference.Artist);
        Assert.Equal("Al", reference.Album);
        Assert.Equal("G", reference.Genre);
        Assert.Equal(180_000, reference.DurationMs);
        Assert.Equal(44_100, reference.SampleRate);
        Assert.Equal((short)2, reference.Channels);
        Assert.Equal(1000, reference.BitrateKbps);
        Assert.Equal(2019, reference.Year);
        Assert.Equal(-14.2, reference.Loudness.IntegratedLufs, 3);
        Assert.Equal(-1.3, reference.Loudness.TruePeakDbtp, 3);
        Assert.True(reference.Loudness.Measurable);
    }

    [Fact]
    public async Task Catalog_GetById_Unknown_ReturnsNull()
    {
        await fixture.ResetAsync();
        var reference = await ((IMediaCatalog)Harness.Repo(fixture)).GetByIdAsync(new LibraryScope([1L]), "999999", CancellationToken.None);
        Assert.Null(reference);
    }
}
