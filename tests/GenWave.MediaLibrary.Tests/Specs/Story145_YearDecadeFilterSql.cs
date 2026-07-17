// STORY-145 (repo half) — GET /api/media year/decade/year-missing filters + bpm/trackEnergy
// projection (Epic X / SPEC F49.1–F49.2, closes gitea-#190, gitea-#208).
//
// BDD specification — xUnit. Integration via DatabaseCollection (real Postgres). Mirrors
// StoryF3_BulkEligibilityByFilter.cs's ListAdminAsync filter facts (state/artist/genre/q/eligible)
// for the three new year predicates — the WHERE-clause composition itself deserves real-Postgres
// coverage like its siblings, since the Host-level wiring specs
// (Host.Tests/Specs/Story145_YearDecadeFiltersAndSignalDto.cs) exercise only an in-process fake.
//
// Covers:
//   • ListAdminAsync respects ?year= (exact match).
//   • ListAdminAsync respects ?decade= (year BETWEEN start AND start+9).
//   • ListAdminAsync respects ?year-missing=true (year IS NULL).
//   • A decade filter composes with the pre-existing artist filter (same WHERE, AND-ed).
//   • ListAdminAsync's projection carries bpm + track_energy (SPEC F49.2).

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureYearDecadeFilterSql
{
    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — year/decade/year-missing narrow the browse
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioYearFilterMatchesExactly(DatabaseFixture db)
    {
        [Fact]
        public async Task ListAdminWithYearReturnsOnlyThatYear()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var matchId = await repo.InsertDiscoveredAsync("/media/year-exact-match.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(matchId, Harness.ReadyResultWith(year: 1975), CancellationToken.None);

            var otherId = await repo.InsertDiscoveredAsync("/media/year-exact-other.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(otherId, Harness.ReadyResultWith(year: 1980), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(Year: 1975), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(matchId.ToString(), result.Items[0].MediaId);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDecadeFilterReturnsTheTenYearSpan(DatabaseFixture db)
    {
        [Fact]
        public async Task ListAdminWithDecadeReturnsYearsInThatSpanOnly()
        {
            // decade=1970 -> year BETWEEN 1970 AND 1979: 1969 and 1980 are just outside the span.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var beforeId = await repo.InsertDiscoveredAsync("/media/decade-1969.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(beforeId, Harness.ReadyResultWith(year: 1969), CancellationToken.None);

            var startId = await repo.InsertDiscoveredAsync("/media/decade-1970.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(startId, Harness.ReadyResultWith(year: 1970), CancellationToken.None);

            var endId = await repo.InsertDiscoveredAsync("/media/decade-1979.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(endId, Harness.ReadyResultWith(year: 1979), CancellationToken.None);

            var afterId = await repo.InsertDiscoveredAsync("/media/decade-1980.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(afterId, Harness.ReadyResultWith(year: 1980), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(Decade: 1970), CancellationToken.None);

            var ids = result.Items.Select(r => r.MediaId).OrderBy(x => x).ToList();
            var expected = new[] { startId.ToString(), endId.ToString() }.OrderBy(x => x);
            Assert.Equal(expected, ids);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioYearMissingFilter(DatabaseFixture db)
    {
        [Fact]
        public async Task ListAdminWithYearMissingReturnsOnlyNullYearRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var missingId = await repo.InsertDiscoveredAsync("/media/year-missing-null.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(missingId, Harness.ReadyResultWith(year: null), CancellationToken.None);

            var filledId = await repo.InsertDiscoveredAsync("/media/year-missing-filled.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(filledId, Harness.ReadyResultWith(year: 2001), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(YearMissing: true), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(missingId.ToString(), result.Items[0].MediaId);
        }

        [Fact]
        public async Task ListAdminWithYearMissingAbsentAppliesNoFilter()
        {
            // Mirrors never-play's documented semantics: absent applies no filter (both rows return).
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var missingId = await repo.InsertDiscoveredAsync("/media/year-missing-absent-null.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(missingId, Harness.ReadyResultWith(year: null), CancellationToken.None);

            var filledId = await repo.InsertDiscoveredAsync("/media/year-missing-absent-filled.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(filledId, Harness.ReadyResultWith(year: 2001), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(), CancellationToken.None);

            Assert.Equal(2, result.Total);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // COMPOSITION — a year filter combines (AND) with the pre-existing filter set
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioDecadeComposesWithArtistFilter(DatabaseFixture db)
    {
        [Fact]
        public async Task DecadeAndArtistFilterIntersect()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var matchId = await repo.InsertDiscoveredAsync("/media/decade-artist-match.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(matchId, Harness.ReadyResultWith(artist: "Alpha", year: 1975), CancellationToken.None);

            var wrongArtistId = await repo.InsertDiscoveredAsync("/media/decade-artist-wrong-artist.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(wrongArtistId, Harness.ReadyResultWith(artist: "Beta", year: 1975), CancellationToken.None);

            var wrongDecadeId = await repo.InsertDiscoveredAsync("/media/decade-artist-wrong-decade.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(wrongDecadeId, Harness.ReadyResultWith(artist: "Alpha", year: 1985), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(Artist: "Alpha", Decade: 1970), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(matchId.ToString(), result.Items[0].MediaId);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PROJECTION — bpm + track_energy ride the admin projection (SPEC F49.2)
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioProjectionCarriesBpmAndTrackEnergy(DatabaseFixture db)
    {
        [Fact]
        public async Task ListAdminCarriesBpmAndTheGeneratedTrackEnergyColumn()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // ReadyResultWith measures at -14.0 LUFS -> track_energy = clamp((-14+36)/30, 0, 1) = 0.7333...
            var id = await repo.InsertDiscoveredAsync("/media/signal-projection.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(), CancellationToken.None);
            await repo.WriteBpmClaimAsync(id, 128.4, CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(), CancellationToken.None);

            var row = Assert.Single(result.Items, r => r.MediaId == id.ToString());
            Assert.Equal(128.4, row.Bpm);
            Assert.NotNull(row.TrackEnergy);
            Assert.Equal((-14.0 + 36.0) / 30.0, row.TrackEnergy!.Value, precision: 10);
        }

        [Fact]
        public async Task AnUnanalyzedRowCarriesNullBpmAndNullTrackEnergy()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // A discovered (never-enriched) row: bpm and integrated_lufs (hence track_energy) are
            // both still null.
            var id = await repo.InsertDiscoveredAsync("/media/signal-projection-unanalyzed.flac", "flac", 1, Harness.Mtime, CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(), CancellationToken.None);

            var row = Assert.Single(result.Items, r => r.MediaId == id.ToString());
            Assert.Null(row.Bpm);
            Assert.Null(row.TrackEnergy);
        }
    }
}
