// StoryF3 (repo half) — Bulk eligibility by filter + GET ?eligible= filter
//
// BDD specification — xUnit. Integration via DatabaseCollection (real Postgres).
// Mirrors Story040_AdminWriteRepoAndEligibilityFilter for the bulk write side.
// Covers:
//   • SetEligibilityAsync sets eligible on ONLY the rows matching the filter (state, artist,
//     genre, library-id, q) within the scope — out-of-scope rows are never touched.
//   • Returned count equals the number of rows actually affected.
//   • Empty scope → 0 affected, no SQL issued.
//   • ListAdminAsync respects ?eligible= filter (both true and false).
//
// HTTP-layer scenarios (auth, POST round-trip) live in
// GenWave.Host.Tests/Specs/StoryF3_BulkEligibilityEndpoint.cs (operator-gated).

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureBulkEligibilityByFilter
{
    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — filter matches a subset; only those rows flip
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFilteredBulkSetAffectsOnlyMatchingRows(DatabaseFixture db)
    {
        [Fact]
        public async Task SetEligibilityByArtistFilterAffectsOnlyMatchingRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // Seed: two rows with different artists.
            var matchId = await repo.InsertDiscoveredAsync("/media/bulk-artist-a.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(matchId, Harness.ReadyResultWith(artist: "ArtistAlpha"), CancellationToken.None);

            var noMatchId = await repo.InsertDiscoveredAsync("/media/bulk-artist-b.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(noMatchId, Harness.ReadyResultWith(artist: "ArtistBeta"), CancellationToken.None);

            // Both start eligible = true.
            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(Artist: "ArtistAlpha");

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);

            Assert.Equal(1, affected);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            var matchEligible = await conn.ExecuteScalarAsync<bool>(
                "select eligible from library.media where id = @id", new { id = matchId });
            var noMatchEligible = await conn.ExecuteScalarAsync<bool>(
                "select eligible from library.media where id = @id", new { id = noMatchId });

            Assert.False(matchEligible);
            Assert.True(noMatchEligible);
        }

        [Fact]
        public async Task SetEligibilityByGenreFilterAffectsOnlyMatchingRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var rockId = await repo.InsertDiscoveredAsync("/media/bulk-genre-rock.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(rockId, Harness.ReadyResultWith(genre: "Rock"), CancellationToken.None);

            var jazzId = await repo.InsertDiscoveredAsync("/media/bulk-genre-jazz.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(jazzId, Harness.ReadyResultWith(genre: "Jazz"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(Genre: "Rock");

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);

            Assert.Equal(1, affected);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            Assert.False(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = rockId }));
            Assert.True(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = jazzId }));
        }

        [Fact]
        public async Task SetEligibilityByStateFilterAffectsOnlyRowsInThatState()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // Ready row
            var readyId = await repo.InsertDiscoveredAsync("/media/bulk-state-ready.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(readyId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            // Discovered row (no enrichment = stays discovered)
            var discoveredId = await repo.InsertDiscoveredAsync("/media/bulk-state-discovered.flac", "flac", 1, Harness.Mtime, CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(State: "ready");

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);

            Assert.Equal(1, affected);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            Assert.False(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = readyId }));
            Assert.True(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = discoveredId }));
        }

        [Fact]
        public async Task SetEligibilityByQFilterAffectsOnlyMatchingTitleRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var matchId = await repo.InsertDiscoveredAsync("/media/bulk-q-hello.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(matchId, Harness.ReadyResultWith(title: "Hello World"), CancellationToken.None);

            var noMatchId = await repo.InsertDiscoveredAsync("/media/bulk-q-other.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(noMatchId, Harness.ReadyResultWith(title: "Goodbye World"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(Q: "Hello");

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);

            Assert.Equal(1, affected);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            Assert.False(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = matchId }));
            Assert.True(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = noMatchId }));
        }

        [Fact]
        public async Task SetEligibilityWithNoFilterSetsAllInScopeRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id1 = await repo.InsertDiscoveredAsync("/media/bulk-all-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id1, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var id2 = await repo.InsertDiscoveredAsync("/media/bulk-all-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id2, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(); // no predicates beyond scope

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);

            Assert.Equal(2, affected);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            Assert.False(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = id1 }));
            Assert.False(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = id2 }));
        }

        [Fact]
        public async Task ReturnedCountMatchesActuallyAffectedRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            for (var i = 0; i < 3; i++)
            {
                var id = await repo.InsertDiscoveredAsync($"/media/bulk-count-{i}.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(genre: "CountGenre"), CancellationToken.None);
            }

            // One row with a different genre — must not be counted.
            var otherId = await repo.InsertDiscoveredAsync("/media/bulk-count-other.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(otherId, Harness.ReadyResultWith(genre: "OtherGenre"), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(Genre: "CountGenre");

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);

            Assert.Equal(3, affected);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCOPE ENFORCEMENT — out-of-scope rows are never touched
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioOutOfScopeRowsAreNeverTouched(DatabaseFixture db)
    {
        [Fact]
        public async Task RowsOutsideScopeAreNotAffected()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // Insert a second library (id is generated always as identity — no explicit id).
            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            var lib2Id = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.library (name) values ('TestLib2') returning id");

            // In-scope row: library 1.
            var inScopeId = await repo.InsertDiscoveredAsync("/media/bulk-scope-in.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(inScopeId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            // Out-of-scope row: insert directly into the new library.
            var outScopeId = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.media (path, format, size_bytes, mtime, library_id) " +
                "values ('/media/bulk-scope-out.flac', 'flac', 1, @mtime, @lib2Id) returning id",
                new { mtime = Harness.Mtime, lib2Id });
            await repo.WriteEnrichmentAsync(outScopeId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            // Scope only includes library 1 — the lib2 row must not be touched.
            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(); // no filter beyond scope

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);

            Assert.Equal(1, affected);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            Assert.False(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = inScopeId }));
            // The out-of-scope row must still be true (unchanged).
            Assert.True(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id = outScopeId }));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EMPTY SCOPE — zero affected, no SQL
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEmptyScopeFlipsNothing(DatabaseFixture db)
    {
        [Fact]
        public async Task EmptyScopeReturnsZeroAndDoesNotTouchAnyRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/bulk-empty-scope.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(
                new MediaQuery(), false, LibraryScope.None, CancellationToken.None);

            Assert.Equal(0, affected);

            await using var conn = await db.DataSource.OpenConnectionAsync();
            Assert.True(await conn.ExecuteScalarAsync<bool>("select eligible from library.media where id = @id", new { id }));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EQUIVALENCE — ListAdminAsync and SetEligibilityAsync select identical rows
    // for the same non-trivial MediaQuery (defense-in-depth against WHERE divergence)
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioListAndBulkSelectIdenticalRows(DatabaseFixture db)
    {
        /// <summary>
        /// Seeds a mixed row set and asserts that the ids returned by
        /// <see cref="IAdminMediaQuery.ListAdminAsync"/> equal exactly the ids whose
        /// <c>eligible</c> column was flipped by <see cref="IAdminMediaWrite.SetEligibilityAsync"/>
        /// for the same non-trivial <see cref="MediaQuery"/>.
        /// </summary>
        [Fact]
        public async Task ListAdminAndSetEligibilitySelectIdenticalRowsForSameQuery()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // Seed rows that exercise state + artist + q + eligible predicates simultaneously.
            // "Match" rows: state=ready, artist=BandA, title contains "Song", eligible=true.
            var matchId1 = await repo.InsertDiscoveredAsync("/media/eq-match-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(matchId1, Harness.ReadyResultWith(artist: "BandA", title: "Song One"), CancellationToken.None);

            var matchId2 = await repo.InsertDiscoveredAsync("/media/eq-match-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(matchId2, Harness.ReadyResultWith(artist: "BandA", title: "Song Two"), CancellationToken.None);

            // Non-match: wrong artist (state=ready, title contains "Song" but artist ≠ BandA).
            var wrongArtistId = await repo.InsertDiscoveredAsync("/media/eq-wrong-artist.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(wrongArtistId, Harness.ReadyResultWith(artist: "BandB", title: "Song Three"), CancellationToken.None);

            // Non-match: right artist but title does not contain "Song".
            var wrongTitleId = await repo.InsertDiscoveredAsync("/media/eq-wrong-title.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(wrongTitleId, Harness.ReadyResultWith(artist: "BandA", title: "Other Track"), CancellationToken.None);

            // Non-match: already ineligible — the eligible=true filter excludes it.
            var ineligibleId = await repo.InsertDiscoveredAsync("/media/eq-ineligible.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(ineligibleId, Harness.ReadyResultWith(artist: "BandA", title: "Song Ineligible"), CancellationToken.None);
            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            await connSetup.ExecuteAsync("update library.media set eligible = false where id = @id", new { id = ineligibleId });

            var scope = new LibraryScope([1L]);
            // Non-trivial query: state + artist + q + eligible all set.
            var query = new MediaQuery(State: "ready", Artist: "BandA", Q: "Song", Eligible: true, Limit: 200);

            // Step 1: capture the ids the list returns.
            var listResult = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, query, CancellationToken.None);
            var listedIds = listResult.Items.Select(r => long.Parse(r.MediaId)).OrderBy(x => x).ToList();

            // Step 2: flip eligible=false using the same query; capture affected count.
            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(query, false, scope, CancellationToken.None);

            // Step 3: assert that count matches.
            Assert.Equal(listedIds.Count, affected);

            // Step 4: assert that exactly the listed ids now have eligible=false; all others unchanged.
            await using var conn = await db.DataSource.OpenConnectionAsync();
            foreach (var id in listedIds)
            {
                var eligible = await conn.ExecuteScalarAsync<bool>(
                    "select eligible from library.media where id = @id", new { id });
                Assert.False(eligible);
            }

            // The non-matching rows must still be in their original state.
            Assert.True(await conn.ExecuteScalarAsync<bool>(
                "select eligible from library.media where id = @id", new { id = wrongArtistId }));
            Assert.True(await conn.ExecuteScalarAsync<bool>(
                "select eligible from library.media where id = @id", new { id = wrongTitleId }));
            // The already-ineligible row was excluded by the eligible=true filter in both list and bulk.
            Assert.False(await conn.ExecuteScalarAsync<bool>(
                "select eligible from library.media where id = @id", new { id = ineligibleId }));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET ?eligible= FILTER — ListAdminAsync respects the eligible predicate
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGetEligibleFilter(DatabaseFixture db)
    {
        [Fact]
        public async Task ListAdminWithEligibleTrueReturnsOnlyEligibleRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var eligibleId = await repo.InsertDiscoveredAsync("/media/list-eligible-true.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(eligibleId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var ineligibleId = await repo.InsertDiscoveredAsync("/media/list-eligible-false.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(ineligibleId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            await connSetup.ExecuteAsync("update library.media set eligible = false where id = @id", new { id = ineligibleId });

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(Eligible: true), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(eligibleId.ToString(), result.Items[0].MediaId);
        }

        [Fact]
        public async Task ListAdminWithEligibleFalseReturnsOnlyIneligibleRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var eligibleId = await repo.InsertDiscoveredAsync("/media/list-ineligible-true.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(eligibleId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var ineligibleId = await repo.InsertDiscoveredAsync("/media/list-ineligible-false.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(ineligibleId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            await connSetup.ExecuteAsync("update library.media set eligible = false where id = @id", new { id = ineligibleId });

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(Eligible: false), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(ineligibleId.ToString(), result.Items[0].MediaId);
        }

        [Fact]
        public async Task ListAdminWithNoEligibleFilterReturnsAllRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var eligibleId = await repo.InsertDiscoveredAsync("/media/list-no-filter-eligible.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(eligibleId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var ineligibleId = await repo.InsertDiscoveredAsync("/media/list-no-filter-ineligible.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(ineligibleId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            await connSetup.ExecuteAsync("update library.media set eligible = false where id = @id", new { id = ineligibleId });

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(), CancellationToken.None);

            // Both rows returned regardless of eligible value.
            Assert.Equal(2, result.Total);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STORY-113 (S5) — ListAdminAsync carries rating state (score/neverPlay), plus the
    // ?never-play=true filter and its composition with library scope (SPEC F33.10).
    // The Host-level wiring (controller param binding, camelCase serialization, ETag
    // stability) is proven with fakes in GenWave.Host.Tests/Specs/Story113_CatalogRatingReads.cs;
    // this is the R8/Q2 real-SQL certainty that the LEFT JOIN + COALESCE actually resolves.
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioListAdminCarriesRatingState(DatabaseFixture db)
    {
        static MediaRatingRepository RatingRepo(DatabaseFixture f) => new(f.DataSource);

        [Fact]
        public async Task ListAdminCarriesTheVotedScoreForARatedRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/rating-voted.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var ratingRepo = RatingRepo(db);
            for (var i = 0; i < 3; i++)
                await ratingRepo.VoteAsync(id.ToString(), VoteDirection.Up, CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(), CancellationToken.None);

            var row = Assert.Single(result.Items, r => r.MediaId == id.ToString());
            Assert.Equal(53, row.Score);
            Assert.False(row.NeverPlay);
        }

        [Fact]
        public async Task ListAdminDefaultsAnUnratedRowToTheLedgerDefault()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // No library.media_rating row is ever written for this id (F33.2 — no backfill).
            var id = await repo.InsertDiscoveredAsync("/media/rating-unrated.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(), CancellationToken.None);

            var row = Assert.Single(result.Items, r => r.MediaId == id.ToString());
            Assert.Equal(50, row.Score);
            Assert.False(row.NeverPlay);
        }

        [Fact]
        public async Task ListAdminCarriesTheNeverPlayFlagAlongsideAnUnflaggedRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var flaggedId = await repo.InsertDiscoveredAsync("/media/rating-flagged.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(flaggedId, Harness.ReadyResult(measurable: true), CancellationToken.None);
            var openId = await repo.InsertDiscoveredAsync("/media/rating-open.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(openId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await RatingRepo(db).SetNeverPlayAsync(flaggedId.ToString(), true, CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(), CancellationToken.None);

            Assert.True(result.Items.Single(r => r.MediaId == flaggedId.ToString()).NeverPlay);
            Assert.False(result.Items.Single(r => r.MediaId == openId.ToString()).NeverPlay);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNeverPlayFilter(DatabaseFixture db)
    {
        static MediaRatingRepository RatingRepo(DatabaseFixture f) => new(f.DataSource);

        [Fact]
        public async Task NeverPlayTrueReturnsExactlyTheFlaggedRow()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var flaggedId = await repo.InsertDiscoveredAsync("/media/never-play-filter-flagged.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(flaggedId, Harness.ReadyResult(measurable: true), CancellationToken.None);
            var openId1 = await repo.InsertDiscoveredAsync("/media/never-play-filter-open-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(openId1, Harness.ReadyResult(measurable: true), CancellationToken.None);
            var openId2 = await repo.InsertDiscoveredAsync("/media/never-play-filter-open-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(openId2, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await RatingRepo(db).SetNeverPlayAsync(flaggedId.ToString(), true, CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(NeverPlay: true), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(flaggedId.ToString(), result.Items[0].MediaId);
        }

        [Fact]
        public async Task NeverPlayAbsentAppliesNoFilter()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var flaggedId = await repo.InsertDiscoveredAsync("/media/never-play-absent-flagged.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(flaggedId, Harness.ReadyResult(measurable: true), CancellationToken.None);
            var openId = await repo.InsertDiscoveredAsync("/media/never-play-absent-open.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(openId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await RatingRepo(db).SetNeverPlayAsync(flaggedId.ToString(), true, CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(), CancellationToken.None);

            Assert.Equal(2, result.Total);
        }

        [Fact]
        public async Task NeverPlayExplicitFalseAppliesNoFilterTheSameAsAbsent()
        {
            // Documented S5 decision: SPEC F33.10 only requires ?never-play=true; false and absent
            // both behave as "no filter" — there is deliberately no "only unflagged" mode (a track
            // must never become a one-way door once flagged).
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var flaggedId = await repo.InsertDiscoveredAsync("/media/never-play-false-flagged.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(flaggedId, Harness.ReadyResult(measurable: true), CancellationToken.None);
            var openId = await repo.InsertDiscoveredAsync("/media/never-play-false-open.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(openId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            await RatingRepo(db).SetNeverPlayAsync(flaggedId.ToString(), true, CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(NeverPlay: false), CancellationToken.None);

            Assert.Equal(2, result.Total);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioNeverPlayComposesWithLibraryScope(DatabaseFixture db)
    {
        [Fact]
        public async Task NeverPlayFilterAppliesOnlyWithinTheGivenScope()
        {
            // Two libraries, each with a flagged row. Scoping to library 1 (the F23.3 named-library
            // swap, resolved by the controller before it ever reaches the repository) must return
            // only library 1's flagged row — library 2's flagged row must never leak across scope.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            // A distinct library name from the ScenarioOutOfScopeRowsAreNeverTouched fixture above:
            // ResetAsync only truncates library.media (cascade), never library.library, and both
            // scenarios share the same DatabaseCollection-scoped Postgres instance.
            await using var connSetup = await db.DataSource.OpenConnectionAsync();
            var lib2Id = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.library (name) values ('RatingNeverPlayScopeLib') returning id");

            var lib1FlaggedId = await repo.InsertDiscoveredAsync("/media/never-play-scope-lib1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(lib1FlaggedId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var lib2FlaggedId = await connSetup.ExecuteScalarAsync<long>(
                "insert into library.media (path, format, size_bytes, mtime, library_id) " +
                "values ('/media/never-play-scope-lib2.flac', 'flac', 1, @mtime, @lib2Id) returning id",
                new { mtime = Harness.Mtime, lib2Id });
            await repo.WriteEnrichmentAsync(lib2FlaggedId, Harness.ReadyResult(measurable: true), CancellationToken.None);

            var ratingRepo = new MediaRatingRepository(db.DataSource);
            await ratingRepo.SetNeverPlayAsync(lib1FlaggedId.ToString(), true, CancellationToken.None);
            await ratingRepo.SetNeverPlayAsync(lib2FlaggedId.ToString(), true, CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, new MediaQuery(NeverPlay: true), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(lib1FlaggedId.ToString(), result.Items[0].MediaId);
        }
    }
}
