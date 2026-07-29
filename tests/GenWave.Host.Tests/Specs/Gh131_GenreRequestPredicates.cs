// gh-#131 — "Anything metal!!" means metal, not Rock (genre predicate + genre/mood pickers)
//
// BDD specification — xUnit. Owns the HOST half of gh-#131: (1) genre populated from free text by
// BOTH parsers — the LLM schema's new optional `genre` and the deterministic parser's
// "recognized = member of the current genre options list" rule; (2) the matcher's genre gate —
// "station has no metal ⇒ UNMATCHED, never a mood coercion", the AND-merge into FindBestAsync, and
// the stocked-genre vibe survival; (3) GET /spectator/api/request-options — SpectatorSurface,
// rate-limited, cached like the sibling public GETs, moods = MoodVocabulary.Terms verbatim; (4)
// POST picker validation — fail-closed 400 on non-members (value never echoed), valid values stored
// as picked predicates with NO LLM in the path; (5) the F69 degradation pin — a picker-only request
// parses to predicates identically in Hard mode, zero LLM calls, because no parser runs at all.
// The probe SQL is MediaLibrary.Tests' Gh131_GenreRequestCatalog.cs; the fulfillment rung is
// Orchestration.Tests' Gh131_GenreVibeFulfillment.cs — the STORY-226/227 three-file split.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Requests;
using GenWave.Host.Tests.Fakes;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>
/// Brings up the real HTTP pipeline with a scriptable <see cref="FakeRequestCatalogProbe"/> standing
/// in for the library-schema genre reads (the same "no Postgres round trip on this surface" posture
/// Story224's own factory takes with <see cref="FakeRequestStore"/>) — request-options and the POST
/// picker validation both read the probe's <see cref="FakeRequestCatalogProbe.RequestableGenres"/>.
/// </summary>
file sealed class GenreRequestWebFactory(bool spectatorMode = true) : WebApplicationFactory<Program>
{
    public FakeRequestStore RequestStore { get; } = new();
    public FakeRequestCatalogProbe CatalogProbe { get; } = new() { RequestableGenres = ["Metal", "Rock"] };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", spectatorMode ? "true" : "false");
        builder.UseSetting("Station:Requests:Enabled", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
            services.RemoveAll<IRequestStore>();
            services.AddSingleton<IRequestStore>(RequestStore);
            services.RemoveAll<IRequestCatalogProbe>();
            services.AddSingleton<IRequestCatalogProbe>(CatalogProbe);
        });
    }
}

/// <summary>Hands every client the SAME shared handler (never disposed by the client) — mirrors
/// Story225's own file-local twin, which a file-scoped class cannot share across files.</summary>
file sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

