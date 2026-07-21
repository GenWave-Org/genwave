// STORY-200 — MusicBrainz etiquette: throttle, descriptive User-Agent, miss-stamping
//
// BDD specification — xUnit (SPEC F76.1-F76.2). Extends the MusicBrainzYearLookup seam (Story144).
//
// F76.1 (throttle + identity) is a fake-handler unit spec — Microsoft.Extensions.Time.Testing's
// FakeTimeProvider drives MusicBrainzRateLimiter's clock directly, so the 1 req/s ceiling is
// asserted deterministically: the fake clock only moves when the test calls Advance, so a pending
// second/third request can be proven "still parked behind the gate" without ever sleeping for real
// wall-clock time.
//
// F76.2 (miss-stamping) is a real-Postgres Integration fact (the Story194 idiom) driving the actual
// MusicBrainzYearLookup client — behind a fake HTTP handler, never the network (F48.7) — through
// EnrichmentService.BackfillYearLookupAsync twice, proving the second pass makes zero HTTP calls.

using System.Net;
using System.Reflection;
using System.Text;
using Dapper;
using Microsoft.Extensions.Time.Testing;
using GenWave.MediaLibrary.Options;
using GenWave.MediaLibrary.Tests.Fakes;
using GenWave.MediaLibrary.YearLookup;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureMusicBrainzEtiquette
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static HttpResponseMessage EmptyRecordingsResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{ "recordings": [] }""", Encoding.UTF8, "application/json"),
    };

    static string ExpectedUserAgent() =>
        $"GenWave/{typeof(MusicBrainzYearLookup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown"} " +
        "(+https://github.com/GenWave-Org/genwave)";

    // ---------------------------------------------------------------------
    // HAPPY PATH — throttle + identity (F76.1, AC1)
    // ---------------------------------------------------------------------

    public sealed class ScenarioThrottleAndIdentity
    {
        [Fact]
        public async Task ASecondLookupNeverStartsBeforeOneFakeSecondHasElapsed()
        {
            // Given a fake clock, a shared rate limiter over it, and a MusicBrainz client whose HTTP
            // calls are traced against that same fake clock...
            var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var requestTimes = new List<DateTimeOffset>();
            var handler = new FakeHttpMessageHandler((_, _) =>
            {
                requestTimes.Add(fakeTime.GetUtcNow());
                return Task.FromResult(EmptyRecordingsResponse());
            });
            var lookup = new MusicBrainzYearLookup(
                new HttpClient(handler),
                new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()),
                new MusicBrainzRateLimiter(fakeTime));

            // When the first lookup completes...
            await lookup.TryLookupAsync("Artist One", "Song One", null, CancellationToken.None);
            Assert.Single(requestTimes);

            // ...and a second lookup is started immediately after, with the fake clock un-advanced...
            var second = lookup.TryLookupAsync("Artist Two", "Song Two", null, CancellationToken.None);

            // Then its HTTP request has NOT been sent yet — it is parked behind the shared gate.
            Assert.Single(requestTimes);

            // Only once the fake clock is advanced by the full 1s minimum interval does it proceed.
            fakeTime.Advance(MusicBrainzRateLimiter.MinInterval);
            await second;
            Assert.Equal(2, requestTimes.Count);
            Assert.Equal(requestTimes[0] + MusicBrainzRateLimiter.MinInterval, requestTimes[1]);
        }

        [Fact]
        public async Task ABatchOfLookupsNeverExceedsOneRequestPerSecondOfFakeTime()
        {
            // Given the same fake-clock-traced client as above, primed for a batch of three lookups...
            var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var requestTimes = new List<DateTimeOffset>();
            var handler = new FakeHttpMessageHandler((_, _) =>
            {
                requestTimes.Add(fakeTime.GetUtcNow());
                return Task.FromResult(EmptyRecordingsResponse());
            });
            var lookup = new MusicBrainzYearLookup(
                new HttpClient(handler),
                new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()),
                new MusicBrainzRateLimiter(fakeTime));

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // When three lookups run back to back, advancing the fake clock by the minimum interval
            // between each pending pair rather than ever sleeping for real time...
            await lookup.TryLookupAsync("Artist One", "Song One", null, CancellationToken.None);

            var second = lookup.TryLookupAsync("Artist Two", "Song Two", null, CancellationToken.None);
            fakeTime.Advance(MusicBrainzRateLimiter.MinInterval);
            await second;

            var third = lookup.TryLookupAsync("Artist Three", "Song Three", null, CancellationToken.None);
            fakeTime.Advance(MusicBrainzRateLimiter.MinInterval);
            await third;

            sw.Stop();

            // Then the whole batch completed without ever waiting out a real multi-second sleep — the
            // deterministic point of driving the gate with a fake clock in the first place.
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"expected no real sleep under a fake clock; took {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task EveryRequestCarriesTheDescriptiveUserAgent()
        {
            // Given a MusicBrainz client behind a fake handler that captures every request...
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(EmptyRecordingsResponse()));
            var lookup = new MusicBrainzYearLookup(
                new HttpClient(handler),
                new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions()),
                new MusicBrainzRateLimiter(TimeProvider.System));

            // When a lookup is performed...
            await lookup.TryLookupAsync("The Testers", "Testing Waters", null, CancellationToken.None);

            // Then the request carries "GenWave/<build-stamped version> (+repo)" — never a hardcoded
            // literal that could silently go stale (SPEC F65.1, F76.1).
            var request = Assert.Single(handler.Requests);
            Assert.Equal(ExpectedUserAgent(), request.Headers.UserAgent.ToString());
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — misses stamped, never re-queried (F76.2, AC2)
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioMissStamping(DatabaseFixture db)
    {
        [Fact]
        public async Task SecondPassOverAMissStampedLibraryIssuesZeroMusicBrainzCalls()
        {
            // Given a ready, yearless, tagged row eligible for a MusicBrainz lookup, and a fake
            // endpoint that always reports "no confident match" for it...
            await db.ResetAsync();
            var repo = Harness.Repo(db);
            var path = $"/synthetic/{Guid.NewGuid():N}.flac";
            var id = await repo.InsertDiscoveredAsync(path, "flac", 100, Harness.Mtime, CancellationToken.None);
            await repo.WriteEnrichmentAsync(
                id,
                Harness.ReadyResultWith(artist: "The Testers", title: "Testing Waters") with { Year = null },
                CancellationToken.None);

            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(EmptyRecordingsResponse()));
            var yearLookupOptions = new FakeOptionsMonitor<YearLookupOptions>(new YearLookupOptions());
            var yearLookup = new MusicBrainzYearLookup(
                new HttpClient(handler), yearLookupOptions, new MusicBrainzRateLimiter(TimeProvider.System));
            var svc = Harness.BackfillYearLookupWith(repo, yearLookup, yearLookupOptions);

            // When the first enrichment pass runs over the row...
            await svc.BackfillYearLookupAsync(CancellationToken.None);

            // Then MusicBrainz is called once and the row's miss sentinel is stamped.
            Assert.Single(handler.Requests);
            Assert.NotNull(await YearLookupMissedAtOfAsync(db, id));

            // When a second enrichment pass runs over the now miss-stamped library...
            await svc.BackfillYearLookupAsync(CancellationToken.None);

            // Then it issues zero further MusicBrainz calls for the stamped row.
            Assert.Single(handler.Requests);
        }

        static async Task<DateTime?> YearLookupMissedAtOfAsync(DatabaseFixture f, long id)
        {
            await using var conn = await f.DataSource.OpenConnectionAsync();
            return await conn.ExecuteScalarAsync<DateTime?>(
                "select year_lookup_missed_at from library.media where id = @id", new { id });
        }
    }
}
