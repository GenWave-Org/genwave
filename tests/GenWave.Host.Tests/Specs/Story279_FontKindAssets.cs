// STORY-279 — The catalog admits the font kind (SPEC F104.1 · PLAN T193/T194)
//
// BDD specification — xUnit. T193's slice: the entry model admits kind:"font" + assets[]
// (path/sha256/bytes per asset), CatalogIndexValidator learns the font manifest file pattern and
// the asset shape, and the two golden fixtures (golden.font.json + golden-font.woff2) become the
// cross-repo format contract — the T177 precedent (golden.theme.json/ThemeManifestSerializer)
// applied to a font pack and, for the first time, to binary content.
//
// AC1/AC3/AC4 drive CatalogIndexValidator.TryValidate directly — the same "test the seam directly,
// no endpoint exists yet" idiom Story269_CatalogKindSeam.cs and Story273_ThemeShelfPreview.cs
// already use for kind/preview. AC2 (the guarded-door binary fetch) stays skipped — that is T194's
// transport slice, not this one.
//
// S1 REVIEW FIX (T193): ScenarioBothRealRoutesServeAValidFontEntry below is WIRED — it drives the
// real GET /api/catalog/index and GET /api/catalog/entries/{slug} routes (WebApplicationFactory<Program>,
// mirrors Story273's own ThemeShelfWebFactory) against a fake origin serving a valid font entry,
// proving CatalogController.ToWireKind actually admits CatalogEntryKind.Font end to end — before
// this fix, a valid font entry 500'd BOTH routes (UnreachableException) even though
// CatalogIndexValidator had already learned to admit the kind.
//
// S2 REVIEW FIX (T193): ScenarioAWrongTypedAssetsDoesNotRejectTheWholeIndex mirrors
// Story273_ThemeShelfPreview.cs's own ScenarioAWrongTypedPreviewDoesNotRejectTheWholeIndex — the
// identical "raw JsonElement, defensive per-shape conversion" fix applied to assets[] instead of
// preview.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad
// path (malformed/empty assets skip only their own entry) is its own block.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Xunit;

namespace GenWave.Host.Tests.Specs;

// ── Fixture file access ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Locates and reads this story's committed <c>Fixtures/</c> files from their SOURCE location (not
/// a build output copy) — mirrors <c>Story269_CatalogKindSeam.cs</c>'s own <c>GoldenThemeFixtureFile</c>
/// idiom (itself <c>file</c>-scoped, so this file needs its own copy).
/// </summary>
file static class FontFixtureFiles
{
    public static string ReadManifestText() => File.ReadAllText(LocatePath("golden.font.json"));

    public static byte[] ReadWoff2Bytes() => File.ReadAllBytes(LocatePath("golden-font.woff2"));

    public static string ReadCatalogIndexText() => File.ReadAllText(LocatePath("font-catalog-index.json"));

    static string LocatePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("repo root (GenWave.sln) not found");

        return Path.Combine(dir.FullName, "tests", "GenWave.Host.Tests", "Fixtures", fileName);
    }
}

public sealed class FeatureFontKindAssets
{
    static readonly Uri Directory = new("https://catalog.test/repo/");

