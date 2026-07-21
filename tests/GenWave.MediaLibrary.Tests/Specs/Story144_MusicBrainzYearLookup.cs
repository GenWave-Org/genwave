// STORY-144 — Missing release years filled from MusicBrainz (Epic X / SPEC F48.1–F48.2,
// closes gitea-#208) — HTTP client half. The Core contract lives in
// Core.Tests/Specs/Story144_YearLookupContract.cs; the claim/pacing pipeline in
// Specs/Story144_YearLookupPipeline.cs (this project).
//
// BDD specification — xUnit. Authored PENDING at /plan time (2026-07-14, house rule since Epic S).
// House rule F48.7: every fact here runs against a fake HttpMessageHandler with fixture JSON — no
// test reaches the network; the ONE real MusicBrainz request is X10(c)'s sanctioned gate exception.

using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Tests.Fakes;
using GenWave.MediaLibrary.YearLookup;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMusicBrainzYearLookup
{
    public sealed class ScenarioAConfidentMatchYieldsTheEarliestReleaseYear
    {
        [Fact]
        public async Task AHighScoreMatchingArtistCandidateYieldsItsYear()
        {
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 95,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1975-09-12" }],
                      "first-release-date": "1975"
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture, out _);
            var year = await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.Equal(1975, year);
        }

        [Fact]
        public async Task TheEarliestOfSeveralReleaseDatesWins()
        {
            // Mixed date precisions ("YYYY", "YYYY-MM", "YYYY-MM-DD") — the earliest YEAR wins
            // regardless of which release entry carries the finest-grained date (F48.2).
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 95,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [
                        { "date": "1990" },
                        { "date": "1975-09-12" },
                        { "date": "1980-03" }
                      ]
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture, out _);
            var year = await lookup.TryLookupAsync("The Testers", "Testing Waters", "Test Album", CancellationToken.None);

            Assert.Equal(1975, year);
        }

        [Fact]
        public async Task ArtistComparisonIsCaseInsensitiveAndTrimmed()
        {
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 95,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1975" }]
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture, out _);
            // The row's tag value is untrimmed and differently cased — still a legal match (F48.2).
            var year = await lookup.TryLookupAsync("  the testers  ", "Testing Waters", null, CancellationToken.None);

            Assert.Equal(1975, year);
        }

        [Fact]
        public async Task RequestsCarryTheDescriptiveUserAgent()
        {
            // The exact User-Agent format/version derivation is STORY-200's own concern
            // (Specs/Story200_MusicBrainzEtiquette.cs, SPEC F76.1); this fact only pins that SOME
            // descriptive, GenWave-identifying header rides along, matching the STORY-144 contract.
            const string fixture = """{ "recordings": [] }""";

            var lookup = BuildLookup(fixture, out var requests);
            await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            var expectedVersion =
                typeof(MusicBrainzYearLookup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                ?? "unknown";
            var request = Assert.Single(requests);
            Assert.Equal(
                $"GenWave/{expectedVersion} (+https://github.com/GenWave-Org/genwave)",
                request.Headers.UserAgent.ToString());
        }

        [Fact]
        public async Task EmbeddedQuotesInTagValuesAreLuceneEscapedInTheQuery()
        {
            // A tag like `He said "hi"` must not break the lucene query syntax — embedded quotes
            // (and backslashes) are backslash-escaped before the value is wrapped in its own quoted
            // phrase. URL-encoding of the whole query string is a separate, later concern (the
            // request is built via QueryHelpers.AddQueryString), so the query param is decoded here
            // before asserting on the lucene-level escaping.
            const string fixture = """{ "recordings": [] }""";

            var lookup = BuildLookup(fixture, out var requests);
            await lookup.TryLookupAsync("The \"Quoted\" Band", "Say \"Hi\"", null, CancellationToken.None);

            var request = Assert.Single(requests);
            Assert.NotNull(request.RequestUri);
            var decodedQuery = QueryHelpers.ParseQuery(request.RequestUri.Query)["query"].ToString();

            Assert.Equal(
                "artist:\"The \\\"Quoted\\\" Band\" AND recording:\"Say \\\"Hi\\\"\"",
                decodedQuery);
        }
    }

    // ── Sad path ────────────────────────────────────────────────────────────────────────────────

    public sealed class ScenarioLowConfidenceAndFailureYieldNull
    {
        [Fact]
        public async Task ABelowThresholdScoreYieldsNull()
        {
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 50,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1975" }]
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture, out _);
            var year = await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.Null(year);
        }

        [Fact]
        public async Task AnArtistMismatchYieldsNullDespiteAHighScore()
        {
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 100,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "Somebody Else", "artist": { "name": "Somebody Else" } }],
                      "releases": [{ "date": "1975" }]
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture, out _);
            var year = await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.Null(year);
        }

        [Fact]
        public async Task MalformedJsonYieldsNull()
        {
            const string fixture = "{ this is not json ";

            var lookup = BuildLookup(fixture, out _);
            var year = await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.Null(year);
        }

        [Fact]
        public async Task ATimeoutYieldsNullWithinTheBoundedBudget()
        {
            // Options timeout is shortened to 1s; the handler delays 5s but respects the linked
            // token, so the fact resolves in ~1s, not 5s — the timeout CTS firing must surface as
            // null, never an exception past the boundary (F48.1).
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{ "recordings": [] }""", Encoding.UTF8, "application/json"),
                };
            });
            var http = new HttpClient(handler);
            var options = new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions { TimeoutSeconds = 1 });
            var lookup = new MusicBrainzYearLookup(http, options, new MusicBrainzRateLimiter(TimeProvider.System));

            var year = await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.Null(year);
        }
    }

    // ── LastCallFailed diagnostic (X5, SPEC F48.5) ───────────────────────────────────────────────
    // Additive to the X4 client: distinguishes "no confident match" from "the endpoint call itself
    // could not complete" — the out-of-band signal EnrichmentService's backfill aggregates into a
    // WARN-once-per-tick (see MediaLibrary.Tests/Specs/Story144_YearLookupPipeline.cs).

    public sealed class ScenarioLastCallFailedDistinguishesEndpointOutageFromNoMatch
    {
        [Fact]
        public async Task ASuccessfulRoundTripClearsLastCallFailedRegardlessOfMatch()
        {
            // Below MinScore — a legal "no confident match", not a failure.
            const string fixture = """
                {
                  "recordings": [
                    {
                      "score": 50,
                      "title": "Testing Waters",
                      "artist-credit": [{ "name": "The Testers", "artist": { "name": "The Testers" } }],
                      "releases": [{ "date": "1975" }]
                    }
                  ]
                }
                """;

            var lookup = BuildLookup(fixture, out _);
            await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.False(lookup.LastCallFailed);
        }

        [Fact]
        public async Task ATimeoutSetsLastCallFailedTrue()
        {
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{ "recordings": [] }""", Encoding.UTF8, "application/json"),
                };
            });
            var http = new HttpClient(handler);
            var options = new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions { TimeoutSeconds = 1 });
            var lookup = new MusicBrainzYearLookup(http, options, new MusicBrainzRateLimiter(TimeProvider.System));

            await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            Assert.True(lookup.LastCallFailed);
        }
    }

    /// <summary>
    /// Builds a <see cref="MusicBrainzYearLookup"/> whose <see cref="HttpClient"/> is backed by a
    /// <see cref="FakeHttpMessageHandler"/> that returns 200 with <paramref name="responseJson"/> for
    /// every request; <paramref name="requests"/> receives every captured request so a fact can
    /// assert on it. Default options (MinScore 90) apply — override per-fact only where needed.
    /// </summary>
    static MusicBrainzYearLookup BuildLookup(string responseJson, out IReadOnlyList<HttpRequestMessage> requests)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        }));
        requests = handler.Requests;

        var http = new HttpClient(handler);
        IOptionsMonitor<YearLookupOptions> options = new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions());
        return new MusicBrainzYearLookup(http, options, new MusicBrainzRateLimiter(TimeProvider.System));
    }
}
