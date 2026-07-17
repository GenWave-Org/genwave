// STORY-157 — Year lookup finds the original recording (Epic Z / SPEC F60, closes gitea-#228).
//
// BDD specification — xUnit. Implemented Z4 (2026-07-15).
// MusicBrainzYearLookup.SelectYear: among ALL candidates passing the F48.2 confidence rule
// (score ≥ MinScore AND normalized artist match), pick the OLDEST earliest-release year —
// replacing best-score-first. Canned payloads ONLY (no live WAN call in CI, F60.3); every other
// F48 rule stands (F60.2).
//
// ScenarioOldestQualifyingYearWins is the client-level half: unit facts against the real
// MusicBrainzYearLookup backed by canned MusicBrainz JSON via a fake HttpMessageHandler — mirrors
// Story144_MusicBrainzYearLookup.cs's BuildLookup idiom exactly, no database involved.
//
// ScenarioEveryOtherF48RuleStands is the pipeline half: real Postgres via DatabaseFixture, the SAME
// production MusicBrainzYearLookup (not a FakeYearLookup stand-in) wired to a canned payload and run
// through EnrichmentService — mirrors Story144_YearLookupPipeline.cs, proving the F48.3/F48.4
// stamping and never-overwrite claim-predicate guarantees survive the F60.1 comparator change.

