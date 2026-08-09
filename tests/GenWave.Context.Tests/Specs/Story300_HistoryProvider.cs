// STORY-300 — This day in history, honestly (F109, gh-#382)
using System.Net;
using System.Text;
using GenWave.Context.History;
using GenWave.Context.Tests.Fakes;
using GenWave.Core.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GenWave.Context.Tests.Specs;

public static class FeatureHistoryProvider
{
    // A real On-This-Day "selected" reply is field-shaped { text, year, pages: [...] } per entry (curl'd
    // from api.wikimedia.org/feed/v1/wikipedia/en/onthisday/selected/08/08 during T228 development) —
    // this fixture keeps the real field names/types (text/year) and an empty pages array (never
    // deserialized by WikimediaSelectedEvent) rather than the full, much larger real payload. Five
    // entries — one more than SPEC F109.1's 2-4 segment cap, so ScenarioParaphraseNeverInvention proves
    // the trim actually happens.
    const string RealShapeFixture = """
        {
          "selected": [
            { "text": "The World Health Organization declared the Western African Ebola epidemic a public health emergency.", "year": 2014, "pages": [] },
            { "text": "A massive mudslide struck the Chinese province of Gansu.", "year": 2010, "pages": [] },
            { "text": "A tour helicopter and a small airplane collided over the Hudson River.", "year": 2009, "pages": [] },
            { "text": "A EuroCity train derailed near Studenka station.", "year": 2008, "pages": [] },
            { "text": "The Iranian consulate in Mazar-i-Sharif was raided by Taliban leaders.", "year": 1998, "pages": [] }
          ]
        }
        """;

    // A fixed UTC instant so "today"/"tomorrow" (station-local, FakeTimeProvider's LocalTimeZone
    // defaulting to UTC) never depend on the wall clock the test happened to run at.
    static readonly DateTimeOffset FixedNow = new(2026, 8, 8, 10, 0, 0, TimeSpan.Zero);
    const string TodayFile = "08-08.json";
    const string TomorrowFile = "08-09.json";