    static bool TryValidate(string indexJson, out IReadOnlyList<CatalogEntrySummary>? entries) =>
        CatalogIndexValidator.TryValidate(Encoding.UTF8.GetBytes(indexJson), Directory, out entries, out _);

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheEntryModelCarriesAssets
    {
        const string RefPlaceholder = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string WoffSha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string LicenceSha256 = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

        [Fact]
        public void AFontEntryWithManifestMetaAndAssetsIsAdmittedWithItsAssetReferencesIntact()
        {
            // Given a kind:"font" entry with a valid manifest/meta pair and two assets — an
            // upright woff2 face and the pack's OFL licence text (SPEC F104.1's "1-2 woff2 +
            // licence" shape),
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "sample-pack", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/sample-pack/sample-pack.font.json", "sha256": "{{RefPlaceholder}}" },
                    "meta": { "path": "entries/sample-pack/sample-pack.meta.json", "sha256": "{{RefPlaceholder}}" },
                    "assets": [
                      { "path": "entries/sample-pack/sample-pack-variable-latin.woff2", "sha256": "{{WoffSha256}}", "bytes": 12345 },
                      { "path": "entries/sample-pack/OFL.txt", "sha256": "{{LicenceSha256}}", "bytes": 4523 }
                    ] } ] }
                """;

            // When the index is parsed,
            var success = TryValidate(index, out var entries);
            Assert.True(success);
            var entry = Assert.Single(entries!);

            // Then the entry is admitted as kind:"font" with its asset references intact, in order.
            Assert.Equal(CatalogEntryKind.Font, entry.Kind);
            Assert.Equal(
                new[]
                {
                    new CatalogAssetRef("entries/sample-pack/sample-pack-variable-latin.woff2", WoffSha256, 12345),
                    new CatalogAssetRef("entries/sample-pack/OFL.txt", LicenceSha256, 4523),
                },
                entry.Assets);
        }
    }

    public sealed class ScenarioAssetsStreamThroughTheGuardedDoor
    {
        [Fact(Skip = "pending T194 (STORY-279 AC2)")]
        public void AWoff2AssetFetchesThroughTheProxyWithSizeCapAndSha256Applied() { }

        [Fact(Skip = "pending T194 (STORY-279 AC2)")]
        public void AHashMismatchedAssetIsWithheldWithTheIntegrityPosture() { }
    }

    public sealed class ScenarioGoldenParityFixtures
    {
        [Fact]
        public void GoldenFontJsonRoundTripsByteStable()
        {
            // Given the committed golden.font.json — the concrete .font.json format contract
            // (T193, mirrors golden.theme.json's own T177 precedent: authored here first, staged
            // for genwave-catalog to commit byte-for-byte identical in a later task),
            var original = FontFixtureFiles.ReadManifestText();

            // When it is parsed as a CatalogFontManifest and re-serialized,
            var manifest = CatalogFontManifestSerializer.Deserialize(original);
            Assert.NotNull(manifest);

            // Then it is byte-identical.
            Assert.Equal(original, CatalogFontManifestSerializer.Serialize(manifest));
        }

        // The golden woff2's real sha256 (PLAN T193) — hand-transcribed from the committed
        // fixture's own bytes (`sha256sum Fixtures/golden-font.woff2`), the same value
        // font-catalog-index.json's own asset entry carries, NOT re-derived from the file at test
        // time — a hand-edit that silently swaps the fixture's bytes goes red here rather than
        // tautologically re-hashing whatever the file happens to contain today.
        const string RecordedWoff2Sha256 = "4f8000489733987cfe711fb469bd932a3024290bea8bc44151f6807f588932ee";

        [Fact]
        public void TheGoldenWoff2FixtureHashesToItsRecordedSha256()
        {
            // Given the committed golden-font.woff2 bytes,
            var bytes = FontFixtureFiles.ReadWoff2Bytes();

            // When they are hashed,
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            // Then the hash matches the value recorded above.
            Assert.Equal(RecordedWoff2Sha256, hash);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioOlderAppsSkipFontEntries
    {
        [Fact]
        public void AnIndexCarryingAFontEntryStillServesEveryOtherEntry()
        {
            // Given the committed font-catalog-index.json fixture — a persona entry alongside a
            // kind:"font" entry carrying the golden woff2's real hash (PLAN T193),
            var index = FontFixtureFiles.ReadCatalogIndexText();

            // When the index is parsed,
            var success = TryValidate(index, out var entries);
            Assert.True(success);

            // Then both entries are served — font is no longer forward-compat-skipped now that
            // this app recognises it (unlike Story269's own pre-T193 "font" example).
            Assert.Equal(
                new[] { ("valid-dj", CatalogEntryKind.Persona), ("space-grotesk", CatalogEntryKind.Font) }.ToHashSet(),
                entries!.Select(e => (e.Slug, e.Kind)).ToHashSet());
        }

        [Fact]
        public void AFontEntryWithMalformedAssetsSkipsOnlyItself()
        {
            // Given a font entry with an EMPTY assets[] — F104.1's "a pack IS its files" rule —
            // alongside a valid persona entry,
            const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "broken-pack", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/broken-pack/broken-pack.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/broken-pack/broken-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [] } ] }
                """;

            // When the index is parsed,
            var success = TryValidate(index, out var entries);
            Assert.True(success);

            // Then only the persona entry survives — the whole index is not rejected, and the
            // broken pack is simply absent, the same posture an unrecognised kind gets.
            Assert.Equal("valid-dj", Assert.Single(entries!).Slug);
        }
    }

    public sealed class ScenarioAWrongTypedAssetsDoesNotRejectTheWholeIndex
    {
        const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        // Given an index carrying a persona entry alongside a font entry whose own `assets` is one
        // of four shapes proven (S2 review finding) to throw straight out of the top-level
        // Deserialize call in TryValidate and reject the WHOLE index — the exact T185 `preview`
        // trap (Story273_ThemeShelfPreview.cs's own ScenarioAWrongTypedPreviewDoesNotRejectTheWholeIndex),
        // reintroduced here and now fixed the identical way.
        [Theory]
        [InlineData("""{ "notAnArray": true }""")] // object, not array
        [InlineData("""[ "not-an-object" ]""")] // string element, not an object
        [InlineData("""[ { "path": "entries/broken-pack/broken-pack-variable-latin.woff2", "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bytes": "12345" } ]""")] // string bytes
        [InlineData("""[ { "path": "entries/broken-pack/broken-pack-variable-latin.woff2", "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bytes": 9999999999999999999999999999999999999999 } ]""")] // long-overflow bytes
        public void AWronglyShapedAssetsListDegradesOnlyTheFontEntry(string assetsJson)
        {
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "broken-pack", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/broken-pack/broken-pack.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/broken-pack/broken-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": {{assetsJson}} } ] }
                """;

            // When the index is parsed,
            var success = TryValidate(index, out var entries);

            // Then the WHOLE index still loads — the persona entry survives — and the font entry is
            // simply absent, never a rejection.
            Assert.True(success);
            Assert.Equal("valid-dj", Assert.Single(entries!).Slug);
        }
    }

    // ── WIRED (S1 review finding) ────────────────────────────────────────────
    //
    // Before this fix, a valid font entry 500'd BOTH GET /api/catalog/index and
    // GET /api/catalog/entries/{slug} — CatalogController.ToWireKind threw UnreachableException the
    // instant a CatalogEntrySummary/CatalogEntryContent carrying CatalogEntryKind.Font reached
    // either projection, even though CatalogIndexValidator had already learned to admit the kind
    // (T193). Entry-point discipline: drives the real routes through WebApplicationFactory<Program>
    // against a fake origin, mirrors Story273_ThemeShelfPreview.cs's own ThemeShelfWebFactory.

    public sealed class ScenarioBothRealRoutesServeAValidFontEntry
    {
        [Fact]
        public async Task TheIndexRouteListsBothEntriesWithTheFontEntryTypedFont()
        {
            // Given a catalog index with a persona entry and a valid kind:"font" entry (real
            // manifest/meta content, and a real, valid asset), served by a fake origin,
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/index is called through the real production route,
            var response = await client.GetAsync("/api/catalog/index");

            // Then it responds 200 (never the pre-fix 500), listing both entries, the font entry
            // typed "font".
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            Assert.Equal(
                new[] { (FontShelfFixtures.PersonaSlug, "persona"), (FontShelfFixtures.FontSlug, "font") }.ToHashSet(),
                body!.Entries!.Select(e => (e.Slug, e.Kind)).ToHashSet());
        }

        [Fact]
        public async Task TheEntryRouteServesTheFontEntryWithKindFont()
        {
            // Given the same fake origin, When GET /api/catalog/entries/{slug} is called for the
            // font entry through the real production route,
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}");

            // Then it responds 200 (never the pre-fix 500) with kind "font".
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            Assert.Equal("font", body!.Kind);
        }

        [Fact]
        public async Task TheEntryRouteStillServesThePersonaEntryIntactAlongsideTheFontEntry()
        {
            // S1's "persona entries intact" requirement: the SAME route, for the persona slug in
            // the SAME index, is unaffected by a font entry sharing the shelf.
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.PersonaSlug}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            Assert.Equal("persona", body!.Kind);
        }
    }

    // ── Test harness (S1 review finding) ─────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own S1 route-level
    /// scenario — boots the real Program.cs graph with <c>Community:CatalogIndexUrl</c> pointed at
    /// <see cref="FontShelfFixtures.IndexUrl"/>, served by <see cref="FontShelfFixtures.BuildRoutedHandler"/>.
    /// Mirrors Story273_ThemeShelfPreview.cs's own <c>ThemeShelfWebFactory</c> (private to that
    /// file, so this file needs its own copy) trimmed to only what this scenario needs.
    /// </summary>
    sealed class FontShelfWebFactory : WebApplicationFactory<Program>
    {
        internal const string Password = "test-password-story279-fontshelf";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
            builder.UseSetting("Admin:Password", Password);
            builder.UseSetting("Community:CatalogIndexUrl", FontShelfFixtures.IndexUrl);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(new SingleHandlerHttpClientFactory(FontShelfFixtures.BuildRoutedHandler()));
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
}

/// <summary>Fixture documents + a routed fake HTTP double for <see cref="FeatureFontKindAssets.ScenarioBothRealRoutesServeAValidFontEntry"/>
/// (S1 review finding) — a persona entry and a valid font entry (real manifest/meta content, one
/// real asset), every sha256 computed from the served content itself so both real routes fetch and
/// hash-verify successfully. <c>file</c>-scoped (this file's own established idiom, see
/// <see cref="FontFixtureFiles"/> above).</summary>
file static class FontShelfFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string PersonaSlug = "valid-dj";
    public const string FontSlug = "sample-pack";

    public static string PersonaCardJson => """
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

    public static string PersonaMetaJson => """
        {
          "author": "Test Fixture",
          "description": "A persona entry sharing the shelf with a font entry (S1 review finding).",
          "samplePatter": ["Line one."],
          "audience": "everyone",
          "added": "2026-08-05"
        }
        """;

    public static string FontManifestJson => FontFixtureFiles.ReadManifestText();

    public static string FontMetaJson => """
        {
          "author": "Test Fixture",
          "description": "A curated font pack sharing the shelf with a persona entry.",
          "audience": "everyone",
          "added": "2026-08-05"
        }
        """;

    static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string IndexJson() => $$"""
        { "generatedAt": "2026-08-05", "entries": [
          { "slug": "valid-dj", "audience": "everyone",
            "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha256Hex(PersonaCardJson)}}" },
            "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha256Hex(PersonaMetaJson)}}" } },
          { "slug": "sample-pack", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/sample-pack/sample-pack.font.json", "sha256": "{{Sha256Hex(FontManifestJson)}}" },
            "meta": { "path": "entries/sample-pack/sample-pack.meta.json", "sha256": "{{Sha256Hex(FontMetaJson)}}" },
            "assets": [
              { "path": "entries/sample-pack/sample-pack-variable-latin.woff2", "sha256": "{{Sha256Hex(FontFixtureFiles.ReadWoff2Bytes())}}", "bytes": 7844 }
            ] } ] }
        """;

    /// <summary>Serves every fixture document at its OWN resolved URL, 404 for anything else — every
    /// request is still recorded on <see cref="FakeHttpMessageHandler.Requests"/> (mirrors
    /// Story273's own <c>ThemeShelfWebFactory.BuildRoutedHandler</c>).</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/valid-dj/valid-dj.persona.json"] = PersonaCardJson,
            [Directory + "entries/valid-dj/valid-dj.meta.json"] = PersonaMetaJson,
            [Directory + "entries/sample-pack/sample-pack.font.json"] = FontManifestJson,
            [Directory + "entries/sample-pack/sample-pack.meta.json"] = FontMetaJson,
        };

        return new((request, _) => Task.FromResult(
            routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound)));
    }
}
