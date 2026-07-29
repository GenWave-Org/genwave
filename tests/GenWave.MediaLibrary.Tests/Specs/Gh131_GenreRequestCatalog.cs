// gh-#131 — "Anything metal!!" means metal, not Rock (catalog half)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration). Owns the SQL half of gh-#131
// against the real schema: FindBestAsync's AND-merged case-insensitive exact genre leg,
// HasRequestableGenreAsync (the matcher's "station actually stocks this genre" gate),
// ListRequestableGenresAsync (the public options list — law + safe-scope applied, the acceptance
// pin that safe content's genres never leak), FindVibeAsync's widened (moods, genre, envelope)
// shape, RequestRepository's picked_genre/picked_mood round trip (a picker-only row is a legal
// parse target with a NULL wish), and db/28's in-place migration. The matcher decision tree is
// Host.Tests' Gh131_GenreRequestPredicates.cs; the fulfillment rung is Orchestration.Tests'
// Gh131_GenreVibeFulfillment.cs — the same split STORY-226/227 established.

using Dapper;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Station;
using GenWave.MediaLibrary.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureGenreRequestCatalog
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static RequestCatalogProbeRepository Probe(DatabaseFixture db) =>
        new(db.DataSource, new FakeSafeScopeProvider(), new FakeAudiencePostureProvider(),
            NullLogger<RequestCatalogProbeRepository>.Instance);

    static RequestCatalogProbeRepository ScopedProbe(DatabaseFixture db, long safeLibraryId) =>
        new(db.DataSource, new FakeSafeScopeProvider(safeLibraryId), new FakeAudiencePostureProvider(),
            NullLogger<RequestCatalogProbeRepository>.Instance);

    static RequestRepository RequestRepo(DatabaseFixture db) =>
        new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource), 24);

    /// <summary>Inserts a ready + measurable + eligible row carrying the given artist/genre — the
    /// same shape Story226's own seed helper builds, genre made explicit for this file's facts.</summary>
    static async Task<long> InsertGenreTrackAsync(DatabaseFixture db, string path, string artist, string genre)
    {
        var repo = Harness.Repo(db);
        var id = await repo.InsertDiscoveredAsync(path, "flac", 1, Harness.Mtime, CancellationToken.None);
        await repo.WriteEnrichmentAsync(
            id, Harness.ReadyResultWith(artist: artist, genre: genre), CancellationToken.None);
        return id;
    }

    /// <summary>Creates a fresh named library — tag keeps the name unique across facts (the
    /// Story226/Gh099 seed idiom; ResetAsync truncates library.media only, never library.library).</summary>
    static async Task<long> CreateLibraryAsync(DatabaseFixture db, string tag)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<long>(
            "insert into library.library (name) values (@name) returning id", new { name = $"gh131-{tag}" });
    }

    static async Task MoveToLibraryAsync(DatabaseFixture db, long id, long libraryId)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync("update library.media set library_id = @libraryId where id = @id", new { libraryId, id });
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the genre leg ANDs into the best-match probe
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGenreAndsIntoBestMatch(DatabaseFixture db)
    {
        [Fact]
        public async Task AnArtistHitOutsideTheRequestedGenreIsNoMatch()
        {
            // Arrange: the only row by the requested artist is Rock — the request said Jazz.
            await db.ResetAsync();
            await InsertGenreTrackAsync(db, "/gh131/best-genre-mismatch.flac", "Led Zeppelin", "Rock");

            // Act: artist AND genre — predicates merge, never compete.
            var found = await Probe(db).FindBestAsync("Led Zeppelin", null, "Jazz", CancellationToken.None);

            // Assert: null — the genre leg vetoes the artist hit.
            Assert.Null(found);
        }

        [Fact]
        public async Task TheGenreLegMatchesCaseInsensitivelyAndExactly()
        {
            // Arrange: a Rock row by the artist, plus a Post-Rock decoy that would ONLY match if the
            // genre leg were a substring match instead of exact.
            await db.ResetAsync();
            var rockId = await InsertGenreTrackAsync(db, "/gh131/best-genre-exact.flac", "Led Zeppelin", "Rock");
            await InsertGenreTrackAsync(db, "/gh131/best-genre-decoy.flac", "Led Zeppelin", "Post-Rock");

            // Act: lowercase "rock" against the stored "Rock".
            var found = await Probe(db).FindBestAsync("Led Zeppelin", null, "rock", CancellationToken.None);

            // Assert: exactly the Rock row — case folds, substrings never match.
            Assert.Equal(rockId, found);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — HasRequestableGenreAsync, the matcher's stocked-genre gate
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioHasRequestableGenre(DatabaseFixture db)
    {
        [Fact]
        public async Task AStockedGenreAnswersTrueCaseInsensitively()
        {
            await db.ResetAsync();
            await InsertGenreTrackAsync(db, "/gh131/has-genre.flac", "Some Band", "Metal");

            Assert.True(await Probe(db).HasRequestableGenreAsync("mETAl", CancellationToken.None));
        }

        [Fact]
        public async Task AnUnstockedGenreAnswersFalse()
        {
            // The "station has no metal ⇒ unmatched" pin's SQL half.
            await db.ResetAsync();
            await InsertGenreTrackAsync(db, "/gh131/has-genre-only-rock.flac", "Some Band", "Rock");

            Assert.False(await Probe(db).HasRequestableGenreAsync("Metal", CancellationToken.None));
        }

        [Fact]
        public async Task AGenreCarriedOnlyBySafeScopeRowsAnswersFalse()
        {
            // gh-#99 discipline carried forward: safe content is not requestable, so its genres do
            // not count as stocked either.
            await db.ResetAsync();
            var safeLibraryId = await CreateLibraryAsync(db, "has-genre-safe");
            var id = await InsertGenreTrackAsync(db, "/gh131/has-genre-safe.flac", "Station", "Ambient");
            await MoveToLibraryAsync(db, id, safeLibraryId);

            Assert.False(await ScopedProbe(db, safeLibraryId).HasRequestableGenreAsync("Ambient", CancellationToken.None));
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — ListRequestableGenresAsync, the public options list
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioRequestableGenresList(DatabaseFixture db)
    {
        [Fact]
        public async Task GenresAreDistinctCaseInsensitivelyAndOrdered()
        {
            // Arrange: "Rock" twice in different casings plus "Jazz" — one entry per
            // case-insensitively distinct genre, ordered case-insensitively.
            await db.ResetAsync();
            await InsertGenreTrackAsync(db, "/gh131/list-rock-1.flac", "Band A", "Rock");
            await InsertGenreTrackAsync(db, "/gh131/list-rock-2.flac", "Band B", "rock");
            await InsertGenreTrackAsync(db, "/gh131/list-jazz.flac", "Band C", "Jazz");

            var genres = await Probe(db).ListRequestableGenresAsync(CancellationToken.None);

            Assert.Equal(["jazz", "rock"], genres.Select(g => g.ToLowerInvariant()).ToList());
        }

        [Fact]
        public async Task SafeScopeGenresNeverAppearInTheList()
        {
            // The acceptance pin: genres exclude safe-scope rows — the seeded safe loop's genre
            // must not leak onto the public request form.
            await db.ResetAsync();
            var safeLibraryId = await CreateLibraryAsync(db, "list-safe");
            var safeId = await InsertGenreTrackAsync(db, "/gh131/list-safe.flac", "Station", "Ambient");
            await MoveToLibraryAsync(db, safeId, safeLibraryId);
            await InsertGenreTrackAsync(db, "/gh131/list-main.flac", "Band A", "Rock");

            var genres = await ScopedProbe(db, safeLibraryId).ListRequestableGenresAsync(CancellationToken.None);

            Assert.Equal(["Rock"], genres);
        }

        [Fact]
        public async Task ANeverPlayOnlyGenreNeverAppearsInTheList()
        {
            // Operator vetoes are law on this read too — a genre whose only row is never_play is
            // not requestable, so it is not offered.
            await db.ResetAsync();
            var vetoedId = await InsertGenreTrackAsync(db, "/gh131/list-vetoed.flac", "Band A", "Metal");
            await new MediaRatingRepository(db.DataSource, new FakeSafeScopeProvider())
                .SetNeverPlayAsync(vetoedId.ToString(), true, CancellationToken.None);
            await InsertGenreTrackAsync(db, "/gh131/list-kept.flac", "Band B", "Rock");

            var genres = await Probe(db).ListRequestableGenresAsync(CancellationToken.None);

            Assert.Equal(["Rock"], genres);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — FindVibeAsync's genre leg (genre-only legal, genre+mood ANDs)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioGenreVibeQuery(DatabaseFixture db)
    {
        [Fact]
        public async Task AGenreOnlyVibeFindsAMatchingRow()
        {
            await db.ResetAsync();
            var id = await InsertGenreTrackAsync(db, "/gh131/vibe-genre-only.flac", "Some Band", "Metal");

            var found = await Probe(db).FindVibeAsync([], "metal", envelope: null, CancellationToken.None);

            Assert.Equal(id.ToString(), found?.MediaId);
        }

        [Fact]
        public async Task GenreAndMoodMustBothHold()
        {
            // Arrange: a Metal row without the mood and a dreamy Rock row — neither satisfies the
            // AND; only a dreamy Metal row does.
            await db.ResetAsync();
            await InsertGenreTrackAsync(db, "/gh131/vibe-and-metal.flac", "Band A", "Metal");
            var dreamyRockId = await InsertGenreTrackAsync(db, "/gh131/vibe-and-rock.flac", "Band B", "Rock");
            await Harness.Repo(db).WriteMoodsAsync(dreamyRockId, ["dreamy"], CancellationToken.None);

            var missed = await Probe(db).FindVibeAsync(["dreamy"], "Metal", envelope: null, CancellationToken.None);

            var dreamyMetalId = await InsertGenreTrackAsync(db, "/gh131/vibe-and-both.flac", "Band C", "Metal");
            await Harness.Repo(db).WriteMoodsAsync(dreamyMetalId, ["dreamy"], CancellationToken.None);

            var found = await Probe(db).FindVibeAsync(["dreamy"], "Metal", envelope: null, CancellationToken.None);

            Assert.Null(missed);
            Assert.Equal(dreamyMetalId.ToString(), found?.MediaId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the store's picked columns and the genre-widened row shapes
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioPickedColumnsRoundTrip(DatabaseFixture db)
    {
        [Fact]
        public async Task APickerOnlyRowIsALegalParseTargetWithANullWish()
        {
            // Given a picker-only insert (gh-#131: null wish, validated picked values)...
            await db.ResetRequestAsync();
            var repo = RequestRepo(db);
            var id = await repo.InsertAsync(
                null, "Metal", "dreamy", DateTimeOffset.UtcNow.AddMinutes(15), CancellationToken.None);

            // When the parser asks for it...
            var result = await repo.GetForParseAsync(id, CancellationToken.None);

            // Then the row IS a parse target — null wish, picked values carried through — and the
            // recovery sweep sees it too.
            Assert.NotNull(result);
            Assert.Null(result?.Wish);
            Assert.Equal("Metal", result?.PickedGenre);
            Assert.Equal("dreamy", result?.PickedMood);
            Assert.Equal([id], await repo.ListUnparsedPendingIdsAsync(CancellationToken.None));
        }

        [Fact]
        public async Task MarkParsedStoresTheGenreAndTheFulfillmentReadCarriesIt()
        {
            // Given a picker-only row parsed into a genre predicate...
            await db.ResetRequestAsync();
            var repo = RequestRepo(db);
            var id = await repo.InsertAsync(
                null, "Metal", null, DateTimeOffset.UtcNow.AddMinutes(15), CancellationToken.None);

            // When the parse outcome lands (genre column, T88's write widened by gh-#131)...
            await repo.MarkParsedAsync(id, null, null, "Metal", [], unmatched: false, CancellationToken.None);

            // Then the row leaves the unparsed set and qualifies for fulfillment ON ITS GENRE alone
            // (no match, no moods), carrying it to the vibe probe.
            Assert.Empty(await repo.ListUnparsedPendingIdsAsync(CancellationToken.None));
            var live = await repo.GetOldestLiveAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            Assert.Equal(id, live?.Id);
            Assert.Equal("Metal", live?.Genre);
            Assert.Null(live?.MatchedMediaId);
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — db/28's in-place migration is idempotent and complete
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioInPlaceMigration(DatabaseFixture db)
    {
        [Fact]
        public async Task TheMigrationAddsTheThreeColumnsAndRunsTwiceWithoutError()
        {
            // Given a pre-gh-#131 station.request shape (the three columns dropped, as an existing
            // deployment's DB would look)...
            await db.ResetRequestAsync();
            await using (var conn = await db.StationDataSource.OpenConnectionAsync())
            {
                await conn.ExecuteAsync(
                    """
                    alter table station.request
                      drop column if exists picked_genre,
                      drop column if exists picked_mood,
                      drop column if exists genre
                    """);
            }

            // When db/28 runs — twice, because every migration script must be idempotent
            // (RunFileInContainer throws on a nonzero exit code, so reaching the assertions at all
            // is the "no error" proof)...
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "29-request-genre-migration.sh"));
            db.RunFileInContainer(Path.Combine(db.RepoRoot, "db", "29-request-genre-migration.sh"));

            // Then the widened repository shape works end-to-end against the migrated table.
            var repo = RequestRepo(db);
            var id = await repo.InsertAsync(
                null, "Metal", "dreamy", DateTimeOffset.UtcNow.AddMinutes(15), CancellationToken.None);
            var result = await repo.GetForParseAsync(id, CancellationToken.None);
            Assert.Equal("Metal", result?.PickedGenre);
        }
    }
}