    static (HistoryContextProvider Provider, FakeHttpMessageHandler Handler, string CacheRoot, FakeTimeProvider Time)
        Build(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, string? cacheRoot = null)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri(HistoryContextProvider.WikimediaBaseAddress) };
        var root = cacheRoot ?? Directory.CreateTempSubdirectory("genwave-history-tests-").FullName;
        var cacheRootProvider = new FakeContextCacheRootProvider { Root = root };
        var time = new FakeTimeProvider(FixedNow);
        var logger = new CapturingLogger<HistoryContextProvider>();
        var provider = new HistoryContextProvider(http, cacheRootProvider, time, logger);

        return (provider, handler, root, time);
    }

    static Task<HttpResponseMessage> RespondWith(string json) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    static string HistoryDir(string cacheRoot) => Path.Combine(cacheRoot, "context", "history");

    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioParaphraseNeverInvention : IDisposable
    {
        readonly (HistoryContextProvider Provider, FakeHttpMessageHandler Handler, string CacheRoot, FakeTimeProvider Time) build =
            Build((_, _) => RespondWith(RealShapeFixture));

        [Fact]
        public async Task SegmentFactsDeriveFromTheFetchedPayload()
        {
            // Every fact in ContextContent traces to a payload entry (year + text, verbatim) — no
            // synthesized events. Trimmed to the first 4 of the 5 fixture entries (SPEC F109.1's 2-4
            // segment cap); the patter fact is the single first entry, compact.
            var content = await build.Provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            const string Expected =
                "2014: The World Health Organization declared the Western African Ebola epidemic a public health emergency. · " +
                "2010: A massive mudslide struck the Chinese province of Gansu. · " +
                "2009: A tour helicopter and a small airplane collided over the Hudson River. · " +
                "2008: A EuroCity train derailed near Studenka station.";
            Assert.Equal(Expected, content.SegmentFacts);
            Assert.Equal(
                "2014: The World Health Organization declared the Western African Ebola epidemic a public health emergency.",
                content.PatterFact);
            Assert.DoesNotContain("Mazar-i-Sharif", content.SegmentFacts); // The 5th entry never made the cut.
        }

        public void Dispose() => Directory.Delete(build.CacheRoot, recursive: true);
    }

    public sealed class ScenarioDayFileCache : IDisposable
    {
        readonly List<string> cacheRoots = [];

        (HistoryContextProvider Provider, FakeHttpMessageHandler Handler, string CacheRoot, FakeTimeProvider Time) NewBuild()
        {
            var built = Build((_, _) => RespondWith(RealShapeFixture));
            cacheRoots.Add(built.CacheRoot);
            return built;
        }

        [Fact]
        public async Task AFetchedDayPersistsAsAJsonFile()
        {
            var build = NewBuild();

            await build.Provider.FetchAsync(CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(HistoryDir(build.CacheRoot), TodayFile)));
        }

        [Fact]
        public async Task ACacheHitCostsZeroNetwork()
        {
            var build = NewBuild();

            await build.Provider.FetchAsync(CancellationToken.None); // Cache miss: fetches today + pre-fetches tomorrow.
            var requestCountAfterFirstFetch = build.Handler.Requests.Count;
            Assert.NotEqual(0, requestCountAfterFirstFetch);

            // Second ask for the same day: today AND tomorrow are both already cached — zero HTTP calls.
            await build.Provider.FetchAsync(CancellationToken.None);

            Assert.Equal(requestCountAfterFirstFetch, build.Handler.Requests.Count);
        }

        [Fact]
        public async Task TheNextDayPreFetches()
        {
            var build = NewBuild();

            await build.Provider.FetchAsync(CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(HistoryDir(build.CacheRoot), TomorrowFile)));
        }

        [Fact]
        public async Task OldDayFilesSweep()
        {
            var build = NewBuild();
            var historyDir = HistoryDir(build.CacheRoot);
            Directory.CreateDirectory(historyDir);

            // A day file for a date far from today/tomorrow, written well outside any sane retention
            // horizon — 400 days comfortably clears "old" under any reasonable policy choice while
            // never colliding with TodayFile/TomorrowFile, which this same fetch writes fresh.
            var staleFilePath = Path.Combine(historyDir, "01-01.json");
            await File.WriteAllTextAsync(staleFilePath, """{"Entries":[{"Year":2000,"Text":"stale"}]}""");
            File.SetLastWriteTimeUtc(staleFilePath, build.Time.GetUtcNow().UtcDateTime.AddDays(-400));

            await build.Provider.FetchAsync(CancellationToken.None); // Any successful fetch sweeps.

            Assert.False(File.Exists(staleFilePath));
        }

        [Fact]
        public async Task OrphanedTempFilesFromAFailedWriteAreAlsoSwept()
        {
            // F6 fix, T228 review: the sweep's old "*.json"-only glob never matched
            // WriteCacheAsync's own "{path}.{guid}.tmp" temp file — an orphan left behind by a crash
            // mid-write would otherwise sit forever, never reclaimed. Same shape/naming as a real
            // WriteCacheAsync temp file, aged past the retention horizon exactly like a stale day file.
            var build = NewBuild();
            var historyDir = HistoryDir(build.CacheRoot);
            Directory.CreateDirectory(historyDir);

            var orphanTempPath = Path.Combine(historyDir, $"01-01.json.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(orphanTempPath, "partial write, never completed");
            File.SetLastWriteTimeUtc(orphanTempPath, build.Time.GetUtcNow().UtcDateTime.AddDays(-400));

            await build.Provider.FetchAsync(CancellationToken.None); // Any successful fetch sweeps.

            Assert.False(File.Exists(orphanTempPath));
        }

        public void Dispose()
        {
            foreach (var root in cacheRoots)
                Directory.Delete(root, recursive: true);
        }
    }

    // F5 (T228 review): the version-stamped etiquette User-Agent and the exact outbound request URI —
    // documented behavior this file previously never pinned.
    public sealed class ScenarioEtiquette : IDisposable
    {
        readonly (HistoryContextProvider Provider, FakeHttpMessageHandler Handler, string CacheRoot, FakeTimeProvider Time) build =
            Build((_, _) => RespondWith(RealShapeFixture));

        [Fact]
        public async Task TheRequestCarriesTheVersionStampedUserAgent()
        {
            await build.Provider.FetchAsync(CancellationToken.None);

            var request = build.Handler.Requests[0];
            var userAgent = request.Headers.UserAgent.ToString();
            Assert.StartsWith("GenWave/", userAgent);
            Assert.EndsWith("(+https://github.com/GenWave-Org/genwave)", userAgent);
        }

        [Fact]
        public async Task ValidFetchPinsTheExactOutboundRequestUri()
        {
            // FixedNow is 2026-08-08 (station-local, UTC default) — "selected/{MM}/{dd}", never a
            // parsed or caller-supplied path segment (this class's own remarks).
            await build.Provider.FetchAsync(CancellationToken.None);

            var request = build.Handler.Requests[0];
            Assert.NotNull(request.RequestUri);
            Assert.Equal(
                "https://api.wikimedia.org/feed/v1/wikipedia/en/onthisday/selected/08/08",
                request.RequestUri.GetLeftPart(UriPartial.Path));
        }

        public void Dispose() => Directory.Delete(build.CacheRoot, recursive: true);
    }

    // F5 (T228 review): the IStationClockProvider arm (this class's own remarks) actually exercised —
    // every prior fact in this file left stationClock at its default null, so TimeProvider.LocalTimeZone
    // was the only arm ever proven.
    public sealed class ScenarioStationClock : IDisposable
    {
        readonly string cacheRoot = Directory.CreateTempSubdirectory("genwave-history-tests-").FullName;

        [Fact]
        public async Task AConfiguredStationClockDrivesWhichDayIsFetchedAndCached()
        {
            // TimeProvider's own UTC "now" resolves to FixedNow's date, 2026-08-08; the station clock
            // is deliberately a day AHEAD of it, so a bug that read TimeProvider instead of the station
            // clock would fetch/cache 08-08, not 08-09 — this fact tells the two apart.
            var handler = new FakeHttpMessageHandler((_, _) => RespondWith(RealShapeFixture));
            var http = new HttpClient(handler) { BaseAddress = new Uri(HistoryContextProvider.WikimediaBaseAddress) };
            var cacheRootProvider = new FakeContextCacheRootProvider { Root = cacheRoot };
            var time = new FakeTimeProvider(FixedNow);
            var stationClock = new FakeStationClockProvider(new DateTimeOffset(2026, 8, 9, 3, 0, 0, TimeSpan.Zero));
            var provider = new HistoryContextProvider(
                http, cacheRootProvider, time, new CapturingLogger<HistoryContextProvider>(), stationClock);

            await provider.FetchAsync(CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(HistoryDir(cacheRoot), "08-09.json"))); // the station's date
            Assert.False(File.Exists(Path.Combine(HistoryDir(cacheRoot), TodayFile))); // never TimeProvider's date
            Assert.Contains(
                handler.Requests, r => r.RequestUri!.AbsoluteUri.Contains("onthisday/selected/08/09", StringComparison.Ordinal));
        }

        public void Dispose() => Directory.Delete(cacheRoot, recursive: true);
    }

    // ---------------------------------------------------------------------
    // SAD PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioOutages : IDisposable
    {
        readonly List<string> cacheRoots = [];

        string NewCacheRoot()
        {
            var root = Directory.CreateTempSubdirectory("genwave-history-tests-").FullName;
            cacheRoots.Add(root);
            return root;
        }

        [Fact]
        public async Task UnreachableWikimediaWithACachedFileStillServes()
        {
            var cacheRoot = NewCacheRoot();
            var historyDir = HistoryDir(cacheRoot);
            Directory.CreateDirectory(historyDir);
            await File.WriteAllTextAsync(
                Path.Combine(historyDir, TodayFile),
                """{"Entries":[{"Year":1969,"Text":"The first cash-dispensing ATM opened."}]}""");

            var build = Build((_, _) => throw new HttpRequestException("simulated Wikimedia outage"), cacheRoot);

            var content = await build.Provider.FetchAsync(CancellationToken.None);

            Assert.NotNull(content);
            Assert.Equal("1969: The first cash-dispensing ATM opened.", content.SegmentFacts);
        }

        [Fact]
        public async Task UnreachableWikimediaWithNoCacheReturnsNull()
        {
            // No file, no network ⇒ null (skip semantics; one Information line upstream, at the
            // ContextPipeline level — pinned in Story296_ContextPipeline.cs, not re-proven here).
            var build = Build((_, _) => throw new HttpRequestException("simulated Wikimedia outage"), NewCacheRoot());

            var content = await build.Provider.FetchAsync(CancellationToken.None);

            Assert.Null(content);
        }

        [Fact]
        public async Task ACorruptDayFileIsTreatedAsAbsentAndDeleted()
        {
            // F5 (T228 review) — this class's own documented contract: "a file that exists but fails
            // to parse ... is DEFENSIVE — logged once and deleted so the next write starts clean —
            // never a thrown exception". Network also down here so the corrupt file's deletion is
            // observable on its own — a healthy re-fetch would otherwise just overwrite it, masking
            // whether the delete itself actually ran.
            var cacheRoot = NewCacheRoot();
            var historyDir = HistoryDir(cacheRoot);
            Directory.CreateDirectory(historyDir);
            var todayPath = Path.Combine(historyDir, TodayFile);
            await File.WriteAllTextAsync(todayPath, "{ this is not valid json");

            var build = Build((_, _) => throw new HttpRequestException("simulated Wikimedia outage"), cacheRoot);

            var content = await build.Provider.FetchAsync(CancellationToken.None);

            Assert.Null(content); // Treated as absent, not served — and the network is down too.
            Assert.False(File.Exists(todayPath)); // The corrupt file itself was deleted, not left behind.
        }

        public void Dispose()
        {
            foreach (var root in cacheRoots)
                Directory.Delete(root, recursive: true);
        }
    }

    // F5 (T228 review): a blank cache root ⇒ off — zero outbound requests, one Information line —
    // mirrors Story299_WeatherProvider.ScenarioFailClosedOnConfiguration one seam over.
    public sealed class ScenarioFailClosedOnConfiguration
    {
        [Fact]
        public async Task EnabledWithBlankCacheRootNeverFetches()
        {
            var handler = new FakeHttpMessageHandler((_, _) => RespondWith(RealShapeFixture));
            var http = new HttpClient(handler) { BaseAddress = new Uri(HistoryContextProvider.WikimediaBaseAddress) };
            var cacheRootProvider = new FakeContextCacheRootProvider(); // Root defaults to "" — never wired.
            var logger = new CapturingLogger<HistoryContextProvider>();
            var provider = new HistoryContextProvider(http, cacheRootProvider, new FakeTimeProvider(FixedNow), logger);

            var content = await provider.FetchAsync(CancellationToken.None);

            Assert.Null(content);
            Assert.Empty(handler.Requests);
            Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Information);
        }
    }

    // F5 (T228 review): ISelfGatingContextProvider.IsAvailable is internal to GenWave.Context — this
    // project has no compile-time visibility onto it — so its behavior is proven the way
    // Story299_WeatherProvider.ScenarioRealPipelineHarness already proves Weather's: through the real
    // ContextPipeline, which is what actually calls it in production, never by casting to the
    // interface directly.
    public sealed class ScenarioRealPipelineHarness
    {
        [Fact]
        public async Task MisconfiguredCacheRootLogsExactlyOnceOverAMultiHourAdvance()
        {
            var handler = new FakeHttpMessageHandler((_, _) => RespondWith(RealShapeFixture));
            var http = new HttpClient(handler) { BaseAddress = new Uri(HistoryContextProvider.WikimediaBaseAddress) };
            var cacheRootProvider = new FakeContextCacheRootProvider(); // Never set — blank, misconfigured.
            var time = new FakeTimeProvider(FixedNow);
            var provider = new HistoryContextProvider(http, cacheRootProvider, time, new CapturingLogger<HistoryContextProvider>());

            var settings = new FakeContextSettingsProvider();
            settings.Set("history", new ContextProviderSettings(true, 60, 60, null));
            var pipelineLogger = new CapturingLogger<ContextPipeline>();
            var pipeline = new ContextPipeline([provider], settings, time, pipelineLogger);

            for (var i = 0; i < 18; i++) // Three simulated hours, one tick every ten minutes.
            {
                await pipeline.TickAsync(CancellationToken.None);
                time.Advance(TimeSpan.FromMinutes(10));
            }

            Assert.Empty(handler.Requests); // Zero fetch attempts the whole run — IsAvailable, not FetchAsync's own backstop, gated every tick.
            Assert.Single(pipelineLogger.Entries, entry => entry.Level == LogLevel.Information);
        }
    }
}
