// STORY-216 — Tracks get moods from a bounded vocabulary
//
// BDD specification — xUnit (SPEC F85.1–F85.5). PLAN T58 ships the vocabulary + column + the
// validating write path (ScenarioBoundedTagging's DB-backed facts below); T72 wires the tagger
// batch (the remaining facts in this file). LLM interaction is faked at the HttpMessageHandler
// boundary (Story187 idiom, mirrors Story144_MusicBrainzYearLookup) — real Postgres for stamps.

using System.Net;
using System.Net.Http.Json;
using Dapper;
using GenWave.Abstractions.Playout;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Enrich;
using GenWave.MediaLibrary.Tests.Fakes;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMoodTagEnrichment
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Inline DTO for querying mood-tag-relevant columns directly — mirrors Story144's own YearRow.</summary>
    sealed class MoodRow
    {
        public string[]? Moods { get; set; }
        public DateTime? MoodTaggedAt { get; set; }
        public DateTime? MoodTagMissedAt { get; set; }
    }

    static async Task<MoodRow> SelectMoodRowAsync(DatabaseFixture db, long id)
    {
        await using var conn = await db.DataSource.OpenConnectionAsync();
        return await conn.QuerySingleAsync<MoodRow>(
            "select moods, mood_tagged_at, mood_tag_missed_at from library.media where id = @id", new { id });
    }

    /// <summary>
    /// A fake chat-completions endpoint returning the SAME <paramref name="rawContent"/> for every
    /// request, regardless of which track it was asked about.
    /// </summary>
    static FakeHttpMessageHandler MoodHandler(string rawContent) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { choices = new[] { new { message = new { content = rawContent } } } }),
        }));

    /// <summary>
    /// A fake chat-completions endpoint whose answer depends on which track the request names —
    /// keyed by a substring of the request's own user-prompt content (the tagger embeds
    /// <c>Title: {title}</c> verbatim) — so a single handler can serve a batch of more than one row
    /// with different outcomes. Each request body is appended to <paramref name="capturedBodies"/>
    /// (read here, inside the responder, rather than left for the caller to re-read afterwards —
    /// <see cref="OllamaMoodTagger"/> disposes its <see cref="HttpRequestMessage"/> once the call
    /// completes, so <c>FakeHttpMessageHandler.Requests[i].Content</c> is no longer readable by then).
    /// </summary>
    static FakeHttpMessageHandler MoodHandlerByTitle(
        IReadOnlyDictionary<string, string> responsesByTitle, List<string>? capturedBodies = null) =>
        new(async (request, ct) =>
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            capturedBodies?.Add(body);
            var (_, rawContent) = responsesByTitle.First(kv => body.Contains(kv.Key));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { choices = new[] { new { message = new { content = rawContent } } } }),
            };
        });

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioBoundedTagging(DatabaseFixture db)
    {
        // Arrange (T58): one ready row, seeded via the real enrichment write path (Story211 idiom).
        // WriteMoodsAsync is exercised directly against Postgres here — no fake LLM needed for the
        // write-path facts; T72 owns the tagger-batch arrangement (fake HttpMessageHandler) for the
        // remaining facts below.
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

        [Fact]
        public async Task ATaggedTrackCarriesOneToThreeMoods()
        {
            // F85.2: a completed tagger run assigns at least one and at most MaxMoodsPerTrack (3)
            // vocabulary terms — never zero (that is a miss, SadPathDegradationAndSprawl's own
            // concern) and never more than the cap (the parser truncates before the write path ever
            // sees it).
            var id = await SeedReadyAsync();
            var repo = Harness.Repo(db);
            var handler = MoodHandler("dreamy, warm, driving");
            var svc = Harness.BackfillMoodTagWith(repo, handler);

            await svc.BackfillMoodTagAsync(CancellationToken.None);

            var moods = await SelectMoodsAsync(db, id);
            Assert.NotNull(moods);
            Assert.InRange(moods.Length, 1, MoodVocabulary.MaxMoodsPerTrack);
            Assert.Equal(["dreamy", "warm", "driving"], moods);
        }

        [Fact]
        public async Task ASuccessIsStampedOnce()
        {
            // tagged-at stamp set; a second batch does not re-ask it (F85.2) — moods is no longer
            // null after the first tick, which is what ListMoodTagClaimsAsync's own gate excludes on.
            var id = await SeedReadyAsync();
            var repo = Harness.Repo(db);

            var firstHandler = MoodHandler("dreamy");
            var firstRun = Harness.BackfillMoodTagWith(repo, firstHandler);
            await firstRun.BackfillMoodTagAsync(CancellationToken.None);

            var afterFirst = await SelectMoodRowAsync(db, id);
            var moodsAfterFirst = afterFirst.Moods;
            Assert.NotNull(moodsAfterFirst);
            Assert.Equal(["dreamy"], moodsAfterFirst);
            Assert.NotNull(afterFirst.MoodTaggedAt);

            // A FRESH handler/request log for the second tick — if the row were reclaimed, its
            // Requests count would be non-zero.
            var secondHandler = MoodHandler("dreamy");
            var secondRun = Harness.BackfillMoodTagWith(repo, secondHandler);
            await secondRun.BackfillMoodTagAsync(CancellationToken.None);

            Assert.Empty(secondHandler.Requests);
            var moodsAfterSecond = (await SelectMoodRowAsync(db, id)).Moods;
            Assert.NotNull(moodsAfterSecond);
            Assert.Equal(["dreamy"], moodsAfterSecond);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMissesRetrySuccessesDoNot(DatabaseFixture db)
    {
        [Fact]
        public async Task OnlyMissesAreReAsked()
        {
            // the F76 MusicBrainz etiquette pattern applied to moods (F85.2): a batch containing one
            // stamped success and one stamped miss. A reenrichment sentinel reset (simulated here by
            // directly clearing mood_tag_missed_at, standing in for a future admin re-enrichment
            // action — T72 ships the claim-gate mechanism itself, not that admin endpoint) makes ONLY
            // the miss eligible again; the success stays excluded because its moods column is no
            // longer null, regardless of any sentinel state.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var hit = await repo.InsertDiscoveredAsync("/mood/hit.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(hit, Harness.ReadyResultWith(title: "Sunny Skies", artist: "The Testers"), CancellationToken.None);

            var miss = await repo.InsertDiscoveredAsync("/mood/miss.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(miss, Harness.ReadyResultWith(title: "Untitled Noise", artist: "Nobody"), CancellationToken.None);

            var firstHandler = MoodHandlerByTitle(new Dictionary<string, string>
            {
                ["Sunny Skies"] = "dreamy",
                ["Untitled Noise"] = "notavocabword",
            });
            var firstRun = Harness.BackfillMoodTagWith(repo, firstHandler);
            await firstRun.BackfillMoodTagAsync(CancellationToken.None);

            var hitRowAfterFirst = await SelectMoodRowAsync(db, hit);
            var hitMoodsAfterFirst = hitRowAfterFirst.Moods;
            Assert.NotNull(hitMoodsAfterFirst);
            Assert.Equal(["dreamy"], hitMoodsAfterFirst);
            Assert.Null(hitRowAfterFirst.MoodTagMissedAt);

            var missRowAfterFirst = await SelectMoodRowAsync(db, miss);
            Assert.Null(missRowAfterFirst.Moods);
            Assert.NotNull(missRowAfterFirst.MoodTagMissedAt);

            // Simulate the reenrichment reset — clears the miss's re-claim gate only.
            await using (var conn = await db.DataSource.OpenConnectionAsync())
                await conn.ExecuteAsync(
                    "update library.media set mood_tag_missed_at = null where id = @miss", new { miss });

            var secondBodies = new List<string>();
            var secondHandler = MoodHandlerByTitle(new Dictionary<string, string>
            {
                ["Sunny Skies"] = "warm",           // would prove a re-ask if the hit were reclaimed
                ["Untitled Noise"] = "epic",
            }, secondBodies);
            var secondRun = Harness.BackfillMoodTagWith(repo, secondHandler);
            await secondRun.BackfillMoodTagAsync(CancellationToken.None);

            // Only the miss's row was claimed — exactly one request, and it names the miss's track.
            var body = Assert.Single(secondBodies);
            Assert.Contains("Untitled Noise", body);

            var missMoodsAfterSecond = (await SelectMoodRowAsync(db, miss)).Moods;
            Assert.NotNull(missMoodsAfterSecond);
            Assert.Equal(["epic"], missMoodsAfterSecond);

            // The success from the first tick is untouched — still "dreamy", never re-asked.
            var hitMoodsAfterSecond = (await SelectMoodRowAsync(db, hit)).Moods;
            Assert.NotNull(hitMoodsAfterSecond);
            Assert.Equal(["dreamy"], hitMoodsAfterSecond);
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMoodsReachTastePredicates(DatabaseFixture db)
    {
        [Fact]
        public async Task AMoodTagPredicateMatchesTaggedTracks()
        {
            // F82.1's tag predicate itself (GenWave.Orchestration.TasteMatcher.MatchesTag) lives in a
            // project GenWave.MediaLibrary.Tests never references — so this proves the wire this
            // project actually owns end to end: a mood written through the real T58 write path
            // surfaces on the SAME GetEnvelopeCandidatePoolAsync/EnvelopeCandidateRow.Moods field
            // TasteMatcher reads (T63/T64), matching case-insensitively against a "dreamy" tag
            // predicate — "no parallel matching system" (F85.5), up to the project boundary.
            await db.ResetAsync();
            var repo = Harness.Repo(db);

            var id = await repo.InsertDiscoveredAsync("/mood/predicate.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true), CancellationToken.None);
            await repo.WriteMoodsAsync(id, ["dreamy"], CancellationToken.None);
            await repo.RecomputeEnergyPercentilesAsync(CancellationToken.None);

            var catalog = (IMediaCatalog)repo;
            var scope = new LibraryScope([1L]);

            var pool = await catalog.GetEnvelopeCandidatePoolAsync(
                scope, [], artistSeparation: 0, SegmentEnvelope.StationDefault, limit: 10, CancellationToken.None);

            var row = Assert.Single(pool);
            // TasteMatcher.MatchesTag's exact semantics: a tag predicate fires when it equals any of
            // the candidate's moods, case-insensitively.
            const string tagPredicate = "DREAMY";
            Assert.Contains(row.Moods, mood => string.Equals(mood, tagPredicate, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class SadPathDegradationAndSprawl(DatabaseFixture db)
    {
        async Task<long> SeedEligibleAsync()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await repo.InsertDiscoveredAsync("/mood/sad-path.flac", "flac", 1, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(id, Harness.ReadyResult(true), CancellationToken.None);
            return id;
        }

        [Fact]
        public async Task ADegradedLlmSkipsTheBatchWithOneLogLine()
        {
            // degraded/off/unconfigured ⇒ clean skip, single line, no per-track noise (F85.3) — an
            // eligible candidate exists (proving this is a genuine skip, not "nothing to do").
            var id = await SeedEligibleAsync();
            var repo = Harness.Repo(db);

            var handler = MoodHandler("dreamy");   // would succeed if ever called — it must not be
            var gate = new FakeLlmBatchGate(allowed: false, reason: "LLM degraded (Soft)");
            var logger = new CapturingLogger<EnrichmentService>();
            var svc = Harness.BackfillMoodTagWith(repo, handler, gate, logger);

            await svc.BackfillMoodTagAsync(CancellationToken.None);

            Assert.Empty(handler.Requests);
            Assert.Null((await SelectMoodRowAsync(db, id)).Moods);
            // Exactly ONE line for the whole batch — never per-track (this run had one eligible row;
            // even so, the gate short-circuits before any claim query, so there is nothing to iterate).
            var line = Assert.Single(logger.Informational);
            Assert.Contains("degraded", line, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UnknownTermsAreFilteredNotStored()
        {
            // F85.4 constrained-output: parse, filter to vocabulary — an unknown term never lands in
            // storage, while the valid terms alongside it still survive.
            var id = await SeedEligibleAsync();
            var repo = Harness.Repo(db);
            var handler = MoodHandler("moody, dreamy, epic");   // "moody" is not in the vocabulary
            var svc = Harness.BackfillMoodTagWith(repo, handler);

            await svc.BackfillMoodTagAsync(CancellationToken.None);

            var row = await SelectMoodRowAsync(db, id);
            var moods = row.Moods;
            Assert.NotNull(moods);
            Assert.Equal(["dreamy", "epic"], moods);
            Assert.DoesNotContain("moody", moods);
        }

        [Fact]
        public async Task FewerThanOneSurvivorCountsAsAMiss()
        {
            // never a partial write (F85.4): zero in-vocabulary survivors is a miss, not an empty
            // array written to the column.
            var id = await SeedEligibleAsync();
            var repo = Harness.Repo(db);
            var handler = MoodHandler("moody, funky");   // neither term is in the vocabulary
            var svc = Harness.BackfillMoodTagWith(repo, handler);

            await svc.BackfillMoodTagAsync(CancellationToken.None);

            var row = await SelectMoodRowAsync(db, id);
            Assert.Null(row.Moods);
            Assert.NotNull(row.MoodTaggedAt);
            Assert.NotNull(row.MoodTagMissedAt);
        }

        [Fact]
        public async Task WrongShapedOutputCountsAsAMiss()
        {
            // A non-conforming answer with no recognizable word at all (not just an unknown-but-word-
            // shaped answer, distinct from the fixture above) collapses to the same miss outcome.
            var id = await SeedEligibleAsync();
            var repo = Harness.Repo(db);
            var handler = MoodHandler("42 !!! ???");
            var svc = Harness.BackfillMoodTagWith(repo, handler);

            await svc.BackfillMoodTagAsync(CancellationToken.None);

            var row = await SelectMoodRowAsync(db, id);
            Assert.Null(row.Moods);
            Assert.NotNull(row.MoodTagMissedAt);
        }
    }
}