public static class FeatureGenrePredicateParsing
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static HttpResponseMessage ChatResponse(string content) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { choices = new[] { new { message = new { content } } } }),
    };

    // ---------------------------------------------------------------------
    // HAPPY PATH — both parsers populate genre from free text
    // ---------------------------------------------------------------------

    public static class ScenarioDeterministicGenreRecognition
    {
        [Fact]
        public static void ARecognizedGenreWordBecomesTheGenrePredicateInCanonicalCasing()
        {
            // "anything metal" against a station stocking Metal — recognized because (and only
            // because) "metal" is a member of the current genre options list, emitted in the
            // option's own casing, never the wish's.
            var parsed = DeterministicWishParser.Parse("anything metal please", ["Metal", "Rock"]);

            Assert.Equal("Metal", parsed.Genre);
            Assert.Null(parsed.Artist);
            Assert.Null(parsed.Title);
            Assert.Empty(parsed.Moods);
        }

        [Fact]
        public static void AnUnstockedGenreWordIsNeverCoercedIntoAMood()
        {
            // The gh-#131 origin bug, pinned at the parser: "anything metal" on a station with no
            // metal yields EMPTY predicates — no genre (not a member), and crucially no mood
            // stand-in for the genre word. Empty parse ⇒ status=unmatched (F87.4).
            var parsed = DeterministicWishParser.Parse("anything metal!!", ["Rock", "Jazz"]);

            Assert.True(parsed.IsEmpty);
        }

        [Fact]
        public static void AGenuineMoodWordStillParsesAlongsideAGenre()
        {
            // Recognition stays independent: an explicit MoodVocabulary word in the wish is a mood
            // predicate on its own merits — killing the genre→mood coercion never killed real moods.
            var parsed = DeterministicWishParser.Parse("something dreamy, anything rock", ["Rock"]);

            Assert.Equal("Rock", parsed.Genre);
            Assert.Equal(["dreamy"], parsed.Moods);
        }
    }

    public static class ScenarioLlmGenreSchema
    {
        static LlmWishParser BuildLlmParser(FakeHttpMessageHandler handler) =>
            new(
                new SingleHandlerHttpClientFactory(handler),
                new FakeOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "https://llm.example/v1", Model = "test-model" }),
                new DeterministicWishParser(),
                NullLogger<LlmWishParser>.Instance);

        [Fact]
        public static async Task TheWishParseSchemaCarriesGenreThrough()
        {
            // The LLM schema gained optional `genre` (gh-#131) — a model answer using it lands as
            // the genre predicate, trimmed, alongside the established artist/title/moods handling.
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
                ChatResponse("{\"artist\":null,\"title\":null,\"genre\":\" metal \",\"moods\":[]}")));
            var parser = BuildLlmParser(handler);

            var parsed = await parser.ParseAsync("anything metal!!", ["Metal"], CancellationToken.None);

            Assert.Equal("metal", parsed.Genre);
            Assert.Empty(parsed.Moods);
        }

        [Fact]
        public static async Task TheSystemPromptDemandsTheGenreFieldAndForbidsMoodCoercion()
        {
            // The constrained-output contract itself names genre and forbids approximating one as a
            // mood — the instruction half of "kill the mood-coercion fallback for genre words".
            string? capturedBody = null;
            var handler = new FakeHttpMessageHandler(async (req, ct) =>
            {
                capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
                return ChatResponse("{\"artist\":null,\"title\":null,\"genre\":null,\"moods\":[]}");
            });
            var parser = BuildLlmParser(handler);

            await parser.ParseAsync("anything metal", ["Metal"], CancellationToken.None);

            Assert.NotNull(capturedBody);
            var systemContent = JsonDocument.Parse(capturedBody).RootElement
                .GetProperty("messages")[0].GetProperty("content").GetString();
            Assert.Contains("\"genre\": string or null", systemContent, StringComparison.Ordinal);
            Assert.Contains("NEVER approximate a genre as a mood", systemContent, StringComparison.Ordinal);
        }
    }
}

public static class FeatureMatcherGenreGate
{
    // ---------------------------------------------------------------------
    // HAPPY PATH — a stocked genre survives as a vibe predicate
    // ---------------------------------------------------------------------

    public static class ScenarioStationHasTheGenre
    {
        [Fact]
        public static async Task AStockedGenreStaysPendingAsAVibeRequest()
        {
            var probe = new FakeRequestCatalogProbe { Result = null, RequestableGenres = ["Metal"] };
            var store = new FakeRequestStore();
            var matcher = new RequestMatcher(probe, store);

            await matcher.MatchAsync(1, null, null, "Metal", [], CancellationToken.None);

            // Neither write happens — the row stays exactly as MarkParsedAsync left it (pending,
            // genre already stored), ready for the T90 vibe pick with the genre predicate.
            Assert.Empty(store.MarkMatchedCalls);
            Assert.Empty(store.MarkUnmatchedCalls);
        }

