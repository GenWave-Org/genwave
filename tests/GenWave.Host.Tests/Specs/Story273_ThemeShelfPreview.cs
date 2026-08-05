// STORY-273 — The shelf lists themes beside personas (SPEC F103.3, F103.4, PLAN T185)
//
// BDD specification — xUnit. The community catalog shelf's index route admits a second entry kind:
// a theme entry carries an OPTIONAL `preview` swatch payload (T185's contract, catalog-owned
// theme-meta.schema.json) that CatalogIndexValidator projects onto CatalogEntrySummary and
// CatalogController.ToShelfEntryDto projects onto the wire CatalogShelfEntryDto — never fetching or
// parsing a theme's actual manifest to build the shelf listing (F103.4's "browsing costs nothing
// beyond the one index read").
//
// Entry-point discipline: the wire-projection scenario drives the real GET /api/catalog/index route
// (WebApplicationFactory<Program>, mirrors Story234's own CatalogApiWebFactory) against
// Fixtures/mixed-catalog-index.json served by a fake HTTP origin — proving CatalogController is
// actually wired to the new field, not just that CatalogIndexValidator parses it in isolation. The
// tolerance scenarios (missing/malformed preview) drive CatalogIndexValidator.TryValidate directly,
// the same "test the seam directly" idiom Story269_CatalogKindSeam.cs already uses for `kind`.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad path
// (missing/malformed preview tolerance) is its own block.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Host.Api;
using GenWave.Host.Catalog;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

/// <summary>Locates and reads a committed <c>Fixtures/</c> file from its SOURCE location (not a
/// build output copy) — mirrors Story269_CatalogKindSeam.cs's own <c>GoldenThemeFixtureFile</c>
/// idiom (itself <c>file</c>-scoped, so this file needs its own copy).</summary>
file static class MixedIndexFixtureFile
{
    public static string ReadText() => File.ReadAllText(LocatePath());

    static string LocatePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("repo root (GenWave.sln) not found");

        return Path.Combine(dir.FullName, "tests", "GenWave.Host.Tests", "Fixtures", "mixed-catalog-index.json");
    }
}

