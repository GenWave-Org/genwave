// STORY-148 — Eligibility curation by exact artist, album, and genre (Epic Y / SPEC F52.1–F52.4,
// closes gitea-#189) — SQL half. The Core contract lives in Core.Tests/Specs/Story148_FacetQueryContract.cs;
// the API half in Host.Tests/Specs/Story148_FacetsEndpointAndExactParams.cs; the UI half in
// admin-ui/__specs__/catalog-facet-pickers.spec.tsx.
//
// BDD specification — xUnit. Integration via DatabaseCollection (real Postgres). Mirrors
// StoryF3/Story145's ListAdminAsync filter facts (state/artist/genre/q/eligible/year) for the new
// facets query and exact-match predicates.
// House rule F52.4: exact predicates land ONCE in BuildAdminWhere — browse and every bulk path
// inherit them; a second predicate implementation fails review.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureFacetsAndExactFilterSql
{
    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — facets enumerate, exact filters match exactly
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFacetsEnumerateDistinctValues(DatabaseFixture db)
    {
        [Fact]
        public async Task DistinctArtistsReturnWithRowCounts()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var alpha1 = await repo.InsertDiscoveredAsync("/media/facet-artist-alpha-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(alpha1, Harness.ReadyResultWith(artist: "ArtistAlpha"), CancellationToken.None);
            var alpha2 = await repo.InsertDiscoveredAsync("/media/facet-artist-alpha-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(alpha2, Harness.ReadyResultWith(artist: "ArtistAlpha"), CancellationToken.None);

            var beta = await repo.InsertDiscoveredAsync("/media/facet-artist-beta.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(beta, Harness.ReadyResultWith(artist: "ArtistBeta"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var facets = await repo.GetFacetsAsync(FacetField.Artist, scope, CancellationToken.None);

            var alphaFacet = Assert.Single(facets, f => f.Value == "ArtistAlpha");
            Assert.Equal(2, alphaFacet.Count);
            var betaFacet = Assert.Single(facets, f => f.Value == "ArtistBeta");
            Assert.Equal(1, betaFacet.Count);
        }

        [Fact]
        public async Task MixedCaseVariantsGroupIntoOneEntryWithTheGroupTotalCount()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var lower = await repo.InsertDiscoveredAsync("/media/facet-case-lower.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(lower, Harness.ReadyResultWith(artist: "rock band"), CancellationToken.None);
            var upper = await repo.InsertDiscoveredAsync("/media/facet-case-upper.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(upper, Harness.ReadyResultWith(artist: "ROCK BAND"), CancellationToken.None);
            var mixed = await repo.InsertDiscoveredAsync("/media/facet-case-mixed.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(mixed, Harness.ReadyResultWith(artist: "Rock Band"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var facets = await repo.GetFacetsAsync(FacetField.Artist, scope, CancellationToken.None);

            // One entry for the whole case-insensitive group, carrying the group TOTAL (3), not a
            // divided per-casing count — the representative Value is one of the three original
            // casings (GetFacetsAsync picks it deterministically via min(), documented on the method).
            var group = Assert.Single(facets, f => f.Value.Equals("rock band", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(3, group.Count);
        }

        [Fact]
        public async Task ValuesOrderCaseInsensitively()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var cherry = await repo.InsertDiscoveredAsync("/media/facet-order-cherry.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(cherry, Harness.ReadyResultWith(artist: "cherry"), CancellationToken.None);
            var apple = await repo.InsertDiscoveredAsync("/media/facet-order-apple.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(apple, Harness.ReadyResultWith(artist: "Apple"), CancellationToken.None);
            var banana = await repo.InsertDiscoveredAsync("/media/facet-order-banana.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(banana, Harness.ReadyResultWith(artist: "BANANA"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var facets = await repo.GetFacetsAsync(FacetField.Artist, scope, CancellationToken.None);

            var ordered = facets
                .Where(f => new[] { "cherry", "apple", "banana" }.Contains(f.Value, StringComparer.OrdinalIgnoreCase))
                .Select(f => f.Value.ToLowerInvariant())
                .ToList();
            Assert.Equal(["apple", "banana", "cherry"], ordered);
        }

        [Fact]
        public async Task NullAndBlankValuesAreExcluded()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var realId = await repo.InsertDiscoveredAsync("/media/facet-blank-real.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(realId, Harness.ReadyResultWith(artist: "RealArtist"), CancellationToken.None);

            var nullId = await repo.InsertDiscoveredAsync("/media/facet-blank-null.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(nullId, Harness.ReadyResultWith(artist: "Placeholder"), CancellationToken.None);
            var blankId = await repo.InsertDiscoveredAsync("/media/facet-blank-whitespace.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(blankId, Harness.ReadyResultWith(artist: "Placeholder"), CancellationToken.None);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            await connSetup.ExecuteAsync("update library.media set artist = null where id = @id", new { id = nullId });
            await connSetup.ExecuteAsync("update library.media set artist = '   ' where id = @id", new { id = blankId });

            var scope = new LibraryScope([1L]);
            var facets = await repo.GetFacetsAsync(FacetField.Artist, scope, CancellationToken.None);

            Assert.Contains(facets, f => f.Value == "RealArtist");
            Assert.DoesNotContain(facets, f => f.Value is null or "" || f.Value.Trim() == "");
        }

        [Fact]
        public async Task AlbumAndGenreFieldsFacetIdentically()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id1 = await repo.InsertDiscoveredAsync("/media/facet-field-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id1, Harness.ReadyResultWith(genre: "Jazz"), CancellationToken.None);
            var id2 = await repo.InsertDiscoveredAsync("/media/facet-field-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id2, Harness.ReadyResultWith(genre: "Jazz"), CancellationToken.None);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            await connSetup.ExecuteAsync("update library.media set album = 'Kind of Blue' where id = @id", new { id = id1 });
            await connSetup.ExecuteAsync("update library.media set album = 'Kind of Blue' where id = @id", new { id = id2 });

            var scope = new LibraryScope([1L]);

            var genreFacets = await repo.GetFacetsAsync(FacetField.Genre, scope, CancellationToken.None);
            var jazz = Assert.Single(genreFacets, f => f.Value == "Jazz");
            Assert.Equal(2, jazz.Count);

            var albumFacets = await repo.GetFacetsAsync(FacetField.Album, scope, CancellationToken.None);
            var album = Assert.Single(albumFacets, f => f.Value == "Kind of Blue");
            Assert.Equal(2, album.Count);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFacetScopeMirrorsBrowse(DatabaseFixture db)
    {
        [Fact]
        public async Task OutOfScopeRowsContributeNothing()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            var lib2Id = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.library (name) values ('FacetScopeLib2') returning id");

            var inScopeId = await repo.InsertDiscoveredAsync("/media/facet-scope-in.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(inScopeId, Harness.ReadyResultWith(artist: "InScopeArtist"), CancellationToken.None);

            var outScopeId = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.media (path, format, size_bytes, mtime, library_id) " +
                "values ('/media/facet-scope-out.flac', 'flac', 1, @mtime, @lib2Id) returning id",
                new { mtime = Harness.Mtime, lib2Id });
            await repo.WriteEnrichmentAsync(outScopeId, Harness.ReadyResultWith(artist: "OutOfScopeArtist"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var facets = await repo.GetFacetsAsync(FacetField.Artist, scope, CancellationToken.None);

            Assert.Contains(facets, f => f.Value == "InScopeArtist");
            Assert.DoesNotContain(facets, f => f.Value == "OutOfScopeArtist");
        }

        [Fact]
        public async Task ANamedLibraryScopeReturnsOnlyThatLibrarysValues()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            var lib2Id = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.library (name) values ('FacetNamedLib2') returning id");

            var lib1Id = await repo.InsertDiscoveredAsync("/media/facet-named-lib1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(lib1Id, Harness.ReadyResultWith(artist: "Lib1Artist"), CancellationToken.None);

            var lib2MediaId = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.media (path, format, size_bytes, mtime, library_id) " +
                "values ('/media/facet-named-lib2.flac', 'flac', 1, @mtime, @lib2Id) returning id",
                new { mtime = Harness.Mtime, lib2Id });
            await repo.WriteEnrichmentAsync(lib2MediaId, Harness.ReadyResultWith(artist: "Lib2Artist"), CancellationToken.None);

            // Effective scope narrowed to library 2 only (the F23.3 named-library swap, already
            // resolved to a LibraryScope by the controller before reaching this seam).
            var namedScope = new LibraryScope([lib2Id]);
            var facets = await repo.GetFacetsAsync(FacetField.Artist, namedScope, CancellationToken.None);

            Assert.Equal(["Lib2Artist"], facets.Select(f => f.Value));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioExactFiltersMatchExactly(DatabaseFixture db)
    {
        [Fact]
        public async Task ArtistExactQueenDoesNotMatchQueensryche()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var queenId = await repo.InsertDiscoveredAsync("/media/exact-queen.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(queenId, Harness.ReadyResultWith(artist: "Queen"), CancellationToken.None);
            var lookalikeId = await repo.InsertDiscoveredAsync("/media/exact-queensryche.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(lookalikeId, Harness.ReadyResultWith(artist: "Queensrÿche"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(ArtistExact: "Queen"), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(queenId.ToString(), result.Items[0].MediaId);
        }

        [Fact]
        public async Task ArtistExactMatchesCaseInsensitively()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/exact-case-insensitive.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(artist: "queen"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(ArtistExact: "QUEEN"), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(id.ToString(), result.Items[0].MediaId);
        }

        [Fact]
        public async Task AlbumExactMatchesEqualityOnly()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var exactId = await repo.InsertDiscoveredAsync("/media/exact-album-exact.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(exactId, Harness.ReadyResultWith(), CancellationToken.None);
            var supersetId = await repo.InsertDiscoveredAsync("/media/exact-album-superset.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(supersetId, Harness.ReadyResultWith(), CancellationToken.None);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            await connSetup.ExecuteAsync("update library.media set album = 'A Night at the Opera' where id = @id", new { id = exactId });
            await connSetup.ExecuteAsync("update library.media set album = 'A Night at the Opera (Deluxe)' where id = @id", new { id = supersetId });

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(AlbumExact: "A Night at the Opera"), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(exactId.ToString(), result.Items[0].MediaId);
        }

        [Fact]
        public async Task TwoGenresExactOrMatch()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var rockId = await repo.InsertDiscoveredAsync("/media/exact-genres-rock.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(rockId, Harness.ReadyResultWith(genre: "Rock"), CancellationToken.None);
            var jazzId = await repo.InsertDiscoveredAsync("/media/exact-genres-jazz.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(jazzId, Harness.ReadyResultWith(genre: "Jazz"), CancellationToken.None);
            var bluesId = await repo.InsertDiscoveredAsync("/media/exact-genres-blues.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(bluesId, Harness.ReadyResultWith(genre: "Blues"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(GenresExact: ["Rock", "Jazz"]), CancellationToken.None);

            var ids = result.Items.Select(r => r.MediaId).OrderBy(x => x).ToList();
            var expected = new[] { rockId.ToString(), jazzId.ToString() }.OrderBy(x => x);
            Assert.Equal(expected, ids);
        }

        [Fact]
        public async Task ExactFiltersComposeWithTheExistingEligibleFilter()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var eligibleId = await repo.InsertDiscoveredAsync("/media/exact-compose-eligible.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(eligibleId, Harness.ReadyResultWith(artist: "ComposeArtist"), CancellationToken.None);
            var ineligibleId = await repo.InsertDiscoveredAsync("/media/exact-compose-ineligible.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(ineligibleId, Harness.ReadyResultWith(artist: "ComposeArtist"), CancellationToken.None);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            await connSetup.ExecuteAsync("update library.media set eligible = false where id = @id", new { id = ineligibleId });

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(ArtistExact: "ComposeArtist", Eligible: true), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(eligibleId.ToString(), result.Items[0].MediaId);
        }
    }

    // Smoke-caught defect (Epic Y, task Y3): the Catalog bulk toolbar's request body carries every
    // exact filter field, so an unfilled sibling arrives as `""`, not absent. `BuildAdminWhere` used
    // to read a blank `ArtistExact`/`AlbumExact`/`GenresExact` entry as a real (and unmatchable)
    // equality target — `lower(album) = lower('')` — zeroing an otherwise-correct sweep to 0
    // affected. Reproduced live: `?artist-exact=Queen` (2 matching rows) sent
    // `{"artistExact":"Queen","albumExact":""}`, and "Set ineligible" affected 0 rows.
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBlankExactValuesAreTreatedAsAbsent(DatabaseFixture db)
    {
        [Fact]
        public async Task BlankAlbumExactAppliesNoFilterInsteadOfMatchingNothing()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id1 = await repo.InsertDiscoveredAsync("/media/blank-album-exact-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id1, Harness.ReadyResultWith(artist: "Queen"), CancellationToken.None);
            var id2 = await repo.InsertDiscoveredAsync("/media/blank-album-exact-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id2, Harness.ReadyResultWith(artist: "Queen"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            // The exact wire shape the smoke repro sent: a real ArtistExact alongside a blank
            // sibling AlbumExact — the blank must be ignored, not treated as "album = ''".
            var filter = new MediaQuery(ArtistExact: "Queen", AlbumExact: "");

            var listed = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, filter, CancellationToken.None);
            Assert.Equal(2, listed.Total);

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);
            Assert.Equal(2, affected);
        }

        [Fact]
        public async Task GenresExactContainingOnlyBlankEntriesAppliesNoFilter()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var rockId = await repo.InsertDiscoveredAsync("/media/blank-genres-exact-rock.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(rockId, Harness.ReadyResultWith(genre: "Rock"), CancellationToken.None);
            var jazzId = await repo.InsertDiscoveredAsync("/media/blank-genres-exact-jazz.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(jazzId, Harness.ReadyResultWith(genre: "Jazz"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(GenresExact: [""]);

            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, filter, CancellationToken.None);

            Assert.Equal(2, result.Total);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBulkPathsInheritTheExactPredicates(DatabaseFixture db)
    {
        [Fact]
        public async Task AnExactFilteredBulkEligibilityAffectsExactlyTheBrowseSet()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var queenId = await repo.InsertDiscoveredAsync("/media/bulk-exact-queen.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(queenId, Harness.ReadyResultWith(artist: "Queen"), CancellationToken.None);
            var lookalikeId = await repo.InsertDiscoveredAsync("/media/bulk-exact-queensryche.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(lookalikeId, Harness.ReadyResultWith(artist: "Queensrÿche"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "Queen");

            var listResult = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, filter, CancellationToken.None);
            var listedIds = listResult.Items.Select(r => r.MediaId).OrderBy(x => x).ToList();

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);

            Assert.Equal(listedIds.Count, affected);
            Assert.Equal([queenId.ToString()], listedIds);
        }

        [Fact]
        public async Task TheLookalikeSurvivesAnExactFilteredSweep()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var queenId = await repo.InsertDiscoveredAsync("/media/bulk-survive-queen.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(queenId, Harness.ReadyResultWith(artist: "Queen"), CancellationToken.None);
            var lookalikeId = await repo.InsertDiscoveredAsync("/media/bulk-survive-queensryche.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(lookalikeId, Harness.ReadyResultWith(artist: "Queensrÿche"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(ArtistExact: "Queen");

            await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var lookalikeEligible = await conn.ExecuteScalarAsync<bool>(
                "select eligible from library.media where id = @id", new { id = lookalikeId });
            Assert.True(lookalikeEligible);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEligibilityBehaviorIsUnchanged(DatabaseFixture db)
    {
        [Fact]
        public async Task AFullIneligibleSweepLeavesRowsVisibleAndReincludable()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id1 = await repo.InsertDiscoveredAsync("/media/sweep-unchanged-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id1, Harness.ReadyResultWith(genre: "SweepGenre"), CancellationToken.None);
            var id2 = await repo.InsertDiscoveredAsync("/media/sweep-unchanged-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id2, Harness.ReadyResultWith(genre: "SweepGenre"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(GenresExact: ["SweepGenre"]);

            // Sweep every matching row to ineligible.
            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);
            Assert.Equal(2, affected);

            // Rows stay visible on browse (no eligible filter applied) — F18.4/F18.5 stand.
            var afterSweep = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, filter, CancellationToken.None);
            Assert.Equal(2, afterSweep.Total);
            Assert.All(afterSweep.Items, r => Assert.False(r.Eligible));

            // And they are re-includable — never a one-way door.
            var reincluded = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, true, scope, CancellationToken.None);
            Assert.Equal(2, reincluded);

            var afterReinclude = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, filter, CancellationToken.None);
            Assert.All(afterReinclude.Items, r => Assert.True(r.Eligible));
        }
    }
}
