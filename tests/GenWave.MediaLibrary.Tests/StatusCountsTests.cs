using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests;

/// <summary>
/// Integration coverage for <see cref="MediaRepository.GetStatusCountsAsync"/> (SPEC F28.6) — the
/// <c>GET /api/status</c> catalog aggregate. Added after a smoke-test finding: Dapper's constructor
/// materialization requires the projected column types to match <see cref="CatalogStatusCounts"/>'s
/// <c>int</c> parameters exactly, but Postgres' <c>count(*)</c> is <c>bigint</c> — every prior spec
/// exercised this query through <c>FakeMediaCatalog</c>, so the real SQL/materialization path had
/// zero coverage and the mismatch only surfaced against a real Postgres. This class proves the fix
/// (explicit <c>::int</c> casts) against the real <c>library</c> schema.
/// </summary>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "Integration")]
public class StatusCountsTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetStatusCounts_AggregatesEveryStateAndScopesPlayableToSafeScope()
    {
        await fixture.ResetAsync();
        var repo = Harness.Repo(fixture);

        // A second library so the "in-scope vs out-of-scope" playable predicate has something to exclude.
        await using var setupConn = await fixture.DataSource.OpenConnectionAsync();
        var otherLibraryId = await setupConn.ExecuteScalarAsync<long>(
            "insert into library.library (name) values ('OtherLibrary') returning id");

        // ready + measurable + eligible, library 1 (in safeScope) — the only row that should count
        // towards `playable`.
        var playable = await repo.InsertDiscoveredAsync("/media/playable.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(playable, Harness.ReadyResult(measurable: true), CancellationToken.None);

        // ready + measurable but NOT eligible, library 1 — counts as `ready`, never `playable`.
        var notEligible = await repo.InsertDiscoveredAsync("/media/not-eligible.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(notEligible, Harness.ReadyResult(measurable: true), CancellationToken.None);
        await setupConn.ExecuteAsync("update library.media set eligible = false where id = @id", new { id = notEligible });

        // ready but NOT measurable, library 1 — counts as `ready`, never `playable`.
        var notMeasurable = await repo.InsertDiscoveredAsync("/media/not-measurable.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(notMeasurable, Harness.ReadyResult(measurable: false), CancellationToken.None);

        // ready + measurable + eligible, but in a library OUTSIDE the safeScope — counts as `ready`
        // (state counts are unscoped, mirroring GET /api/libraries) but never `playable`.
        var outOfScope = await setupConn.ExecuteScalarAsync<long>(
            "insert into library.media (path, format, size_bytes, mtime, library_id) " +
            "values ('/media/out-of-scope.flac', 'flac', 1, @mtime, @otherLibraryId) returning id",
            new { mtime = Harness.Mtime, otherLibraryId });
        await repo.WriteEnrichmentAsync(outOfScope, Harness.ReadyResult(measurable: true), CancellationToken.None);

        // discovered (never enriched) — surfaced to operators as `enriching`.
        await repo.InsertDiscoveredAsync("/media/discovered.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.InsertDiscoveredAsync("/media/discovered-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);

        // failed.
        var failedId = await repo.InsertDiscoveredAsync("/media/failed.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.MarkFailedAsync(failedId, CancellationToken.None);

        // unavailable.
        var unavailableId = await repo.InsertDiscoveredAsync("/media/unavailable.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.MarkUnavailableAsync([unavailableId], CancellationToken.None);

        var safeScope = new LibraryScope([1L]);
        var result = await repo.GetStatusCountsAsync(safeScope, CancellationToken.None);

        // 4 rows reach state='ready': playable, notEligible, notMeasurable, outOfScope.
        Assert.Equal(4, result.Ready);
        Assert.Equal(2, result.Enriching);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Unavailable);
        // Only `playable` (ready + measurable + eligible + library_id in {1}) counts.
        Assert.Equal(1, result.Playable);
    }

    [Fact]
    public async Task GetStatusCounts_EmptySafeScope_PlayableIsZeroNotAnException()
    {
        await fixture.ResetAsync();
        var repo = Harness.Repo(fixture);

        // A fully playable row exists — proves the empty scope suppresses `playable` via the
        // library_id = any('{}') predicate rather than there being nothing to find.
        var id = await repo.InsertDiscoveredAsync("/media/depleted-safescope.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

        var result = await repo.GetStatusCountsAsync(LibraryScope.None, CancellationToken.None);

        Assert.Equal(1, result.Ready);
        Assert.Equal(0, result.Enriching);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, result.Unavailable);
        Assert.Equal(0, result.Playable);
    }
}