        [Fact]
        public static async Task AGenreAndsIntoTheBestMatchProbe()
        {
            // artist+genre predicates reach FindBestAsync TOGETHER (predicates merge) — a hit
            // stamps the match exactly as before.
            var probe = new FakeRequestCatalogProbe { Result = 42L, RequestableGenres = ["Rock"] };
            var store = new FakeRequestStore();
            var matcher = new RequestMatcher(probe, store);

            await matcher.MatchAsync(1, "Led Zeppelin", null, "Rock", [], CancellationToken.None);

            var call = Assert.Single(probe.Calls);
            Assert.Equal(("Led Zeppelin", null, "Rock"), call);
            Assert.Equal((1L, 42L), Assert.Single(store.MarkMatchedCalls));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — station has no metal ⇒ UNMATCHED, no coercion (the gh-#131 pin)
    // ---------------------------------------------------------------------

    public static class ScenarioStationLacksTheGenre
    {
        [Fact]
        public static async Task AnUnstockedGenreFlipsToUnmatchedEvenWithMoodsPresent()
        {
            // Predicates merge as AND: a genre the station cannot satisfy poisons the whole set, so
            // the row goes unmatched rather than quietly degrading into a mood-only pick the
            // listener never asked for — the coercion path, killed.
            var probe = new FakeRequestCatalogProbe { Result = null, RequestableGenres = ["Rock"] };
            var store = new FakeRequestStore();
            var matcher = new RequestMatcher(probe, store);

            await matcher.MatchAsync(1, null, null, "Metal", ["gritty"], CancellationToken.None);

            Assert.Equal(1L, Assert.Single(store.MarkUnmatchedCalls));
            Assert.Empty(store.MarkMatchedCalls);
        }

        [Fact]
        public static async Task ABestMatchMissWithAnUnstockedGenreAlsoFlipsToUnmatched()
        {
            // The artist route misses, and the surviving genre cannot be satisfied either — same
            // outcome through the artist-first branch.
            var probe = new FakeRequestCatalogProbe { Result = null, RequestableGenres = [] };
            var store = new FakeRequestStore();
            var matcher = new RequestMatcher(probe, store);

            await matcher.MatchAsync(1, "Some Band", null, "Metal", [], CancellationToken.None);

            Assert.Equal(1L, Assert.Single(store.MarkUnmatchedCalls));
        }
    }
}

public static class FeatureRequestOptionsEndpoint
{
    const string Route = "/spectator/api/request-options";

    // ---------------------------------------------------------------------
    // HAPPY PATH — the two pick lists, and nothing else
    // ---------------------------------------------------------------------

    public static class ScenarioOptionsServed
    {
        [Fact]
        public static async Task GenresComeFromTheProbeAndMoodsAreTheVocabularyVerbatim()
        {
            await using var factory = new GenreRequestWebFactory();
            var client = factory.CreateClient();

            var body = JsonDocument.Parse(await client.GetStringAsync(Route)).RootElement;

            var genres = body.GetProperty("genres").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            var moods = body.GetProperty("moods").EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            Assert.Equal(["Metal", "Rock"], genres);
            Assert.Equal(MoodVocabulary.Terms, moods);
        }

        [Fact]
        public static async Task TheResponseIsPubliclyCacheableAtTheSiblingGetTier()
        {
            // The stats/play-history caching posture (30s public Cache-Control + OutputCache) —
            // Story171's own pin, applied to the new route.
            await using var factory = new GenreRequestWebFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync(Route);

            var cache = response.Headers.CacheControl;
            Assert.True(
                cache is { Public: true, MaxAge: not null } && (int)cache.MaxAge.Value.TotalSeconds == 30,
                $"Cache-Control was '{cache}' — expected public, max-age=30.");
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the served form carries the two pickers (Story229's markup idiom)
    // ---------------------------------------------------------------------

    public static class ScenarioFormPickersServed
    {
        [Fact]
        public static async Task TheServedPageCarriesBlankDefaultGenreAndMoodPickers()
        {
            // The pickers ship in the static markup with a blank-value default option each; app.js
            // fills the real lists from request-options at runtime (browser half, verified live).
            await using var factory = new GenreRequestWebFactory();
            var client = factory.CreateClient();

            var html = await client.GetStringAsync("/spectator");

            Assert.Contains("id=\"request-genre\"", html, StringComparison.Ordinal);
            Assert.Contains("id=\"request-mood\"", html, StringComparison.Ordinal);
            Assert.Contains("<option value=\"\">Any genre</option>", html, StringComparison.Ordinal);
            Assert.Contains("<option value=\"\">Any mood</option>", html, StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — SpectatorSurface + the spectator rate limiter hold
    // ---------------------------------------------------------------------

    public static class SadPathSurfaceAndThrottle
    {
        [Fact]
        public static async Task SpectatorModeOffMeansAStandard404()
        {
            await using var factory = new GenreRequestWebFactory(spectatorMode: false);
            var client = factory.CreateClient();

            var response = await client.GetAsync(Route);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public static async Task ExceedingTheSpectatorBudgetReturns429()
        {
            // 120 requests/minute per source IP (RateLimiterPolicies.Spectator, class-wide on
            // SpectatorController) — the 121st call trips it, cached hits included.
            await using var factory = new GenreRequestWebFactory();
            var client = factory.CreateClient();

            HttpStatusCode last = default;
            for (var i = 0; i < 121; i++)
                last = (await client.GetAsync(Route)).StatusCode;

            Assert.Equal(HttpStatusCode.TooManyRequests, last);
        }
    }
}

public static class FeaturePickerIntake
{
    const string Route = "/spectator/api/requests";

    // ---------------------------------------------------------------------
    // HAPPY PATH — valid picker values become picked predicates, no LLM anywhere
    // ---------------------------------------------------------------------

    public static class ScenarioPickerOnlySubmission
    {
        [Fact]
        public static async Task APickerOnlyRequestIsAcceptedAndStoresThePickedValues()
        {
            await using var factory = new GenreRequestWebFactory();
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(Route, new { genre = "Metal", mood = "dreamy" });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var inserted = Assert.Single(factory.RequestStore.Inserted);
            Assert.Null(inserted.Wish);
            Assert.Equal("Metal", inserted.PickedGenre);
            Assert.Equal("dreamy", inserted.PickedMood);
        }

        [Fact]
        public static async Task AGenrePickIsCanonicalizedToTheListsOwnCasing()
        {
            // Membership is case-insensitive (the probe's lower()=lower() semantics), but what is
            // STORED is the published list's own casing — never the caller's byte string.
            await using var factory = new GenreRequestWebFactory();
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(Route, new { genre = "mEtAl" });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var inserted = Assert.Single(factory.RequestStore.Inserted);
            Assert.Equal("Metal", inserted.PickedGenre);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — fail-closed membership validation, value never echoed
    // ---------------------------------------------------------------------

    public static class SadPathNonMemberValues
    {
        [Fact]
        public static async Task ANonMemberGenreGets400AndNothingIsWrittenOrEchoed()
        {
            await using var factory = new GenreRequestWebFactory();
            var client = factory.CreateClient();
            const string nonMember = "Polka-Fusion-Injected-Value";

            var response = await client.PostAsJsonAsync(Route, new { genre = nonMember });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(factory.RequestStore.Inserted);
            Assert.DoesNotContain(nonMember, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public static async Task ANonMemberMoodGets400AndNothingIsWrittenOrEchoed()
        {
            // "metal" is the origin story: a genre word must not sneak in through the mood field
            // either — MoodVocabulary membership is exact.
            await using var factory = new GenreRequestWebFactory();
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(Route, new { mood = "metal" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(factory.RequestStore.Inserted);
            Assert.DoesNotContain("metal", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public static async Task AnEntirelyEmptySubmissionGets400()
        {
            // "Submit requires at least one of text/genre/mood" — the server-side half.
            await using var factory = new GenreRequestWebFactory();
            var client = factory.CreateClient();

            var response = await client.PostAsJsonAsync(Route, new { wish = "   " });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(factory.RequestStore.Inserted);
        }
    }
}

public static class FeaturePickerDegradedParity
{
    // ---------------------------------------------------------------------
    // Helpers — mirrors Story225's BuildService, probe exposed for genre scripting
    // ---------------------------------------------------------------------

    static RequestParserService BuildService(
        FakeRequestStore store, FakeRequestCatalogProbe probe, DegradationMode mode, FakeHttpMessageHandler handler)
    {
        var deterministic = new DeterministicWishParser();
        var llmOptions = new FakeOptionsMonitor<LlmOptions>(new LlmOptions { Endpoint = "https://llm.example/v1", Model = "test-model" });
        var llmParser = new LlmWishParser(
            new SingleHandlerHttpClientFactory(handler), llmOptions, deterministic, NullLogger<LlmWishParser>.Instance);
        var degradation = new FakeDegradationModeReader { CurrentMode = mode };
        var channel = Channel.CreateBounded<long>(8);
        var matcher = new RequestMatcher(probe, store);

        return new RequestParserService(
            channel.Reader, store, llmParser, deterministic, degradation, llmOptions, matcher, probe,
            NullLogger<RequestParserService>.Instance);
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — picker predicates never touch the LLM, in ANY mode (F69)
    // ---------------------------------------------------------------------

    public static class ScenarioPickerOnlyInHardMode
    {
        [Theory]
        [InlineData(DegradationMode.Normal)]
        [InlineData(DegradationMode.Soft)]
        [InlineData(DegradationMode.Hard)]
        public static async Task APickerOnlyRowParsesToPredicatesWithZeroLlmCallsInEveryMode(DegradationMode mode)
        {
            // The degradation pin: a picker-only request has NO wish, so no parser — LLM or
            // deterministic — runs at all; the validated picked values become the predicates
            // byte-identically whether the station is Normal or fully degraded (F69).
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { choices = new[] { new { message = new { content = "{}" } } } }),
                }));
            var store = new FakeRequestStore();
            store.UnparsedById[1] = (null, "Metal", "dreamy", DateTime.UtcNow.AddMinutes(15));
            var probe = new FakeRequestCatalogProbe { RequestableGenres = ["Metal"] };
            var service = BuildService(store, probe, mode, handler);

            await service.ParseOneAsync(1, CancellationToken.None);

            Assert.Empty(handler.Requests); // zero LLM calls — the F69 pin
            var call = Assert.Single(store.MarkParsedCalls);
            Assert.Equal("Metal", call.Genre);
            Assert.Equal(["dreamy"], call.Moods);
            Assert.False(call.Unmatched);
            Assert.Empty(store.MarkUnmatchedCalls); // the stocked genre survives as a vibe — still pending
        }
    }

    // ---------------------------------------------------------------------
    // HAPPY PATH — the end-to-end "anything metal" pin through the real pipeline
    // ---------------------------------------------------------------------

    public static class ScenarioAnythingMetalEndToEnd
    {
        [Fact]
        public static async Task AnythingMetalGoesUnmatchedWhenTheStationHasNoMetal()
        {
            // Deterministic path (Soft), no metal stocked: the wish parses to EMPTY predicates (no
            // genre membership, no mood coercion) and the same MarkParsedAsync write flips the row
            // unmatched — never a Rock track, never a vibe.
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            var store = new FakeRequestStore();
            store.UnparsedById[1] = ("Anything metal!!", null, null, DateTime.UtcNow.AddMinutes(15));
            var probe = new FakeRequestCatalogProbe { RequestableGenres = ["Rock", "Jazz"] };
            var service = BuildService(store, probe, DegradationMode.Soft, handler);

            await service.ParseOneAsync(1, CancellationToken.None);

            var call = Assert.Single(store.MarkParsedCalls);
            Assert.Null(call.Genre);
            Assert.Empty(call.Moods);
            Assert.True(call.Unmatched);
        }

        [Fact]
        public static async Task AnythingMetalBecomesAGenreVibeWhenTheStationStocksMetal()
        {
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            var store = new FakeRequestStore();
            store.UnparsedById[1] = ("Anything metal!!", null, null, DateTime.UtcNow.AddMinutes(15));
            var probe = new FakeRequestCatalogProbe { RequestableGenres = ["Metal", "Rock"] };
            var service = BuildService(store, probe, DegradationMode.Soft, handler);

            await service.ParseOneAsync(1, CancellationToken.None);

            var call = Assert.Single(store.MarkParsedCalls);
            Assert.Equal("Metal", call.Genre);
            Assert.False(call.Unmatched);
            Assert.Empty(store.MarkUnmatchedCalls); // pending genre vibe, awaiting the T90 pick
        }
    }
}
