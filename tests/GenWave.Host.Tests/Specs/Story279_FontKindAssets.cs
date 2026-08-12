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
// already use for kind/preview.
//
// T194: ScenarioAssetsStreamThroughTheGuardedDoor is now WIRED — AC2, the guarded-door binary
// fetch (CatalogProxyService.GetAssetAsync + CatalogController's GET
// /api/catalog/entries/{slug}/assets/{file}). Real WebApplicationFactory<Program> facts prove a
// woff2 asset fetches hash-verified end to end, a corrupted asset (served bytes != the index's own
// sha256) is withheld with the integrity posture (502), and a stream exceeding
// min(declared bytes, CatalogProxyService.MaxAssetBytes) is cut off and withheld the same way.
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

        [Fact]
        public void AFontEntryUnderThePerKindFolderLayoutIsAdmittedWithItsAssets()
        {
            // Given the same well-formed pack sitting under the nested entries/fonts/<slug>/ layout
            // (genwave-catalog#33) — manifest, meta, and both assets all in the one directory,
            var index = $$"""
                { "generatedAt": "2026-08-12", "entries": [
                  { "slug": "sample-pack", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/fonts/sample-pack/sample-pack.font.json", "sha256": "{{RefPlaceholder}}" },
                    "meta": { "path": "entries/fonts/sample-pack/sample-pack.meta.json", "sha256": "{{RefPlaceholder}}" },
                    "assets": [
                      { "path": "entries/fonts/sample-pack/sample-pack-variable-latin.woff2", "sha256": "{{WoffSha256}}", "bytes": 12345 },
                      { "path": "entries/fonts/sample-pack/OFL.txt", "sha256": "{{LicenceSha256}}", "bytes": 4523 }
                    ] } ] }
                """;

            // When the index is parsed,
            var success = TryValidate(index, out var entries);
            Assert.True(success);
            var entry = Assert.Single(entries!);

            // Then the entry is admitted with its nested asset references intact.
            Assert.Equal(
                new[]
                {
                    new CatalogAssetRef("entries/fonts/sample-pack/sample-pack-variable-latin.woff2", WoffSha256, 12345),
                    new CatalogAssetRef("entries/fonts/sample-pack/OFL.txt", LicenceSha256, 4523),
                },
                entry.Assets);
        }
    }

    // T194 (STORY-279 AC2): the asset transport — CatalogProxyService.GetAssetAsync + CatalogController's
    // new GET /api/catalog/entries/{slug}/assets/{file} route. Entry-point discipline: every fact
    // here drives the real route through WebApplicationFactory<Program>, mirrors
    // ScenarioBothRealRoutesServeAValidFontEntry above.
    public sealed class ScenarioAssetsStreamThroughTheGuardedDoor
    {
        [Fact]
        public async Task AWoff2AssetFetchesThroughTheProxyWithSizeCapAndSha256Applied()
        {
            // Given a catalog index naming a real, valid woff2 asset (its real sha256 and byte
            // count) served by a fake origin,
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/entries/{slug}/assets/{file} is called through the real
            // production route,
            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}/assets/{FontShelfFixtures.AssetFile}");

            // Then it responds 200 with the exact hash-verified woff2 bytes, served as font/woff2.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("font/woff2", response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal(FontFixtureFiles.ReadWoff2Bytes(), bytes);
        }

        [Fact]
        public async Task TheAssetRouteResponseCarriesNoStore()
        {
            // F5 review finding (T194), RE-SCOPED (T194 follow-up review finding): this spec pins
            // that Cache-Control: no-store is present on the real asset route — nothing more. It does
            // NOT, and honestly cannot, discriminate AssetFileResult's explicit header stamp from
            // NoCacheApiMiddleware's own best-effort one: at the golden woff2 fixture's size (7,844
            // bytes), TestServer's in-memory pipeline never reports Response.HasStarted == true by
            // the time the middleware's post-`next()` check runs, so the middleware would set the
            // identical header even with AssetFileResult's explicit line deleted — this fact would
            // stay green either way, which is exactly why it must not be read as pinning the explicit
            // stamp. Forcing a real mid-body flush (and therefore HasStarted == true) reliably inside
            // TestServer's buffering, for an asset this small, is not practical here.
            //
            // The middleware-vs-explicit DISTINCTION AssetFileResult's own no-store remarks
            // (CatalogController.cs) document — why a large streamed asset cannot rely on the
            // best-effort middleware alone — is enforced by code review and that comment, not by an
            // executable spec in this file.
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}/assets/{FontShelfFixtures.AssetFile}");

            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        }

        [Fact]
        public async Task AHashMismatchedAssetIsWithheldWithTheIntegrityPosture()
        {
            // Given the SAME index — still declaring the golden woff2's REAL sha256 — but an origin
            // whose actual response body at that asset's URL is different bytes entirely (a
            // corrupted/tampered upstream),
            var corruptedBytes = Encoding.UTF8.GetBytes("not the real font bytes");
            await using var factory = new FontShelfWebFactory(corruptedBytes);
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/entries/{slug}/assets/{file} is called through the real
            // production route,
            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}/assets/{FontShelfFixtures.AssetFile}");

            // Then it is withheld with the integrity posture (502 Bad Gateway) — never served.
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }

        [Fact]
        public async Task AnOversizedAssetWithADeclaredContentLengthIsFastRejectedAndWithheld()
        {
            // Given the SAME index — still declaring the golden woff2's REAL byte count (7,844,
            // well under MaxAssetBytes, so the effective cap here is the DECLARED size) — but an
            // origin whose actual response body at that asset's URL is one byte LARGER than that
            // declared size, served via ByteArrayContent (which auto-computes a Content-Length
            // header the fake handler serves verbatim, F4 review finding): CatalogHttpFetcher.ReadBoundedAsync's
            // fast Content-Length reject fires here, before a single body byte is even read — the
            // SIBLING fact below (StreamContent, no declared Content-Length) is what pins the
            // DURING-stream running-total cut this fact's own name used to (wrongly) claim.
            var oversizedBytes = new byte[FontFixtureFiles.ReadWoff2Bytes().Length + 1];
            await using var factory = new FontShelfWebFactory(oversizedBytes);
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            // When GET /api/catalog/entries/{slug}/assets/{file} is called through the real
            // production route,
            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}/assets/{FontShelfFixtures.AssetFile}");

            // Then the asset is withheld with the same integrity posture (502 Bad Gateway) — never
            // served, never cached.
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }

        [Fact]
        public async Task AnOversizedAssetWithNoDeclaredContentLengthIsCutOffMidStreamAndWithheld()
        {
            // F4 review finding (T194): the SAME index/declared byte count (7,844) as the sibling
            // fact above, but the origin's response is a StreamContent with NO Content-Length header
            // at all — CatalogHttpFetcher.ReadBoundedAsync's fast declared-length reject can never
            // fire here, so a withheld result can ONLY come from its running-total check while
            // actually reading the body (ReadBoundedAsync's own `buffer.Length + read > maxBytes`
            // line) — this is what genuinely pins the DURING-stream cut.
            var oversizedBytes = new byte[FontFixtureFiles.ReadWoff2Bytes().Length + 1];
            await using var factory = new FontShelfWebFactory(oversizedBytes, streamAssetWithNoContentLength: true);
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}/assets/{FontShelfFixtures.AssetFile}");

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }

        [Fact]
        public async Task AnAssetDeclaringExactlyMaxAssetBytesIsCutOffByMaxAssetBytesWhenTheStreamExceedsIt()
        {
            // F4 review finding (T194): "MaxAssetBytes is never the binding constraint in any
            // fixture" — every OTHER fact in this file uses the golden woff2's own declared size
            // (7,844), far below CatalogProxyService.MaxAssetBytes (262,144), so the effective cap
            // (min(declared, MaxAssetBytes)) is always the SMALLER declared value, never the
            // constant itself. This fact's asset declares bytes AT MaxAssetBytes — the largest value
            // CatalogIndexValidator's own F2 review-fix cap now admits (declaring more skips the
            // whole entry before this transport is ever reached) — so min(declared, MaxAssetBytes)
            // resolves to MaxAssetBytes itself; an origin stream one byte past THAT is what proves
            // the constant is genuinely doing the capping, not merely tagging along with a smaller
            // declared value.
            const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            const string Directory = "https://catalog.test/repo/";
            const string Slug = "cap-pack";
            const string AssetFile = "cap-bound.woff2";
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "{{Slug}}", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/{{Slug}}/{{Slug}}.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/{{Slug}}/{{AssetFile}}", "sha256": "{{Sha}}", "bytes": {{CatalogProxyService.MaxAssetBytes}} }
                    ] } ] }
                """;
            var assetUrl = $"{Directory}entries/{Slug}/{AssetFile}";
            var oversizedStream = new byte[CatalogProxyService.MaxAssetBytes + 1];
            var handler = new FakeHttpMessageHandler((request, _) =>
            {
                var url = request.RequestUri!.AbsoluteUri;
                if (url == FontShelfFixtures.IndexUrl)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index, Encoding.UTF8, "application/json") });
                if (url == assetUrl)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(oversizedStream)) });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            });
            await using var factory = new FontShelfWebFactory(handler);
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/catalog/entries/{Slug}/assets/{AssetFile}");

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        }
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

        [Fact]
        public void AFontEntryWithADuplicateAssetPathSkipsOnlyItself()
        {
            // F1 review finding (T194): a pack declaring the SAME asset path twice — which of the
            // two would even be the real one? — alongside a valid persona entry. Before the fix, a
            // duplicate here survived CatalogIndexValidator (each element is checked independently,
            // never de-duped) and threw ArgumentException straight out of
            // CatalogProxyService.PruneChangedAssets's own ToDictionary the moment the index was
            // ever fetched — an unhandled 500 on every catalog route.
            const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "broken-pack", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/broken-pack/broken-pack.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/broken-pack/broken-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/broken-pack/broken-pack-variable-latin.woff2", "sha256": "{{Sha}}", "bytes": 100 },
                      { "path": "entries/broken-pack/broken-pack-variable-latin.woff2", "sha256": "{{Sha}}", "bytes": 100 }
                    ] } ] }
                """;

            // When the index is parsed,
            var success = TryValidate(index, out var entries);

            // Then the index still loads and only the persona entry survives — the pack declaring
            // the same path twice is simply absent, never a whole-index rejection.
            Assert.True(success);
            Assert.Equal("valid-dj", Assert.Single(entries!).Slug);
        }

        [Fact]
        public void AFontEntryWithAnOverCapDeclaredByteAssetSkipsOnlyItself()
        {
            // F2 review finding (T194): an asset declaring MORE bytes than the fetch transport will
            // EVER accept (CatalogProxyService.MaxAssetBytes, 262,144) is malformed by definition —
            // alongside a valid persona entry. Before the fix, this survived validation and let
            // CatalogController's zero-fetch FontByteTotal sum (Assets.Sum(a => a.Bytes), checked
            // arithmetic) overflow straight into an unhandled 500 on the shelf/detail routes.
            const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "broken-pack", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/broken-pack/broken-pack.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/broken-pack/broken-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/broken-pack/broken-pack-variable-latin.woff2", "sha256": "{{Sha}}", "bytes": {{CatalogProxyService.MaxAssetBytes + 1}} }
                    ] } ] }
                """;

            // When the index is parsed,
            var success = TryValidate(index, out var entries);

            // Then the index still loads (the shelf survives) and only the persona entry survives —
            // the over-cap pack is simply absent, never a whole-index rejection.
            Assert.True(success);
            Assert.Equal("valid-dj", Assert.Single(entries!).Slug);
        }

        [Fact]
        public void AFontEntryWhoseAssetStraysFromItsManifestDirectorySkipsOnlyItself()
        {
            // genwave-catalog#33: with BOTH shelf layouts admitted, the flat entries/broken-pack/
            // and the nested entries/fonts/broken-pack/ are DISTINCT directories sharing a slug —
            // an asset under one while the manifest sits under the other passes every per-path
            // check (shape, slug ownership, containment) yet breaks the one-directory invariant
            // (and with it the bare-filename uniqueness CatalogProxyService's filename-keyed asset
            // lookup leans on). Alongside a valid persona entry,
            const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var index = $$"""
                { "generatedAt": "2026-08-12", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "{{Sha}}" } },
                  { "slug": "broken-pack", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/fonts/broken-pack/broken-pack.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/fonts/broken-pack/broken-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/broken-pack/broken-pack-variable-latin.woff2", "sha256": "{{Sha}}", "bytes": 100 }
                    ] } ] }
                """;

            // When the index is parsed,
            var success = TryValidate(index, out var entries);

            // Then the index still loads and only the persona entry survives — the straying pack is
            // simply absent, the same posture every other malformed-asset shape gets.
            Assert.True(success);
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

    // ── F3 review finding (T194): zero specs pinned CatalogController's font-kind meta projection
    // (FontFamily/FontByteTotal/FontSpecimenFile) before this fix — and the fixture they'd have
    // exercised was itself broken: FontShelfFixtures.AssetFile named a DIFFERENT file than
    // golden.font.json's own upright face, so FontSpecimenFile silently resolved to null in every
    // fact that touched it (see that constant's own remarks for the fixture fix). F7 review finding
    // (STORY-281 AC1 reconciliation) is pinned here too: the shelf's own FontFamily, sourced from
    // the INDEX's optional `family` field, not the manifest the shelf route never fetches.

    public sealed class ScenarioFontMetaProjectsThroughTheRealRoutes
    {
        [Fact]
        public async Task TheDetailRouteProjectsTheManifestsFamily()
        {
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}");

            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            Assert.Equal("Space Grotesk", body!.FontFamily);
        }

        [Fact]
        public async Task TheDetailRouteProjectsTheSummedAssetByteTotal()
        {
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}");

            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            Assert.Equal(7844L, body!.FontByteTotal);
        }

        [Fact]
        public async Task TheDetailRouteResolvesTheSpecimenFileToTheDeclaredAsset()
        {
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}");

            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            Assert.Equal(FontShelfFixtures.AssetFile, body!.FontSpecimenFile);
        }

        [Fact]
        public async Task TheDetailRouteProjectsTheLicenceVersionAndSubsetTrio()
        {
            // PLAN T204 (Dean's post-v3.1.0 review): the pre-install review panel showed no licence
            // at all — FontLicense/FontVersion/FontSubset are parsed off the SAME hash-verified
            // manifest FontFamily already reads (golden.font.json: "OFL-1.1"/"2.000"/"text"), so this
            // trust fact reaches the panel at zero extra fetch cost.
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync($"/api/catalog/entries/{FontShelfFixtures.FontSlug}");

            var body = await response.Content.ReadFromJsonAsync<CatalogEntryResponse>();
            Assert.Equal("OFL-1.1", body!.FontLicense);
            Assert.Equal("2.000", body.FontVersion);
            Assert.Equal("text", body.FontSubset);
        }

        [Fact]
        public async Task TheShelfRouteProjectsTheSameByteTotalAsTheDetailRoute()
        {
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/catalog/index");

            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var fontEntry = body!.Entries!.Single(e => e.Slug == FontShelfFixtures.FontSlug);
            Assert.Equal(7844L, fontEntry.FontByteTotal);
        }

        [Fact]
        public async Task TheShelfRouteShowsNullFamilyWhenTheIndexEntryCarriesNone()
        {
            // The golden-fixture index (FontShelfFixtures.IndexJson) never declares `family` on its
            // font entry — the "genuinely optional, degrades to null" half of the F7 review-fix
            // contract.
            await using var factory = new FontShelfWebFactory();
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/catalog/index");

            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var fontEntry = body!.Entries!.Single(e => e.Slug == FontShelfFixtures.FontSlug);
            Assert.Null(fontEntry.FontFamily);
        }

        [Fact]
        public async Task TheShelfRouteShowsTheFamilyWhenTheIndexEntryCarriesOne()
        {
            // The other half of the F7 review-fix contract: an index entry that DOES declare
            // `family` shows it on the shelf wire, straight off the index — no manifest fetch.
            const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "family-pack", "kind": "font", "audience": "everyone", "family": "Cool Grotesk",
                    "manifest": { "path": "entries/family-pack/family-pack.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/family-pack/family-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/family-pack/family-pack.woff2", "sha256": "{{Sha}}", "bytes": 100 }
                    ] } ] }
                """;
            var handler = new FakeHttpMessageHandler((request, _) =>
                Task.FromResult(request.RequestUri!.AbsoluteUri == FontShelfFixtures.IndexUrl
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound)));
            await using var factory = new FontShelfWebFactory(handler);
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/catalog/index");

            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var fontEntry = body!.Entries!.Single(e => e.Slug == "family-pack");
            Assert.Equal("Cool Grotesk", fontEntry.FontFamily);
        }

        [Fact]
        public async Task TheShelfRouteDegradesACssInjectionPayloadFamilyToNull()
        {
            // BLOCKER (T194 review finding): CatalogIndexValidator.TryParseFamily admitted the
            // optional `family` string onto the real shelf wire with only a `Length > 0` check — the
            // reviewer's own proof, a CSS-injection payload shaped to break out of the
            // `font-family: "<value>"` position ThemeCssComposer-adjacent surfaces interpolate a
            // family into, flowed straight through to CatalogShelfEntryDto.FontFamily. Gated on the
            // same shape ThemeManifestParser.FontFamilyPattern enforces, it now degrades to null.
            const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "hostile-pack", "kind": "font", "audience": "everyone", "family": "X;}</style><script>alert(1)</script>",
                    "manifest": { "path": "entries/hostile-pack/hostile-pack.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/hostile-pack/hostile-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/hostile-pack/hostile-pack.woff2", "sha256": "{{Sha}}", "bytes": 100 }
                    ] } ] }
                """;
            var handler = new FakeHttpMessageHandler((request, _) =>
                Task.FromResult(request.RequestUri!.AbsoluteUri == FontShelfFixtures.IndexUrl
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound)));
            await using var factory = new FontShelfWebFactory(handler);
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/catalog/index");

            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var fontEntry = body!.Entries!.Single(e => e.Slug == "hostile-pack");
            Assert.Null(fontEntry.FontFamily);
        }

        [Fact]
        public async Task TheShelfRouteStillShowsALegitimateFamily()
        {
            // The other half of the blocker fix's contract (mirrors the CSS-payload fact above): a
            // well-shaped family name — one this format's own vocabulary actually ships
            // ("Space Grotesk", the golden font fixture's own family) — still flows to the shelf wire
            // unchanged; the new FamilyPattern gate rejects hostile shapes, not legitimate ones.
            const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var index = $$"""
                { "generatedAt": "2026-08-05", "entries": [
                  { "slug": "legit-pack", "kind": "font", "audience": "everyone", "family": "Space Grotesk",
                    "manifest": { "path": "entries/legit-pack/legit-pack.font.json", "sha256": "{{Sha}}" },
                    "meta": { "path": "entries/legit-pack/legit-pack.meta.json", "sha256": "{{Sha}}" },
                    "assets": [
                      { "path": "entries/legit-pack/legit-pack.woff2", "sha256": "{{Sha}}", "bytes": 100 }
                    ] } ] }
                """;
            var handler = new FakeHttpMessageHandler((request, _) =>
                Task.FromResult(request.RequestUri!.AbsoluteUri == FontShelfFixtures.IndexUrl
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound)));
            await using var factory = new FontShelfWebFactory(handler);
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            var response = await client.GetAsync("/api/catalog/index");

            var body = await response.Content.ReadFromJsonAsync<CatalogIndexResponse>();
            var fontEntry = body!.Entries!.Single(e => e.Slug == "legit-pack");
            Assert.Equal("Space Grotesk", fontEntry.FontFamily);
        }

        [Fact]
        public async Task ListingTheShelfFetchesOnlyTheIndexNeverAnAsset()
        {
            // AC3-style guarantee (mirrors Story273's own "recorded requests" idiom, F3 review
            // finding): a font shelf card's byte total/family are computed straight off the index's
            // own fields — browsing costs zero asset requests, nothing beyond the one index read.
            var handler = FontShelfFixtures.BuildRoutedHandler();
            await using var factory = new FontShelfWebFactory(handler);
            var client = await FontShelfWebFactory.LoggedInClientAsync(factory);

            await client.GetAsync("/api/catalog/index");

            var requestedPath = Assert.Single(handler.Requests).RequestUri!.AbsolutePath;
            Assert.Equal("/repo/index.json", requestedPath);
        }
    }

    // ── Test harness (S1 review finding) ─────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own S1 route-level
    /// scenario and <see cref="FeatureFontKindAssets.ScenarioAssetsStreamThroughTheGuardedDoor"/>'s
    /// asset-transport facts (T194) — boots the real Program.cs graph with
    /// <c>Community:CatalogIndexUrl</c> pointed at <see cref="FontShelfFixtures.IndexUrl"/>, served by
    /// <see cref="FontShelfFixtures.BuildRoutedHandler"/> (or a caller-supplied
    /// <see cref="FakeHttpMessageHandler"/>, when a fact needs to inspect every request the
    /// production code actually issued, or serve a bespoke index — F3/F4 review findings). Mirrors
    /// Story273_ThemeShelfPreview.cs's own <c>ThemeShelfWebFactory</c> dual-constructor shape
    /// (private to that file, so this file needs its own copy) trimmed to only what this scenario
    /// needs.
    /// </summary>
    sealed class FontShelfWebFactory : WebApplicationFactory<Program>
    {
        internal const string Password = "test-password-story279-fontshelf";

        readonly FakeHttpMessageHandler handler;

        public FontShelfWebFactory(byte[]? assetBytesOverride = null, bool streamAssetWithNoContentLength = false)
            : this(FontShelfFixtures.BuildRoutedHandler(assetBytesOverride, streamAssetWithNoContentLength))
        {
        }

        public FontShelfWebFactory(FakeHttpMessageHandler handler)
        {
            this.handler = handler;
        }

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

    // F3 review finding (T194): must equal golden.font.json's own upright `files[].file` name
    // ("space-grotesk-variable-latin.woff2") — CatalogController.ResolveSpecimenFile cross-references
    // the manifest's upright filename against this entry's own declared asset filenames by EQUALITY
    // (Path.GetFileName(a.Path) == uprightFile); a mismatch here silently resolves FontSpecimenFile
    // to null in every fact that depends on it, the exact bug this fixture shipped with pre-fix. The
    // asset's bare FILENAME need not equal the slug (CatalogIndexValidator.AssetFileNameText's own
    // "the pack's OWN file name" rule) — only the DIRECTORY segment does.
    public const string AssetFile = "space-grotesk-variable-latin.woff2";

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
              { "path": "entries/sample-pack/{{AssetFile}}", "sha256": "{{Sha256Hex(FontFixtureFiles.ReadWoff2Bytes())}}", "bytes": 7844 }
            ] } ] }
        """;

    /// <summary>
    /// Serves every fixture document at its OWN resolved URL, 404 for anything else — every request
    /// is still recorded on <see cref="FakeHttpMessageHandler.Requests"/> (mirrors Story273's own
    /// <c>ThemeShelfWebFactory.BuildRoutedHandler</c>). The one binary asset URL serves
    /// <paramref name="assetBytesOverride"/> when given — <see cref="ScenarioAssetsStreamThroughTheGuardedDoor"/>'s
    /// hash-mismatch/oversize facts use this to make the ORIGIN's real response diverge from
    /// <see cref="IndexJson"/>'s own declared sha256/bytes (which always stay the golden woff2's REAL
    /// values, T193) — otherwise the golden woff2's real bytes, so the happy-path fact and every
    /// OTHER scenario in this file get a hash-verified, correctly-sized asset with no extra setup.
    /// <paramref name="streamAssetWithNoContentLength"/> (F4 review finding, T194) serves the asset
    /// via <see cref="StreamContent"/> instead of <see cref="ByteArrayContent"/> — the latter
    /// auto-computes a <c>Content-Length</c> header, which lets <c>CatalogHttpFetcher.ReadBoundedAsync</c>'s
    /// FAST reject fire before a single body byte is read, never exercising the DURING-stream
    /// running-total cut its own doc comment claims to pin.
    /// </summary>
    public static FakeHttpMessageHandler BuildRoutedHandler(
        byte[]? assetBytesOverride = null, bool streamAssetWithNoContentLength = false)
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/valid-dj/valid-dj.persona.json"] = PersonaCardJson,
            [Directory + "entries/valid-dj/valid-dj.meta.json"] = PersonaMetaJson,
            [Directory + "entries/sample-pack/sample-pack.font.json"] = FontManifestJson,
            [Directory + "entries/sample-pack/sample-pack.meta.json"] = FontMetaJson,
        };
        var assetUrl = Directory + "entries/sample-pack/" + AssetFile;
        var assetBytes = assetBytesOverride ?? FontFixtureFiles.ReadWoff2Bytes();

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
            {
                HttpContent content = streamAssetWithNoContentLength
                    ? new StreamContent(new MemoryStream(assetBytes))
                    : new ByteArrayContent(assetBytes);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}