using System.Net;
using System.Text;
using Dapper;
using Microsoft.Extensions.Options;
using GenWave.MediaLibrary.Catalog;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Tests.Fakes;
using GenWave.MediaLibrary.YearLookup;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureYearLookupOldestQualifying
{
    // ─────────────────────────────────────────────────────────────────────────
    // HAPPY PATH — the comparator
    // ─────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioOldestQualifyingYearWins
    {
        [Fact]
        public async Task TheOldestQualifyingYearIsSelectedOverAHigherScoringLaterRecording()
        {
            // Both candidates qualify (score >= MinScore=90, artist matches) — the OLDEST year wins
            // regardless of relative score above the floor (F60.1), not the score-100 candidate.
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 100,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1990" }]
                    },
                    {
                      "score": 91,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1965" }]
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture);
            var year = await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.Equal(1965, year);
        }

        [Fact]
        public async Task TheQueenFixtureWritesNineteenSeventyFive()
        {
            // The gitea-#228 finding, pinned verbatim (F60.3): a later re-recording scores a perfect 100 —
            // MusicBrainz's own confidence — but the 1975 original still clears MinScore (90), so it
            // qualifies too. Best-score-first used to write 1993; F60.1 must write 1975.
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 100,
                      "title": "Bohemian Rhapsody",
                      "artist-credit": [{ "name": "Queen", "artist": { "name": "Queen" } }],
                      "releases": [{ "date": "1993-11-01" }],
                      "first-release-date": "1993-11-01"
                    },
                    {
                      "score": 92,
                      "title": "Bohemian Rhapsody",
                      "artist-credit": [{ "name": "Queen", "artist": { "name": "Queen" } }],
                      "releases": [{ "date": "1975-10-31" }],
                      "first-release-date": "1975-10-31"
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture);
            var year = await lookup.TryLookupAsync("Queen", "Bohemian Rhapsody", null, CancellationToken.None);

            Assert.Equal(1975, year);
        }

        [Fact]
        public async Task ATieOnYearSelectsDeterministically()
        {
            // Two qualifying candidates tie on the oldest year (1975); a third, higher-scoring
            // candidate is a distractor at a YOUNGER year (1990) and must still lose to the 1975 tie.
            // Pinned (F60.1): tied-oldest candidates are value-identical on the thing SelectYear
            // returns (a year, not a recording), so the tie is inherently deterministic — no
            // tie-break policy is needed. This fact pins that the result is always the shared oldest
            // year, 1975, never null and never the younger distractor's 1990.
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 100,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1990" }]
                    },
                    {
                      "score": 90,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1975-01-01" }]
                    },
                    {
                      "score": 95,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1975-06-15" }]
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture);
            var year = await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.Equal(1975, year);
        }

        [Fact]
        public async Task ADatelessQualifyingCandidateNeverBlocksAResolvableOne()
        {
            // The score-100 candidate qualifies (>= MinScore 90, artist matches) but carries NO
            // parseable date anywhere — no release dates, no first-release-date — so it has nothing
            // to offer "oldest" and is skipped from the comparison entirely (deliberate F60.1 reading,
            // ruled acceptable at review). The score-92 candidate, dated 1980, is the only one left
            // standing and its year is what gets written — never null.
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 100,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }]
                    },
                    {
                      "score": 92,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1980" }]
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture);
            var year = await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.Equal(1980, year);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioEveryOtherF48RuleStands(DatabaseFixture db)
    {
        [Fact]
        public async Task NonQualifyingCandidatesNeverContributeAYearEvenIfOldest()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");

            // The 1950 candidate is OLDER but scores below MinScore (90) — it must never contribute
            // a year, even under the new oldest-wins comparator. Only the qualifying 1980 candidate
            // may win.
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 50,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1950" }]
                    },
                    {
                      "score": 95,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1980" }]
                    }
                  ]
                }
                """;
            var (lookup, handler) = BuildRealLookup(fixture);
            var svc = Harness.BackfillYearLookupWith(repo, lookup);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            Assert.Single(handler.Requests);
            var row = await SelectYearRowAsync(db, id);
            Assert.Equal(1980, row.Year);
            Assert.NotNull(row.YearLookupAt);
        }

        [Fact]
        public async Task NoQualifierStillSkipsAndStamps()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters");

            const string fixture = """{ "recordings": [] }""";
            var (lookup, handler) = BuildRealLookup(fixture);
            var svc = Harness.BackfillYearLookupWith(repo, lookup);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            var row = await SelectYearRowAsync(db, id);
            Assert.Null(row.Year);
            Assert.NotNull(row.YearLookupAt);   // attempted and stamped despite no confident match

            // The sentinel means this row is no longer claimed — a second tick issues no new request
            // (no retry storm, F48.3).
            await svc.BackfillYearLookupAsync(CancellationToken.None);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task AnExistingYearIsStillNeverOverwritten()
        {
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var id = await SeedRowAsync(repo, artist: "The Testers", title: "Testing Waters", year: 2001);

            // A payload that would readily qualify with a much older year, IF the row were ever
            // claimed — proving the claim predicate (year IS NULL), not the comparator, is what
            // guards F48.4, and F60.1's oldest-year preference cannot reach an already-filled row.
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 100,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1970" }]
                    }
                  ]
                }
                """;
            var (lookup, handler) = BuildRealLookup(fixture);
            var svc = Harness.BackfillYearLookupWith(repo, lookup);

            await svc.BackfillYearLookupAsync(CancellationToken.None);

            Assert.Empty(handler.Requests);   // never claimed, never even asked
            var row = await SelectYearRowAsync(db, id);
            Assert.Equal(2001, row.Year);
            Assert.Null(row.YearLookupAt);
        }

        // ---------------------------------------------------------------------
        // Helpers — mirrors Story144_YearLookupPipeline.cs's SeedRowAsync/SelectYearRowAsync;
        // duplicated locally because those helpers are private to that file's scenario classes.
        // ---------------------------------------------------------------------

        sealed class YearRow
        {
            public int? Year { get; set; }
            public DateTime? YearLookupAt { get; set; }
        }

        static async Task<YearRow> SelectYearRowAsync(DatabaseFixture f, long id)
        {
            await using var conn = await f.DataSource.OpenConnectionAsync();
            return await conn.QuerySingleAsync<YearRow>(
                "select year, year_lookup_at from library.media where id = @id", new { id });
        }

        static async Task<long> SeedRowAsync(MediaRepository repo, string? artist, string? title, int? year = null)
        {
            var path = $"/synthetic/{Guid.NewGuid():N}.flac";
            var id = await repo.InsertDiscoveredAsync(path, "flac", 100, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(
                id, Harness.ReadyResultWith(artist: artist, title: title) with { Year = year },
                CancellationToken.None);
            return id;
        }

        /// <summary>
        /// Builds the REAL production <see cref="MusicBrainzYearLookup"/> against a canned
        /// MusicBrainz JSON payload — this scenario's whole point is proving the pipeline's stamping
        /// and never-overwrite behavior survive the F60.1 comparator change, so a <c>FakeYearLookup</c>
        /// stand-in (which bypasses <c>SelectYear</c> entirely) would not do.
        /// </summary>
        static (MusicBrainzYearLookup Lookup, FakeHttpMessageHandler Handler) BuildRealLookup(string responseJson)
        {
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            }));
            var http = new HttpClient(handler);
            IOptionsMonitor<YearLookupOptions> options = new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions());
            return (new MusicBrainzYearLookup(http, options), handler);
        }
    }

    /// <summary>
    /// Builds a <see cref="MusicBrainzYearLookup"/> whose <see cref="HttpClient"/> is backed by a
    /// <see cref="FakeHttpMessageHandler"/> that returns 200 with <paramref name="responseJson"/> for
    /// every request. Default options (MinScore 90) apply. Mirrors
    /// Story144_MusicBrainzYearLookup.cs's own BuildLookup helper.
    /// </summary>
    static MusicBrainzYearLookup BuildLookup(string responseJson)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        }));
        var http = new HttpClient(handler);
        IOptionsMonitor<YearLookupOptions> options = new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions());
        return new MusicBrainzYearLookup(http, options);
    }
}
