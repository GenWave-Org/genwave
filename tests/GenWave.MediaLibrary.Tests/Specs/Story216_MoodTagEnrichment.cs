// STORY-216 — Tracks get moods from a bounded vocabulary
//
// BDD specification — xUnit (SPEC F85.1–F85.5). PLAN T58 ships the vocabulary + column + the
// validating write path (ScenarioBoundedTagging's DB-backed facts below); T72 wires the tagger
// batch (the remaining Skip-pending facts). LLM interaction is faked at the HttpMessageHandler
// boundary (Story187 idiom); the live-Ollama batch is T72's own acceptance, operator-verified.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMoodTagEnrichment
{
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBoundedTagging(DatabaseFixture db)
    {
        // Arrange (T58): one ready row, seeded via the real enrichment write path (Story211 idiom).
        // WriteMoodsAsync is exercised directly against Postgres here — no fake LLM needed for the
        // write-path facts; T72 owns the tagger-batch arrangement (fake HttpMessageHandler) for the
        // remaining Skip-pending facts below.
        async Task<long> SeedReadyAsync()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/media/mood-bounded.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true), CancellationToken.None);
            return id;
        }

        static async Task<string[]?> SelectMoodsAsync(DatabaseFixture db, long id)
        {
            await using var conn = await db.DataSource.OpenConnectionAsync();
            return await conn.ExecuteScalarAsync<string[]?>(
                "select moods from library.media where id = @id", new { id });
        }

        [Fact(Skip = "Pending T72 — see docs/PLAN.md")]
        public void ATaggedTrackCarriesOneToThreeMoods()
        {
            Assert.Fail("pending T72");
        }

        [Fact]
        public async Task EveryStoredMoodIsInTheVocabulary()
        {
            // write-time validation rejects out-of-vocabulary terms (F85.1) — nothing ever lands in
            // the column, not even the valid entries from the same rejected set.
            var id = await SeedReadyAsync();
            var repo = Harness.Repo(db);

            await repo.WriteMoodsAsync(id, ["dreamy", "not-a-real-mood"], CancellationToken.None);

            Assert.Null(await SelectMoodsAsync(db, id));
        }

        [Fact]
        public async Task AnOutOfVocabularyWriteIsReportedAsRejected()
        {
            var id = await SeedReadyAsync();
            var repo = Harness.Repo(db);

            var result = await repo.WriteMoodsAsync(id, ["dreamy", "not-a-real-mood"], CancellationToken.None);

            Assert.Equal(MoodWriteResult.UnknownMood, result);
        }

        [Fact]
        public async Task MoreThanThreeMoodsAreRejected()
        {
            // F85.2 — the ≤3 cap is enforced at the write path itself, not only at tagger parse time.
            var id = await SeedReadyAsync();
            var repo = Harness.Repo(db);

            var result = await repo.WriteMoodsAsync(id, ["dreamy", "driving", "warm", "epic"], CancellationToken.None);

            Assert.Equal(MoodWriteResult.TooManyMoods, result);
        }

        [Fact]
        public async Task AValidSetOfUpToThreeMoodsIsWritten()
        {
            var id = await SeedReadyAsync();
            var repo = Harness.Repo(db);

            await repo.WriteMoodsAsync(id, ["dreamy", "driving", "warm"], CancellationToken.None);

            Assert.Equal(["dreamy", "driving", "warm"], await SelectMoodsAsync(db, id) ?? []);
        }

        [Fact(Skip = "Pending T72 — see docs/PLAN.md")]
        public void ASuccessIsStampedOnce()
        {
            // tagged-at stamp set; a second batch does not re-ask it (F85.2)
            Assert.Fail("pending T72");
        }
    }

    public static class ScenarioMissesRetrySuccessesDoNot
    {
        // Arrange (T72): a batch containing stamped successes and stamped misses; run reenrichment.

        [Fact(Skip = "Pending T72 — see docs/PLAN.md")]
        public static void OnlyMissesAreReAsked()
        {
            // the F76 MusicBrainz etiquette pattern applied to moods (F85.2)
            Assert.Fail("pending T72");
        }
    }

    public static class ScenarioMoodsReachTastePredicates
    {
        // Arrange (T72 + T63): a tagged library; a taste rule with a mood tag predicate.

        [Fact(Skip = "Pending T72 — see docs/PLAN.md")]
        public static void AMoodTagPredicateMatchesTaggedTracks()
        {
            // no parallel matching system — the F82.1 tag predicate consumes moods (F85.5)
            Assert.Fail("pending T72");
        }
    }

    public static class SadPathDegradationAndSprawl
    {
        [Fact(Skip = "Pending T72 — see docs/PLAN.md")]
        public static void ADegradedLlmSkipsTheBatchWithOneLogLine()
        {
            // degraded/off/unconfigured ⇒ clean skip, single line, no per-track noise (F85.3)
            Assert.Fail("pending T72");
        }

        [Fact(Skip = "Pending T72 — see docs/PLAN.md")]
        public static void UnknownTermsAreFilteredNotStored()
        {
            // F85.4 constrained-output: parse, filter to vocabulary
            Assert.Fail("pending T72");
        }

        [Fact(Skip = "Pending T72 — see docs/PLAN.md")]
        public static void FewerThanOneSurvivorCountsAsAMiss()
        {
            // never a partial write (F85.4)
            Assert.Fail("pending T72");
        }

        [Fact(Skip = "Pending T72 — see docs/PLAN.md")]
        public static void WrongShapedOutputCountsAsAMiss()
        {
            Assert.Fail("pending T72");
        }
    }
}
