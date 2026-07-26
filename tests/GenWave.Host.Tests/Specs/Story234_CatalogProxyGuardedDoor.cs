// STORY-234 — The proxy: one guarded door to the shelf (SPEC F90.1–F90.4, PLAN T99–T101)
//
// BDD specification — xUnit, pending until /build-loop turns each fact live. Entry-point
// discipline: every scenario drives the production surface (WebApplicationFactory<Program>
// against /api/catalog/*) with the upstream catalog faked at the HTTP boundary — never by
// calling proxy internals.
//
// T99 (SPEC F90.1) is the one exception: it ships no endpoint, so its two facts below —
// ScenarioValidatorEnforcesTheUrlRule and ScenarioAccessorIsFailClosed — are real, always-run
// unit coverage of SettingValidator's Community:CatalogIndexUrl rule and CommunityCatalogAccessor,
// the two seams T101 builds its endpoints on top of. Same "direct SettingValidator construction"
// idiom as Story124_EndpointLiveness.cs/Story149_SettingCeilings.cs.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using GenWave.Host.Catalog;
using GenWave.Host.Configuration;
using GenWave.Host.Options;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCatalogProxyGuardedDoor
{
    public sealed class ScenarioFetchVerifyCache
    {
        // Given Community:CatalogIndexUrl configured and a valid faked upstream (index +
        // entries with correct sha256), When /api/catalog/index is called twice within TTL.

        [Fact(Skip = "Pending (T100/T101)")]
        public void FirstCallFetchesAndHashVerifiesTheIndex() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void SecondCallWithinTtlServesFromCacheWithoutUpstreamHit() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void ResponseCarriesTheFetchedAtTimestamp() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void EntryFetchResolvesPathsRelativeToTheIndexUrl() { }
    }

    public sealed class ScenarioStaleBeatsAbsent
    {
        // Given a warm cache, When the upstream starts failing and TTL expires.

        [Fact(Skip = "Pending (T100/T101)")]
        public void CachedIndexIsServedAfterUpstreamFailure() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void StaleResponseKeepsItsOriginalFetchedAtTimestamp() { }
    }

    public sealed class ScenarioRejectingEmptyUrl
    {
        // Sad path — Given Community:CatalogIndexUrl = "" (fail-closed, F90.1). T99 shipped the
        // option, its validator (empty is legal — see ScenarioValidatorEnforcesTheUrlRule below),
        // and the CommunityCatalogAccessor fail-closed read side T101 wires into these two
        // endpoints; the endpoints themselves don't exist until T101.

        [Fact(Skip = "Pending (T101)")]
        public void IndexEndpointReturns404WhenUrlIsEmpty() { }

        [Fact(Skip = "Pending (T101)")]
        public void EntryEndpointReturns404WhenUrlIsEmpty() { }
    }

    public sealed class ScenarioRejectingHostileIndex
    {
        // Sad path — Given an upstream index containing an absolute entry URL or a
        // path-traversing relative path (F90.2).

        [Fact(Skip = "Pending (T100/T101)")]
        public void IndexWithAbsoluteEntryUrlIsRejectedWholesale() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void IndexWithPathTraversingEntryIsRejectedWholesale() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void RejectionWarnNamesTheOffendingPath() { }
    }

    public sealed class ScenarioRejectingTamperedContent
    {
        // Sad path — Given one entry whose fetched bytes mismatch the index sha256 (F90.3).

        [Fact(Skip = "Pending (T100/T101)")]
        public void MismatchedEntryIsWithheldWith502() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void RemainingEntriesStillServeWhileOneIsWithheld() { }

        [Fact(Skip = "Pending (T100/T101)")]
        public void OversizeCardIsWithheldBeforeCaching() { }
    }

    // ---------------------------------------------------------------------
    // T99 — real, always-run coverage (SPEC F90.1): the option + validator + fail-closed accessor
    // this whole story's endpoints (T100/T101) are built on top of.
    // ---------------------------------------------------------------------

    public sealed class ScenarioValidatorEnforcesTheUrlRule
    {
        // Mirrors Llm:Endpoint/Tts:Fallback:Endpoint's own "empty legal, else absolute http/https"
        // shape (Story124_EndpointLiveness.cs's sibling coverage for those two keys) — empty is the
        // F90.1 kill switch, not an error.

        static SettingValidator BuildValidator() => new(new ConfigurationBuilder().Build());

        [Fact]
        public void AnAbsoluteHttpsUrlIsAccepted()
        {
            var error = BuildValidator().Validate(
                "Community:CatalogIndexUrl",
                "https://raw.githubusercontent.com/GenWave-Org/genwave-catalog/main/index.json");

            Assert.Null(error);
        }

        [Fact]
        public void EmptyIsAcceptedAsTheFailClosedKillSwitch()
        {
            var error = BuildValidator().Validate("Community:CatalogIndexUrl", "");

            Assert.Null(error);
        }

        [Fact]
        public void ARelativePathIsRejected()
        {
            var error = BuildValidator().Validate("Community:CatalogIndexUrl", "index.json");

            Assert.NotNull(error);
        }

        [Fact]
        public void AnFtpSchemeIsRejected()
        {
            var error = BuildValidator().Validate(
                "Community:CatalogIndexUrl", "ftp://example.test/index.json");

            Assert.NotNull(error);
        }

        [Fact]
        public void GarbageIsRejected()
        {
            var error = BuildValidator().Validate("Community:CatalogIndexUrl", "not a url");

            Assert.NotNull(error);
        }

        [Fact]
        public void TheRejectionMessageNamesTheUrlRule()
        {
            var error = BuildValidator().Validate("Community:CatalogIndexUrl", "not a url");

            Assert.NotNull(error);
            Assert.Contains("absolute http/https URL", error, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioAccessorIsFailClosed
    {
        // CommunityCatalogAccessor is the fail-closed read side T101's endpoints consume — an
        // empty CatalogIndexUrl (the F90.1 kill switch) must resolve to IsEnabled=false and a null
        // IndexUrl, never an empty string a caller might mistake for "no constraint".

        static CommunityCatalogAccessor BuildAccessor(string catalogIndexUrl)
        {
            var monitor = new FakeOptionsMonitor<CommunityOptions>(
                new CommunityOptions { CatalogIndexUrl = catalogIndexUrl });
            return new CommunityCatalogAccessor(monitor);
        }

        const string ConfiguredUrl = "https://raw.githubusercontent.com/GenWave-Org/genwave-catalog/main/index.json";

        [Fact]
        public void AConfiguredUrlIsEnabled()
        {
            var accessor = BuildAccessor(ConfiguredUrl);

            Assert.True(accessor.IsEnabled);
        }

        [Fact]
        public void AConfiguredUrlIsExposed()
        {
            var accessor = BuildAccessor(ConfiguredUrl);

            Assert.Equal(ConfiguredUrl, accessor.IndexUrl);
        }

        [Fact]
        public void AnEmptyUrlIsDisabled()
        {
            var accessor = BuildAccessor("");

            Assert.False(accessor.IsEnabled);
        }

        [Fact]
        public void AnEmptyUrlExposesNoIndexUrl()
        {
            var accessor = BuildAccessor("");

            Assert.Null(accessor.IndexUrl);
        }

        [Fact]
        public void AWhitespaceOnlyUrlIsAlsoDisabled()
        {
            // Mirrors IsNonBlank's own discipline elsewhere in the allowlist (Station:Name/Voice) —
            // whitespace is not a real value, so it degrades to the same fail-closed state as "".
            var accessor = BuildAccessor("   ");

            Assert.False(accessor.IsEnabled);
        }
    }

    // ---------------------------------------------------------------------
    // T100 — real, always-run coverage (SPEC F90.2-F90.4): CatalogProxyService itself, driven
    // directly (constructor + public methods) against a fake IHttpClientFactory/HttpMessageHandler
    // and a fake TimeProvider — the same "test the seam directly, no endpoint exists yet" idiom the
    // T99 section above already uses. T101 is what wires GET /api/catalog/index and
    // GET /api/catalog/entries/{slug} on top of this service; the Pending facts further up stay
    // pending until then.
    // ---------------------------------------------------------------------

    /// <summary>Fixture index.json/card/meta bytes + a routed fake HTTP double, shared by every T100 scenario below.</summary>
    static class CatalogFixtures
    {
        public const string IndexUrl = "https://catalog.test/repo/index.json";
        const string Directory = "https://catalog.test/repo/";

        // Grounded in genwave-catalog/tools/testdata/green/valid-dj (schema-valid card+meta pair).
        public static string ValidDjCard => """
            {
              "schemaVersion": 1,
              "name": "Green Test DJ",
              "tagline": "",
              "soul": "",
              "quirks": [],
              "voice": { "engine": "kokoro", "voiceId": "af_heart", "pace": 1.0, "language": "en" },
              "energyDisposition": 0,
              "lore": [],
              "corrections": []
            }
            """;

        public static string ValidDjMeta => """
            {
              "author": "Test Fixture",
              "description": "Green-variant fixture for tools/run_selftest.sh.",
              "samplePatter": ["Line one.", "Line two."],
              "audience": "everyone",
              "added": "2026-07-26"
            }
            """;

        public static string SecondDjCard => """
            {
              "schemaVersion": 1,
              "name": "Second Test DJ",
              "tagline": "",
              "soul": "",
              "quirks": [],
              "voice": { "engine": "kokoro", "voiceId": "af_heart", "pace": 1.0, "language": "en" },
              "energyDisposition": 0,
              "lore": [],
              "corrections": []
            }
            """;

        public static string SecondDjMeta => """
            {
              "author": "Test Fixture",
              "description": "A sibling entry, used to prove one withheld entry doesn't sink the shelf.",
              "samplePatter": ["Sibling line one.", "Sibling line two."],
              "audience": "everyone",
              "added": "2026-07-26"
            }
            """;

        public static string Sha256Hex(string text) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        public static string CardPath(string slug) => $"entries/{slug}/{slug}.persona.json";
        public static string MetaPath(string slug) => $"entries/{slug}/{slug}.meta.json";
        public static string CardUrl(string slug) => Directory + CardPath(slug);
        public static string MetaUrl(string slug) => Directory + MetaPath(slug);

        public static string BuildIndexJson(params (string Slug, string CardJson, string MetaJson)[] entries)
        {
            var entryJson = string.Join(",", entries.Select(e => $$"""
                {
                  "slug": "{{e.Slug}}",
                  "audience": "everyone",
                  "card": { "path": "{{CardPath(e.Slug)}}", "sha256": "{{Sha256Hex(e.CardJson)}}" },
                  "meta": { "path": "{{MetaPath(e.Slug)}}", "sha256": "{{Sha256Hex(e.MetaJson)}}" }
                }
                """));

            return $$"""{ "generatedAt": "2026-07-26", "entries": [ {{entryJson}} ] }""";
        }

        /// <summary>A fake handler that serves <paramref name="routesByAbsoluteUrl"/> verbatim, 404 for anything else — every request is still recorded on <c>Requests</c>.</summary>
        public static FakeHttpMessageHandler RoutedHandler(IReadOnlyDictionary<string, string> routesByAbsoluteUrl) =>
            new((request, _) => Task.FromResult(
                routesByAbsoluteUrl.TryGetValue(request.RequestUri!.AbsoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound)));

        public static CatalogProxyService BuildService(HttpMessageHandler handler, TimeProvider timeProvider, string indexUrl = IndexUrl) =>
            new(
                new SingleHandlerHttpClientFactory(handler),
                new CommunityCatalogAccessor(new FakeOptionsMonitor<CommunityOptions>(new CommunityOptions { CatalogIndexUrl = indexUrl })),
                timeProvider,
                NullLogger<CatalogProxyService>.Instance);
    }

    /// <summary>
    /// Hands every named-client request to the same fake handler — mirrors
    /// <c>Story189_LlmSingleFlightAndWarnDetail</c>'s own <c>SingleHandlerHttpClientFactory</c>.
    /// <see cref="HttpClient.MaxResponseContentBufferSize"/> is set to MIRROR Program.cs's own
    /// <c>CatalogProxyService.HttpClientName</c> registration EXACTLY, for fidelity — it is INERT
    /// under <c>CatalogHttpFetcher</c>'s <see cref="HttpCompletionOption.ResponseHeadersRead"/> (see
    /// that type's own remarks: SendAsync never auto-buffers under that option, so this setting is
    /// never consulted either way) and so is NOT what makes the Oversize specs below trustworthy —
    /// setting it here only ensures a fake client can never accidentally differ from a real one.
    /// </summary>
    sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            MaxResponseContentBufferSize = CatalogProxyService.MaxIndexBytes,
        };
    }

    public sealed class ScenarioIndexFetchVerifyAndCache
    {
        // Given a valid faked upstream (index + one hash-correct entry), When the index is fetched.

        [Fact]
        public async Task FirstFetchReturnsTheValidatedEntry()
        {
            var index = CatalogFixtures.BuildIndexJson(("valid-dj", CatalogFixtures.ValidDjCard, CatalogFixtures.ValidDjMeta));
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string> { [CatalogFixtures.IndexUrl] = index });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            var result = await service.GetIndexAsync(CancellationToken.None);

            var ok = Assert.IsType<CatalogIndexFetchResult.Ok>(result);
            Assert.Equal("valid-dj", Assert.Single(ok.Entries).Slug);
        }

        [Fact]
        public async Task SecondCallWithinTtlServesFromCacheWithoutASecondUpstreamHit()
        {
            var index = CatalogFixtures.BuildIndexJson(("valid-dj", CatalogFixtures.ValidDjCard, CatalogFixtures.ValidDjMeta));
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string> { [CatalogFixtures.IndexUrl] = index });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            await service.GetIndexAsync(CancellationToken.None);
            await service.GetIndexAsync(CancellationToken.None);

            Assert.Single(handler.Requests);
        }
    }

    public sealed class ScenarioIndexTtlExpiry
    {
        [Fact]
        public async Task CallAfterTtlExpiryRefetchesFromUpstream()
        {
            var index = CatalogFixtures.BuildIndexJson(("valid-dj", CatalogFixtures.ValidDjCard, CatalogFixtures.ValidDjMeta));
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string> { [CatalogFixtures.IndexUrl] = index });
            var clock = new FakeTimeProvider();
            var service = CatalogFixtures.BuildService(handler, clock);

            await service.GetIndexAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromMinutes(16));
            await service.GetIndexAsync(CancellationToken.None);

            Assert.Equal(2, handler.Requests.Count);
        }
    }

    public sealed class ScenarioIndexSingleFlight
    {
        [Fact]
        public async Task ConcurrentCallersDuringAColdCacheShareOneUpstreamFetch()
        {
            var index = CatalogFixtures.BuildIndexJson(("valid-dj", CatalogFixtures.ValidDjCard, CatalogFixtures.ValidDjMeta));
            // A short artificial delay makes an un-gated pair's overlap observable (mirrors
            // Story189's own ConcurrencyTrackingHandler) — the FIRST caller to acquire the gate
            // holds it here; the second, if single-flight is working, never reaches the network at
            // all (it re-checks the now-warm cache once the first releases the gate).
            var handler = new FakeHttpMessageHandler(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index, Encoding.UTF8, "application/json") };
            });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            await Task.WhenAll(
                service.GetIndexAsync(CancellationToken.None),
                service.GetIndexAsync(CancellationToken.None));

            Assert.Single(handler.Requests);
        }
    }

    public sealed class ScenarioStaleOnFailure
    {
        static (FakeHttpMessageHandler Handler, Action FailNextCall) BuildFlakyIndexHandler(string indexJson)
        {
            var failing = false;
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(failing
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(indexJson, Encoding.UTF8, "application/json") }));
            return (handler, () => failing = true);
        }

        [Fact]
        public async Task UpstreamFailureAfterAWarmCacheServesTheStaleIndex()
        {
            var index = CatalogFixtures.BuildIndexJson(("valid-dj", CatalogFixtures.ValidDjCard, CatalogFixtures.ValidDjMeta));
            var (handler, failNextCall) = BuildFlakyIndexHandler(index);
            var clock = new FakeTimeProvider();
            var service = CatalogFixtures.BuildService(handler, clock);

            await service.GetIndexAsync(CancellationToken.None);
            failNextCall();
            clock.Advance(TimeSpan.FromMinutes(16));
            var result = await service.GetIndexAsync(CancellationToken.None);

            var ok = Assert.IsType<CatalogIndexFetchResult.Ok>(result);
            Assert.Equal("valid-dj", Assert.Single(ok.Entries).Slug);
        }

        [Fact]
        public async Task StaleServeKeepsTheOriginalFetchedAtTimestamp()
        {
            var index = CatalogFixtures.BuildIndexJson(("valid-dj", CatalogFixtures.ValidDjCard, CatalogFixtures.ValidDjMeta));
            var (handler, failNextCall) = BuildFlakyIndexHandler(index);
            var clock = new FakeTimeProvider();
            var service = CatalogFixtures.BuildService(handler, clock);

            var first = Assert.IsType<CatalogIndexFetchResult.Ok>(await service.GetIndexAsync(CancellationToken.None));
            failNextCall();
            clock.Advance(TimeSpan.FromMinutes(16));
            var second = Assert.IsType<CatalogIndexFetchResult.Ok>(await service.GetIndexAsync(CancellationToken.None));

            Assert.Equal(first.FetchedAt, second.FetchedAt);
        }
    }

    public sealed class ScenarioColdCacheFailure
    {
        [Fact]
        public async Task AColdCacheWithNoUpstreamIsUnreachable()
        {
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            var result = await service.GetIndexAsync(CancellationToken.None);

            Assert.IsType<CatalogIndexFetchResult.Unreachable>(result);
        }
    }

    public sealed class ScenarioHostileIndexRejected
    {
        // Sad path — an upstream index whose entry path escapes the F90.2 shape rule. Each of these
        // rejects the WHOLE index (never just the one entry) — asserted here as the "unreachable"
        // outcome T101 turns into the graceful empty state, same as any other origin failure.

        [Fact]
        public async Task AnAbsoluteEntryPathRejectsTheWholeIndex()
        {
            var index = """
                { "generatedAt": "2026-07-26", "entries": [
                  { "slug": "evil-dj", "audience": "everyone",
                    "card": { "path": "https://evil.test/x.persona.json", "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                    "meta": { "path": "entries/evil-dj/evil-dj.meta.json", "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } } ] }
                """;
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string> { [CatalogFixtures.IndexUrl] = index });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            var result = await service.GetIndexAsync(CancellationToken.None);

            Assert.IsType<CatalogIndexFetchResult.Unreachable>(result);
        }

        [Fact]
        public async Task ATraversalEntryPathRejectsTheWholeIndex()
        {
            var index = """
                { "generatedAt": "2026-07-26", "entries": [
                  { "slug": "evil-dj", "audience": "everyone",
                    "card": { "path": "../secret/evil-dj.persona.json", "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                    "meta": { "path": "entries/evil-dj/evil-dj.meta.json", "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } } ] }
                """;
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string> { [CatalogFixtures.IndexUrl] = index });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            var result = await service.GetIndexAsync(CancellationToken.None);

            Assert.IsType<CatalogIndexFetchResult.Unreachable>(result);
        }

        [Fact]
        public async Task APathWhoseSlugSegmentDoesNotMatchTheDeclaredSlugRejectsTheWholeIndex()
        {
            // Review finding #8: still regex-valid (entries/<slug>/<name>.persona.json) and still
            // resolving under the SAME index directory (so belt-and-braces never catches it) — but
            // "someone-else" is not this entry's own declared slug "evil-dj".
            var index = """
                { "generatedAt": "2026-07-26", "entries": [
                  { "slug": "evil-dj", "audience": "everyone",
                    "card": { "path": "entries/someone-else/someone-else.persona.json", "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                    "meta": { "path": "entries/evil-dj/evil-dj.meta.json", "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } } ] }
                """;
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string> { [CatalogFixtures.IndexUrl] = index });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            var result = await service.GetIndexAsync(CancellationToken.None);

            Assert.IsType<CatalogIndexFetchResult.Unreachable>(result);
        }

        [Fact]
        public async Task RedirectResponseIsTreatedAsAFetchFailure()
        {
            // AllowAutoRedirect=false is a Program.cs registration concern (this test constructs the
            // HttpClient directly, like every other fake-handler spec in this codebase) — what's
            // under test here is that CatalogProxyService treats a 3xx response it DOES receive as a
            // fetch failure rather than trying to interpret it, which is what actually matters once
            // redirects are disabled upstream (a redirect is never followed, so THIS is the only
            // response CatalogProxyService could ever see for one).
            var handler = new FakeHttpMessageHandler((_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://elsewhere.test/index.json");
                return Task.FromResult(response);
            });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            var result = await service.GetIndexAsync(CancellationToken.None);

            Assert.IsType<CatalogIndexFetchResult.Unreachable>(result);
        }
    }

    public sealed class ScenarioBeltAndBraces
    {
        // Layer 2 of F90.2's belt-and-braces rule, pinned directly (mirrors T99's own "test the
        // seam directly" idiom above): the entry-path shape regex already makes an escaping path
        // unreachable through the public GetIndexAsync/GetEntryAsync surface (no "..", no scheme,
        // no leading "/" can ever match it) — this exercises the SECOND, independent check alone.

        [Fact]
        public void AnEscapingRelativePathResolvesOutsideTheDirectoryPrefix()
        {
            var directory = new Uri("https://catalog.test/repo/");

            var staysUnderDirectory = CatalogIndexValidator.TryResolveWithinDirectory(directory, "../../evil/x.persona.json", out _);

            Assert.False(staysUnderDirectory);
        }
    }

    public sealed class ScenarioTamperedEntryWithheld
    {
        // Given an index advertising a sha256 for "valid-dj"'s card that does NOT match the bytes
        // the fake origin actually serves (upstream drift/tampering) — and a hash-correct sibling
        // "second-dj" entry alongside it.

        static string BuildIndexWithOneTamperedEntry()
        {
            var wrongCardHash = CatalogFixtures.Sha256Hex("not the real card bytes");
            return $$"""
                { "generatedAt": "2026-07-26", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{wrongCardHash}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{CatalogFixtures.Sha256Hex(CatalogFixtures.ValidDjMeta)}}" } },
                  { "slug": "second-dj", "audience": "everyone",
                    "card": { "path": "entries/second-dj/second-dj.persona.json", "sha256": "{{CatalogFixtures.Sha256Hex(CatalogFixtures.SecondDjCard)}}" },
                    "meta": { "path": "entries/second-dj/second-dj.meta.json", "sha256": "{{CatalogFixtures.Sha256Hex(CatalogFixtures.SecondDjMeta)}}" } } ] }
                """;
        }

        static FakeHttpMessageHandler BuildHandler() => CatalogFixtures.RoutedHandler(new Dictionary<string, string>
        {
            [CatalogFixtures.IndexUrl] = BuildIndexWithOneTamperedEntry(),
            [CatalogFixtures.CardUrl("valid-dj")] = CatalogFixtures.ValidDjCard,
            [CatalogFixtures.MetaUrl("valid-dj")] = CatalogFixtures.ValidDjMeta,
            [CatalogFixtures.CardUrl("second-dj")] = CatalogFixtures.SecondDjCard,
            [CatalogFixtures.MetaUrl("second-dj")] = CatalogFixtures.SecondDjMeta,
        });

        [Fact]
        public async Task AHashMismatchedCardIsWithheld()
        {
            var service = CatalogFixtures.BuildService(BuildHandler(), new FakeTimeProvider());

            var result = await service.GetEntryAsync("valid-dj", CancellationToken.None);

            Assert.IsType<CatalogEntryFetchResult.HashMismatch>(result);
        }

        [Fact]
        public async Task ASiblingEntryStillServesWhileOneIsWithheld()
        {
            var service = CatalogFixtures.BuildService(BuildHandler(), new FakeTimeProvider());

            await service.GetEntryAsync("valid-dj", CancellationToken.None);
            var result = await service.GetEntryAsync("second-dj", CancellationToken.None);

            Assert.IsType<CatalogEntryFetchResult.Ok>(result);
        }
    }

    public sealed class ScenarioProductionShapedClientProvesTheStreamingCapIsReal
    {
        // BLOCKING review finding #1: SendAsync's default completion option (ResponseContentRead)
        // made HttpClient buffer the ENTIRE body before CatalogHttpFetcher's own bounded read ever
        // saw a byte — fixed by HttpCompletionOption.ResponseHeadersRead (see CatalogHttpFetcher's
        // own remarks). MaxResponseContentBufferSize plays NO part in that fix — it is INERT under
        // ResponseHeadersRead (SendAsync never auto-buffers, so it's never consulted), kept only as
        // a regression backstop if the completion option is ever reverted. Driving
        // CatalogProxyService's public surface can't prove the fix on its own: an Oversize AND a
        // NetworkFailure collapse to the SAME observable "Unreachable" at that level. This drives
        // CatalogHttpFetcher directly instead — the one seam that can actually distinguish them —
        // against a client shaped exactly like Program.cs registers it, for fidelity.

        [Fact]
        public async Task ABodyLargerThanMaxResponseContentBufferSizeStillYieldsOversizeNotNetworkFailure()
        {
            var oversizeBody = new string(' ', CatalogProxyService.MaxIndexBytes + 1);
            var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(oversizeBody, Encoding.UTF8, "application/json") }));
            var factory = new SingleHandlerHttpClientFactory(handler);

            var outcome = await CatalogHttpFetcher.FetchAsync(
                factory, new Uri(CatalogFixtures.IndexUrl), CatalogProxyService.MaxIndexBytes, CancellationToken.None);

            Assert.IsType<CatalogFetchOutcome.Oversize>(outcome);
        }
    }

    public sealed class ScenarioIndexUrlWithQueryString
    {
        // BLOCKING review finding #2: a prior ResolveDirectory string-sliced Uri.AbsoluteUri at the
        // last '/', which mistook a '/' inside a legal query string for the path separator —
        // SettingValidator only requires "absolute http/https", not "has no query" — bricking the
        // whole catalog. RFC 3986 base-URI resolution (new Uri(indexUri, ".")) strips the query
        // correctly; this proves entries still resolve when the configured index URL carries one.

        [Fact]
        public async Task EntriesStillResolveWhenTheIndexUrlHasAQueryContainingASlash()
        {
            const string indexUrlWithQuery = CatalogFixtures.IndexUrl + "?v=1/2";
            var index = CatalogFixtures.BuildIndexJson(("valid-dj", CatalogFixtures.ValidDjCard, CatalogFixtures.ValidDjMeta));
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string>
            {
                [indexUrlWithQuery] = index,
                [CatalogFixtures.CardUrl("valid-dj")] = CatalogFixtures.ValidDjCard,
                [CatalogFixtures.MetaUrl("valid-dj")] = CatalogFixtures.ValidDjMeta,
            });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider(), indexUrlWithQuery);

            var result = await service.GetEntryAsync("valid-dj", CancellationToken.None);

            Assert.IsType<CatalogEntryFetchResult.Ok>(result);
        }
    }

    public sealed class ScenarioMalformedIndexUrlNeverThrows
    {
        // BLOCKING review finding #3: Community:CatalogIndexUrl is validated by SettingValidator on
        // every write through the settings API, but an env/compose-only override bypasses that
        // validator entirely (ValidateDataAnnotations on CommunityOptions asserts nothing about
        // this field) — a garbage or non-http(s) value must degrade to Unreachable, never propagate
        // a UriFormatException straight out of GetIndexAsync (which a bare `new Uri(url)` did).

        [Fact]
        public async Task AGarbageIndexUrlYieldsUnreachableRatherThanThrowing()
        {
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string>());
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider(), "not a url");

            var result = await service.GetIndexAsync(CancellationToken.None);

            Assert.IsType<CatalogIndexFetchResult.Unreachable>(result);
        }

        [Fact]
        public async Task AFileSchemeIndexUrlYieldsUnreachableRatherThanThrowing()
        {
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string>());
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider(), "file:///etc/passwd");

            var result = await service.GetIndexAsync(CancellationToken.None);

            Assert.IsType<CatalogIndexFetchResult.Unreachable>(result);
        }
    }

    public sealed class ScenarioEntryLevelStaleSurvivesAnUnrelatedIndexRefresh
    {
        // Review finding #5: a blanket cachedEntries.Clear() on every index refresh defeated
        // entry-level stale-on-failure (F90.4) whenever the index origin recovered a moment before
        // an entry's own origin did — even though the index still advertised the SAME sha256 for
        // that entry, so nothing about its cached content actually changed underneath it.

        [Fact]
        public async Task EntryStaysStaleServedAfterAnIndexRefreshWhoseShaForItIsUnchanged()
        {
            var index = CatalogFixtures.BuildIndexJson(("valid-dj", CatalogFixtures.ValidDjCard, CatalogFixtures.ValidDjMeta));
            var routes = new Dictionary<string, string>
            {
                [CatalogFixtures.IndexUrl] = index,
                [CatalogFixtures.CardUrl("valid-dj")] = CatalogFixtures.ValidDjCard,
                [CatalogFixtures.MetaUrl("valid-dj")] = CatalogFixtures.ValidDjMeta,
            };
            var failCardFetch = false;
            var handler = new FakeHttpMessageHandler((request, _) =>
            {
                var url = request.RequestUri!.AbsoluteUri;
                if (url == CatalogFixtures.CardUrl("valid-dj") && failCardFetch)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

                return Task.FromResult(routes.TryGetValue(url, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
            });
            var clock = new FakeTimeProvider();
            var service = CatalogFixtures.BuildService(handler, clock);

            var first = Assert.IsType<CatalogEntryFetchResult.Ok>(await service.GetEntryAsync("valid-dj", CancellationToken.None));
            clock.Advance(TimeSpan.FromMinutes(16)); // both index and entry TTL now expired
            failCardFetch = true;
            var second = Assert.IsType<CatalogEntryFetchResult.Ok>(await service.GetEntryAsync("valid-dj", CancellationToken.None));

            Assert.Equal(first.FetchedAt, second.FetchedAt);
        }
    }

    public sealed class ScenarioEntryCacheCap
    {
        // Review finding #6: an unbounded per-slug cache is an unbounded growth vector.

        [Fact]
        public async Task CachedEntryCountNeverExceedsTheConfiguredMaximum()
        {
            var slugs = Enumerable.Range(0, CatalogProxyService.MaxCachedEntries + 10)
                .Select(i => $"dj-{i:D4}")
                .ToArray();
            var routes = new Dictionary<string, string>
            {
                [CatalogFixtures.IndexUrl] = CatalogFixtures.BuildIndexJson(
                    slugs.Select(slug => (slug, CatalogFixtures.ValidDjCard, CatalogFixtures.ValidDjMeta)).ToArray()),
            };
            foreach (var slug in slugs)
            {
                routes[CatalogFixtures.CardUrl(slug)] = CatalogFixtures.ValidDjCard;
                routes[CatalogFixtures.MetaUrl(slug)] = CatalogFixtures.ValidDjMeta;
            }
            var handler = CatalogFixtures.RoutedHandler(routes);
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            foreach (var slug in slugs)
                await service.GetEntryAsync(slug, CancellationToken.None);

            Assert.Equal(CatalogProxyService.MaxCachedEntries, service.CachedEntryCountForTests);
        }
    }

    public sealed class ScenarioOversizeRejected
    {
        [Fact]
        public async Task AnOversizeIndexIsRejectedWholesale()
        {
            var oversizeBody = new string(' ', CatalogProxyService.MaxIndexBytes + 1);
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string> { [CatalogFixtures.IndexUrl] = oversizeBody });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            var result = await service.GetIndexAsync(CancellationToken.None);

            Assert.IsType<CatalogIndexFetchResult.Unreachable>(result);
        }

        [Fact]
        public async Task AnOversizeCardIsWithheld()
        {
            var oversizeCard = new string('a', CatalogProxyService.MaxCardBytes + 1);
            var index = $$"""
                { "generatedAt": "2026-07-26", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{CatalogFixtures.Sha256Hex(oversizeCard)}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{CatalogFixtures.Sha256Hex(CatalogFixtures.ValidDjMeta)}}" } } ] }
                """;
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string>
            {
                [CatalogFixtures.IndexUrl] = index,
                [CatalogFixtures.CardUrl("valid-dj")] = oversizeCard,
                [CatalogFixtures.MetaUrl("valid-dj")] = CatalogFixtures.ValidDjMeta,
            });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            var result = await service.GetEntryAsync("valid-dj", CancellationToken.None);

            Assert.IsType<CatalogEntryFetchResult.Oversize>(result);
        }
    }

    public sealed class ScenarioMalformedIndexRejected
    {
        [Fact]
        public async Task MalformedJsonRejectsTheWholeIndex()
        {
            var handler = CatalogFixtures.RoutedHandler(new Dictionary<string, string> { [CatalogFixtures.IndexUrl] = "{ not json " });
            var service = CatalogFixtures.BuildService(handler, new FakeTimeProvider());

            var result = await service.GetIndexAsync(CancellationToken.None);

            Assert.IsType<CatalogIndexFetchResult.Unreachable>(result);
        }
    }
}
