// STORY-041 — Operator tag edit survives re-enrichment
//
// BDD specification — xUnit. Exercises the enrichment worker's tag-write step against the
// tags_edited_at sentinel. Integration: real Postgres via DatabaseCollection (mirrors
// Story033_EnrichmentWritesEnergy). Mirrors Story033 seam-for-seam.
//
// AC1 — first enrichment (tags_edited_at NULL) writes tag columns from embedded file tags.
// AC2 — a row with tags_edited_at set is NOT overwritten on re-enrichment.
// AC3 — loudness still (re)writes unconditionally on the edited row (disjoint column from tags).
//       Cue/energy/bpm are NOT "still written" post-STORY-341 (SPEC F135.1): the fast pass resets
//       them to NULL on every run, edited row or not, and the backfill lane sweeps them back in.
// AC4 — a tags_edited_at-NULL row may be (re)written from the file.

using Dapper;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureOperatorTagEditSurvivesReEnrichment
{
    // Inline DTO for querying tag + enricher-owned columns directly from Postgres.
    sealed class TagRow
    {
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Genre { get; set; }
        public int? Year { get; set; }
        public double? IntegratedLufs { get; set; }
        public double? IntroEnergy { get; set; }
        public double? OutroEnergy { get; set; }
        public DateTime? EnergyAnalyzedAt { get; set; }
        public DateTime? TagsEditedAt { get; set; }
    }

    static async Task<TagRow> SelectRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        var row = await conn.QuerySingleAsync<TagRow>(
            "select title, artist, album, genre, year, integrated_lufs, " +
            "intro_energy, outro_energy, energy_analyzed_at, tags_edited_at " +
            "from library.media where id = @id",
            new { id });
        return row;
    }

    /// <summary>
    /// Stamps tags_edited_at on an existing row, simulating an operator PATCH edit (W2).
    /// </summary>
    static async Task SimulateOperatorTagEditAsync(
        DatabaseFixture db, long id,
        string title, string artist, string album, string genre, int year)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        await conn.ExecuteAsync(
            "update library.media set title = @title, artist = @artist, album = @album, " +
            "genre = @genre, year = @year, tags_edited_at = now() where id = @id",
            new { id, title, artist, album, genre, year });
    }

    // ---------------------------------------------------------------------
    // AC1 — HAPPY PATH: first enrichment writes tags from embedded file tags
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioFirstEnrichmentWritesTags(DatabaseFixture db)
    {
        [Fact]
        public async Task FirstEnrichmentWritesTagColumnsFromEmbeddedTags()
        {
            // Arrange: a freshly discovered row (tags_edited_at NULL).
            // The WAV file carries embedded title/artist/album/genre/year.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "ac1_tags.flac",
                    title: "Test Title", artist: "Test Artist", album: "Test Album",
                    genre: "Electronic", year: 2024);

                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                // Act: enrich — tags_edited_at is NULL so tag columns must be written from the file.
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer())
                    .EnrichOneAsync(id, CancellationToken.None);

                // Assert: tag columns contain the embedded file values.
                var row = await SelectRowAsync(db, id);
                Assert.Equal("Test Title", row.Title);
                Assert.Equal("Test Artist", row.Artist);
                Assert.Equal("Test Album", row.Album);
                Assert.Equal("Electronic", row.Genre);
                Assert.Equal(2024, row.Year);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // AC2+AC3 — HAPPY PATH: edited row keeps operator tags; enricher-owned
    //           columns still update unconditionally
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEditedRowTagsAreFrozen(DatabaseFixture db)
    {
        [Fact]
        public async Task ReEnrichmentDoesNotOverwriteTagColumnsWhenTagsEditedAtIsSet()
        {
            // Arrange: enrich once (tags_edited_at NULL → file tags land), then simulate an
            // operator edit that overwrites them and stamps tags_edited_at.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "ac2_frozen.flac",
                    title: "File Title", artist: "File Artist", album: "File Album",
                    genre: "Pop", year: 2020);

                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                // First enrichment — tag columns from file.
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer())
                    .EnrichOneAsync(id, CancellationToken.None);

                // Simulate operator edit (W2 PATCH path): overwrites tags and stamps tags_edited_at.
                await SimulateOperatorTagEditAsync(db, id,
                    title: "Operator Title", artist: "Operator Artist", album: "Operator Album",
                    genre: "Rock", year: 2099);

                // Reset state to discovered so EnrichOneAsync processes the row again.
                await using var conn = await db.DataSource.OpenConnectionAsync();
                await conn.ExecuteAsync("update library.media set state = 'discovered' where id = @id", new { id });

                // Act: re-enrich — tags_edited_at is now set, so tag columns must NOT be overwritten.
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer())
                    .EnrichOneAsync(id, CancellationToken.None);

                // Assert: operator values are preserved; file values are NOT written back.
                var row = await SelectRowAsync(db, id);
                Assert.Equal("Operator Title", row.Title);
                Assert.Equal("Operator Artist", row.Artist);
                Assert.Equal("Operator Album", row.Album);
                Assert.Equal("Rock", row.Genre);
                Assert.Equal(2099, row.Year);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task LoudnessStillUpdatesUnconditionallyOnAnEditedRow()
        {
            // AC3: loudness is enricher-owned and disjoint from the tag columns — it (re)writes
            // even when tags_edited_at is set, unlike title/artist/album/genre/year above.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "ac3_loudness.flac");

                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                // First enrichment with loudness = -16.0 LUFS (FakeLoudnessAnalyzer's default).
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer())
                    .EnrichOneAsync(id, CancellationToken.None);

                // Simulate operator tag edit + stamp tags_edited_at.
                await SimulateOperatorTagEditAsync(db, id,
                    title: "Frozen", artist: "Frozen", album: "Frozen", genre: "Jazz", year: 1999);

                // Reset state to discovered.
                await using var conn = await db.DataSource.OpenConnectionAsync();
                await conn.ExecuteAsync("update library.media set state = 'discovered' where id = @id", new { id });

                // Act: re-enrich with a different loudness measurement.
                var fakeLoud2 = new FakeLoudnessAnalyzer { Fixed = new GenWave.Core.Domain.Loudness(-9.0, -0.5, true) };
                await Harness.EnrichmentWith(repo, fakeLoud2, new FakeCueAnalyzer())
                    .EnrichOneAsync(id, CancellationToken.None);

                // Assert: loudness updated to the new value (unconditional write).
                var row = await SelectRowAsync(db, id);
                Assert.Equal(-9.0, row.IntegratedLufs);
                // Tag columns remain frozen at operator values.
                Assert.Equal("Frozen", row.Title);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task EnergyResetsToNullOnAnEditedRowsFastPassEvenIfPreviouslyBackfilled()
        {
            // AC3 addendum (SPEC F135.1, STORY-341): re-running the fast pass — edited row or not —
            // always nulls out energy again, even if a prior backfill had already filled it in; the
            // backfill lane re-sweeps it on the next tick (same NULL-driven claim, no special case).
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "ac3_energy_reset.flac");

                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);
                await repo.WriteEnrichmentAsync(
                    id, Harness.ReadyResult(true) with { IntroEnergy = 0.6, OutroEnergy = 0.4, EnergyAnalyzedAt = DateTime.UtcNow },
                    CancellationToken.None);

                await SimulateOperatorTagEditAsync(db, id,
                    title: "Frozen", artist: "Frozen", album: "Frozen", genre: "Jazz", year: 1999);

                await using var conn = await db.DataSource.OpenConnectionAsync();
                await conn.ExecuteAsync("update library.media set state = 'discovered' where id = @id", new { id });

                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer())
                    .EnrichOneAsync(id, CancellationToken.None);

                var row = await SelectRowAsync(db, id);
                Assert.Null(row.IntroEnergy);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ---------------------------------------------------------------------
    // AC4 — SAD PATH: tags_edited_at NULL row is NOT falsely frozen
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioUnEditedRowIsNotFalselyFrozen(DatabaseFixture db)
    {
        [Fact]
        public async Task TagColumnsMayBeRewrittenWhenTagsEditedAtIsNull()
        {
            // Arrange: enrich once, then re-enrich with a file carrying different embedded tags
            // but WITHOUT stamping tags_edited_at (no operator edit).
            // The second enrichment must overwrite tag columns from the file.
            await db.ResetAsync();
            var dir = TestMedia.NewTempDir();
            try
            {
                var path = TestMedia.CreateTone(dir, "ac4_no_freeze.flac",
                    title: "Original Title", artist: "Original Artist", album: "Original Album",
                    genre: "Blues", year: 2010);

                var repo = Harness.Repo(db);
                var id = await repo.InsertDiscoveredAsync(path, "flac", new FileInfo(path).Length, Harness.Mtime, CancellationToken.None);

                // First enrichment — tags_edited_at NULL, file tags applied.
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer())
                    .EnrichOneAsync(id, CancellationToken.None);

                // Verify first enrichment landed.
                var rowAfterFirst = await SelectRowAsync(db, id);
                Assert.Equal("Original Title", rowAfterFirst.Title);
                Assert.Null(rowAfterFirst.TagsEditedAt); // still NULL — no operator edit

                // Reset state to discovered so EnrichOneAsync processes the row again.
                // tags_edited_at remains NULL — this simulates a backfill / re-enrichment run
                // on a never-edited row.
                await using var conn = await db.DataSource.OpenConnectionAsync();
                await conn.ExecuteAsync("update library.media set state = 'discovered' where id = @id", new { id });

                // Act: re-enrich — tags_edited_at is still NULL, so file tags must be applied.
                await Harness.EnrichmentWith(repo, new FakeLoudnessAnalyzer(), new FakeCueAnalyzer(), new FakeEnergyAnalyzer())
                    .EnrichOneAsync(id, CancellationToken.None);

                // Assert: tags remain from the file (same file so same values — the key property is
                // that tags_edited_at NULL did NOT prevent the write).
                var row = await SelectRowAsync(db, id);
                Assert.Equal("Original Title", row.Title);
                Assert.Equal("Original Artist", row.Artist);
                Assert.Equal("Original Album", row.Album);
                Assert.Equal("Blues", row.Genre);
                Assert.Equal(2010, row.Year);
                Assert.Null(row.TagsEditedAt); // operator never edited — sentinel still NULL
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
