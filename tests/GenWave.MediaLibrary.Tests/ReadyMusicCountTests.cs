using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests;

/// <summary>
/// Integration coverage for <see cref="MediaRepository.GetReadyMusicCountAsync"/> (SPEC F207.1,
/// STORY-474, PLAN T561) — the About page's own "Tracks in the library" figure. Proven against real
/// Postgres, mirroring <c>StatusCountsTests</c>'s own rationale: this is a plain scalar count, but
/// that class exists precisely because a query exercised only through a fake never caught Dapper's
/// materialization requirements against the real driver.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class ReadyMusicCountTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetReadyMusicCount_CountsReadyRowsWithNoImagingKindOnly()
    {
        await fixture.ResetAsync();
        var repo = Harness.Repo(fixture);

        // Two genuinely ready MUSIC rows (state='ready', imaging_kind is null) — these count.
        var trackOne = await repo.InsertDiscoveredAsync("/media/track-one.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(trackOne, Harness.ReadyResult(measurable: true), CancellationToken.None);
        var trackTwo = await repo.InsertDiscoveredAsync("/media/track-two.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(trackTwo, Harness.ReadyResult(measurable: true), CancellationToken.None);

        // Discovered (never enriched) — not 'ready' yet, never counted.
        await repo.InsertDiscoveredAsync("/media/discovered.flac", "flac", 1, Harness.Mtime, CancellationToken.None);

        // Authored imaging rows land directly in state='ready' (F27.1) with imaging_kind stamped —
        // the SAME imaging_kind is null fence PlayablePredicate uses to keep this content out of
        // music rotation must keep it out of "tracks in the library" too.
        await repo.InsertAuthoredAsync(
            Harness.AuthoredInsert(path: "/authored/liner.wav", kind: ImagingKind.Liner), CancellationToken.None);
        await repo.InsertAuthoredAsync(
            Harness.AuthoredInsert(path: "/authored/station-id.wav", kind: ImagingKind.StationId), CancellationToken.None);
        await repo.InsertAuthoredAsync(
            Harness.AuthoredInsert(path: "/authored/ad.wav", kind: ImagingKind.Ad), CancellationToken.None);

        var count = await repo.GetReadyMusicCountAsync(CancellationToken.None);

        Assert.Equal(2, count);
    }
}