public static class FeatureThemeShelfPreview
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheShelfListsBothKindsWithPreview
    {
        // Given a catalog index with a persona entry (no kind key, bestFor) and a theme entry
        // (kind:"theme", a preview swatch payload), served by a fake origin,
        // When GET /api/catalog/index is called through the real production route,

        [Fact]
        public async Task BothEntriesAreListedRoutedByKind()
        {
            await using var factory = new ThemeShelfWebFactory(MixedIndexFixtureFile.ReadText());
            var client = await ThemeShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/catalog/index");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            // Then both kinds are listed (AC1) — asserted by the exact {slug, kind} pairs, order-independent.
            Assert.Equal(
                new[] { ("valid-dj", "persona"), ("golden-frequency", "theme") }.ToHashSet(),
                body!.Entries!.Select(e => (e.Slug, e.Kind)).ToHashSet());
        }

        [Fact]
        public async Task ThePersonaEntryCarriesNoPreview()
        {
            await using var factory = new ThemeShelfWebFactory(MixedIndexFixtureFile.ReadText());
            var client = await ThemeShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/catalog/index");

            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var persona = body!.Entries!.Single(e => e.Slug == "valid-dj");
            Assert.Null(persona.Preview);
        }

        [Fact]
        public async Task TheThemeEntrysLightSwatchesReachTheWire()
        {
            // Then the theme card's colour chips come straight off this ONE response (AC2) — no
            // second fetch is even possible from these assertions, since nothing here reads any
            // other route.
            await using var factory = new ThemeShelfWebFactory(MixedIndexFixtureFile.ReadText());
            var client = await ThemeShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/catalog/index");

            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var theme = body!.Entries!.Single(e => e.Slug == "golden-frequency");
            Assert.Equal(
                new CatalogShelfSwatchSetDto("#f7ecd2", "#fff8e6", "#2c2410", "#b8860b", "#4f6b52"),
                theme.Preview!.Light);
        }

        [Fact]
        public async Task TheThemeEntrysDarkSwatchesReachTheWireToo()
        {
            await using var factory = new ThemeShelfWebFactory(MixedIndexFixtureFile.ReadText());
            var client = await ThemeShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/catalog/index");

            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var theme = body!.Entries!.Single(e => e.Slug == "golden-frequency");
            Assert.Equal(
                new CatalogShelfSwatchSetDto("#171205", "#241c09", "#f4ecce", "#e0a52c", "#7fa382"),
                theme.Preview!.Dark);
        }

        [Fact]
        public async Task ListingTheShelfFetchesOnlyTheIndexNeverAManifestOrMeta()
        {
            // AC3 — browsing costs no manifest fetch, no meta fetch, nothing beyond the one index
            // read: the fake origin recorded EXACTLY one request, and it is index.json itself
            // (review finding, T185) — a looser "never a manifest/meta path" assertion would still
            // pass if the app fetched some OTHER unexpected path that simply didn't happen to
            // contain ".theme.json"/".meta.json".
            var handler = ThemeShelfWebFactory.BuildRoutedHandler(MixedIndexFixtureFile.ReadText());
            await using var factory = new ThemeShelfWebFactory(handler);
            var client = await ThemeShelfWebFactory.LoggedInClientAsync(factory);

            await client.GetAsync("/api/catalog/index");

            var requestedPath = Assert.Single(handler.Requests).RequestUri!.AbsolutePath;
            Assert.Equal("/repo/index.json", requestedPath);
        }
    }

    public sealed class ScenarioTheGoldenFixtureSuppliesTheSwatchValues
    {
        [Fact]
        public void TheValidatorProjectsThePreviewOntoTheSummary()
        {
            // Given the committed mixed index fixture, When it is parsed directly through the
            // validator seam (mirrors Story269's own idiom for `kind`),
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(MixedIndexFixtureFile.ReadText()),
                new Uri("https://catalog.test/repo/"),
                out var entries, out _);
            Assert.True(success);

            // Then the theme entry's summary carries the same light swatches the fixture declares.
            var theme = entries!.Single(e => e.Kind == CatalogEntryKind.Theme);
            Assert.Equal(new CatalogThemeSwatchSet("#f7ecd2", "#fff8e6", "#2c2410", "#b8860b", "#4f6b52"), theme.Preview!.Light);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAMissingPreviewIsNotAnError
    {
        const string Sha = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

        [Fact]
        public void AThemeEntryFromAnOlderIndexStillLoadsWithNoPreview()
        {
            // Given a theme entry from an index built before T185 (no `preview` key at all — the
            // real shape every genwave-catalog index carries until T191's seed lands),
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "{{Sha}}" } } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), new Uri("https://catalog.test/repo/"), out var entries, out _);

            // Then the entry still loads, with no preview — never a rejection.
            Assert.True(success);
            Assert.Null(Assert.Single(entries!).Preview);
        }
    }

    public sealed class ScenarioAMalformedPreviewDegradesToNoChipsRatherThanRejectingTheIndex
    {
        const string Sha = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

        [Fact]
        public void APreviewMissingItsDarkModeResolvesToNullNotARejection()
        {
            // Given a theme entry whose preview declares light but omits dark entirely (a build
            // regression upstream, not this station's problem to reject the whole shelf over),
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "{{Sha}}" },
                    "preview": { "light": { "bg": "#111111", "surface": "#222222", "ink": "#333333", "accent": "#444444", "accent-2": "#555555" } } } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), new Uri("https://catalog.test/repo/"), out var entries, out _);

            // Then the whole index still loads, this entry's preview is simply absent (the card
            // renders with no chips rather than the shelf losing every entry).
            Assert.True(success);
            Assert.Null(Assert.Single(entries!).Preview);
        }

        [Fact]
        public void APreviewSwatchSetMissingATokenResolvesToNullNotARejection()
        {
            // Given a light swatch set missing its own accent-2 key,
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "{{Sha}}" },
                    "preview": {
                      "light": { "bg": "#111111", "surface": "#222222", "ink": "#333333", "accent": "#444444" },
                      "dark": { "bg": "#111111", "surface": "#222222", "ink": "#333333", "accent": "#444444", "accent-2": "#555555" } } } ] }
                """;

            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), new Uri("https://catalog.test/repo/"), out var entries, out _);

            Assert.True(success);
            Assert.Null(Assert.Single(entries!).Preview);
        }

        [Fact]
        public void AHostileSwatchValueResolvesToNullRatherThanReachingTheWireUnvalidated()
        {
            // Given a preview whose `bg` swatch is a CSS-injection payload rather than a hex colour
            // (F1 review finding: index.json is remote, untrusted content — a swatch value reaches
            // the wire, and an inline `style` attribute in the Admin UI, unless it is held to the
            // same shape ThemeManifestParser.TokenValueText enforces on a real manifest's tokens),
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "{{Sha}}" },
                    "preview": {
                      "light": { "bg": "red;background-image:url(https://evil/x)", "surface": "#222222", "ink": "#333333", "accent": "#444444", "accent-2": "#555555" },
                      "dark": { "bg": "#111111", "surface": "#222222", "ink": "#333333", "accent": "#444444", "accent-2": "#555555" } } } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), new Uri("https://catalog.test/repo/"), out var entries, out _);

            // Then the shelf stays intact — the entry still loads, degraded to no preview — and the
            // hostile string never becomes a swatch value a caller could render.
            Assert.True(success);
            Assert.Null(Assert.Single(entries!).Preview);
        }
    }

    public sealed class ScenarioAWrongTypedPreviewDoesNotRejectTheWholeIndex
    {
        const string Sha = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

        // Given an index carrying a persona entry alongside a theme entry whose own `preview` is
        // some shape other than the expected `{light, dark}` object — F2 review finding: before this
        // fix, any of these shapes threw out of the top-level Deserialize call and rejected the
        // WHOLE index, vanishing the persona entry too.
        [Theory]
        [InlineData("42")]
        [InlineData("\"not-an-object\"")]
        [InlineData("[1, 2, 3]")]
        public void APreviewTypedAsSomethingOtherThanAnObjectDegradesOnlyThatEntrysPreview(string previewJson)
        {
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "{{Sha}}" },
                    "preview": {{previewJson}} } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), new Uri("https://catalog.test/repo/"), out var entries, out _);

            // Then the WHOLE index still loads — both entries — with only the theme entry's preview
            // degraded to null, never a rejection.
            Assert.True(success);
            Assert.Equal(2, entries!.Count);
            Assert.Null(entries!.Single(e => e.Slug == "gilded-static").Preview);
        }

        [Fact]
        public void ALeafValueTypeMismatchInsideAWellShapedPreviewDegradesOnlyThatEntrysPreview()
        {
            // Given a theme entry whose preview object IS shaped `{light, dark}`, but one swatch
            // leaf (`bg`) is typed as a number rather than a string,
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "{{Sha}}" },
                    "preview": {
                      "light": { "bg": 123, "surface": "#222222", "ink": "#333333", "accent": "#444444", "accent-2": "#555555" },
                      "dark": { "bg": "#111111", "surface": "#222222", "ink": "#333333", "accent": "#444444", "accent-2": "#555555" } } } ] }
                """;

            // When the index is parsed,
            var success = CatalogIndexValidator.TryValidate(
                Encoding.UTF8.GetBytes(index), new Uri("https://catalog.test/repo/"), out var entries, out _);

            // Then the WHOLE index still loads — both entries — with only the theme entry's preview
            // degraded to null, never a rejection.
            Assert.True(success);
            Assert.Equal(2, entries!.Count);
            Assert.Null(entries!.Single(e => e.Slug == "gilded-static").Preview);
        }
    }

    // ── Test harness ────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own HTTP-level
    /// scenario — boots the real Program.cs graph with <c>Community:CatalogIndexUrl</c> pointed at a
    /// fake origin serving <paramref name="indexJson"/> (or a caller-supplied
    /// <see cref="FakeHttpMessageHandler"/>, when the test needs to inspect every request the
    /// production code actually issued). Mirrors Story234's own <c>CatalogApiWebFactory</c>
    /// (private to that file, so this file needs its own copy) trimmed to only what this scenario
    /// needs — no TimeProvider/logger override, this story never exercises TTL or WARN content.
    /// </summary>
    sealed class ThemeShelfWebFactory : WebApplicationFactory<Program>
    {
        internal const string Password = "test-password-story273";
        const string IndexUrl = "https://catalog.test/repo/index.json";

        readonly FakeHttpMessageHandler handler;

        public ThemeShelfWebFactory(string indexJson) : this(BuildRoutedHandler(indexJson))
        {
        }

        public ThemeShelfWebFactory(FakeHttpMessageHandler handler)
        {
            this.handler = handler;
        }

        /// <summary>Serves <paramref name="indexJson"/> at <see cref="IndexUrl"/>, 404 for anything
        /// else (mirrors Story234's own <c>CatalogFixtures.RoutedHandler</c>) — every request is
        /// still recorded on <see cref="FakeHttpMessageHandler.Requests"/>.</summary>
        public static FakeHttpMessageHandler BuildRoutedHandler(string indexJson) => new((request, _) =>
            Task.FromResult(request.RequestUri!.AbsoluteUri == IndexUrl
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(indexJson, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound)));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
            builder.UseSetting("Admin:Password", Password);
            builder.UseSetting("Community:CatalogIndexUrl", IndexUrl);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(handler));
            });
        }

        public static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
        {
            var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            return client;
        }
    }

    /// <summary>Hands every named-client request to the same fake handler (mirrors Story234's own copy).</summary>
    sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
