// STORY-220 — The catalog shows and filters by mood (SPEC F86.8) — SQL half. The API half lives in
// Host.Tests/Specs/Story220_CatalogMoodsBrowse.cs; the UI half is T80 (not built by this task).
//
// BDD specification — xUnit. Integration via DatabaseCollection (real Postgres). Mirrors
// Story148's ScenarioExactFiltersMatchExactly facts for the new MoodsExact predicate — the one
// difference is moods is a text[] column, so the predicate is an EXISTS/unnest any-match rather
// than a scalar equality, proven here against real Postgres semantics.
// House rule F52.4/F86.8: exact predicates land ONCE in BuildAdminWhere — browse and every bulk
// path inherit them; a second predicate implementation fails review.

using Dapper;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMoodExactFilterSql
{
    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — case-insensitive any-match, OR'd across occurrences
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMoodExactMatchesAnyOfARowsMoods(DatabaseFixture db)
    {
        static async Task SetMoodsAsync(DatabaseFixture db, long id, string[] moods)
        {
            await using var conn = await db.DataSource.OpenConnectionAsync();
            await conn.ExecuteAsync("update library.media set moods = @moods where id = @id", new { id, moods });
        }

        [Fact]
        public async Task MoodExactMatchesCaseInsensitivelyAgainstAnyOfARowsMoods()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/mood-case-insensitive.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(), CancellationToken.None);
            await SetMoodsAsync(db, id, ["DREAMY", "driving"]);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(MoodsExact: ["dreamy"]), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(id.ToString(), result.Items[0].MediaId);
        }

        [Fact]
        public async Task TwoMoodExactValuesOrMatchAcrossDifferentRows()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var dreamyId = await repo.InsertDiscoveredAsync("/media/mood-or-dreamy.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(dreamyId, Harness.ReadyResultWith(), CancellationToken.None);
            await SetMoodsAsync(db, dreamyId, ["dreamy"]);

            var drivingId = await repo.InsertDiscoveredAsync("/media/mood-or-driving.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(drivingId, Harness.ReadyResultWith(), CancellationToken.None);
            await SetMoodsAsync(db, drivingId, ["driving"]);

            var somberId = await repo.InsertDiscoveredAsync("/media/mood-or-somber.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(somberId, Harness.ReadyResultWith(), CancellationToken.None);
            await SetMoodsAsync(db, somberId, ["somber"]);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(MoodsExact: ["dreamy", "driving"]), CancellationToken.None);

            var ids = result.Items.Select(r => r.MediaId).OrderBy(x => x).ToList();
            var expected = new[] { dreamyId.ToString(), drivingId.ToString() }.OrderBy(x => x);
            Assert.Equal(expected, ids);
        }

        [Fact]
        public async Task AnUntaggedRowNeverMatchesAnActiveMoodFilter()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var taggedId = await repo.InsertDiscoveredAsync("/media/mood-untagged-tagged.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(taggedId, Harness.ReadyResultWith(), CancellationToken.None);
            await SetMoodsAsync(db, taggedId, ["warm"]);

            // Left null — the row's moods column is never set (untagged).
            var untaggedId = await repo.InsertDiscoveredAsync("/media/mood-untagged-null.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(untaggedId, Harness.ReadyResultWith(), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(MoodsExact: ["warm"]), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(taggedId.ToString(), result.Items[0].MediaId);
        }

        [Fact]
        public async Task AnOutOfVocabularyTermMatchesNothingWithoutError()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/media/mood-out-of-vocab.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResultWith(), CancellationToken.None);
            await SetMoodsAsync(db, id, ["warm"]);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(MoodsExact: ["sparkly"]), CancellationToken.None);

            Assert.Equal(0, result.Total);
        }

        [Fact]
        public async Task MoodExactAndsWithArtistExactThroughTheSharedBuilder()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var bothId = await repo.InsertDiscoveredAsync("/media/mood-ands-both.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(bothId, Harness.ReadyResultWith(artist: "Vantage"), CancellationToken.None);
            await SetMoodsAsync(db, bothId, ["driving"]);

            var wrongMoodId = await repo.InsertDiscoveredAsync("/media/mood-ands-wrong-mood.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(wrongMoodId, Harness.ReadyResultWith(artist: "Vantage"), CancellationToken.None);
            await SetMoodsAsync(db, wrongMoodId, ["somber"]);

            var wrongArtistId = await repo.InsertDiscoveredAsync("/media/mood-ands-wrong-artist.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(wrongArtistId, Harness.ReadyResultWith(artist: "Other"), CancellationToken.None);
            await SetMoodsAsync(db, wrongArtistId, ["driving"]);

            var scope = new LibraryScope([1L]);
            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(
                scope, new MediaQuery(ArtistExact: "Vantage", MoodsExact: ["driving"]), CancellationToken.None);

            Assert.Equal(1, result.Total);
            Assert.Equal(bothId.ToString(), result.Items[0].MediaId);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bulk paths inherit the same predicate (F52/F86.8 idiom)
    // ─────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBulkPathsInheritTheMoodExactPredicate(DatabaseFixture db)
    {
        [Fact]
        public async Task AMoodFilteredBulkEligibilityAffectsExactlyTheBrowseSet()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var matchId = await repo.InsertDiscoveredAsync("/media/mood-bulk-match.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(matchId, Harness.ReadyResultWith(), CancellationToken.None);
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync("update library.media set moods = @moods where id = @id", new { id = matchId, moods = new[] { "epic" } });

            var noMatchId = await repo.InsertDiscoveredAsync("/media/mood-bulk-nomatch.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(noMatchId, Harness.ReadyResultWith(), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(MoodsExact: ["epic"]);

            var listed = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, filter, CancellationToken.None);
            Assert.Equal(1, listed.Total);

            var affected = await ((IAdminMediaWrite)repo).SetEligibilityAsync(filter, false, scope, CancellationToken.None);
            Assert.Equal(listed.Total, affected);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBlankMoodsExactEntriesAreTreatedAsAbsent(DatabaseFixture db)
    {
        [Fact]
        public async Task MoodsExactContainingOnlyBlankEntriesAppliesNoFilter()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id1 = await repo.InsertDiscoveredAsync("/media/mood-blank-1.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id1, Harness.ReadyResultWith(), CancellationToken.None);
            var id2 = await repo.InsertDiscoveredAsync("/media/mood-blank-2.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id2, Harness.ReadyResultWith(), CancellationToken.None);

            var scope = new LibraryScope([1L]);
            var filter = new MediaQuery(MoodsExact: [""]);

            var result = await ((IAdminMediaQuery)repo).ListAdminAsync(scope, filter, CancellationToken.None);

            Assert.Equal(2, result.Total);
        }
    }
}
