// STORY-284 — The library is inspectable (SPEC F104.7 · PLAN T203)
//
// BDD specification — xUnit. GET /api/fonts lists every installed pack — family, faces
// (file/style/byteSize), licence/sourceUrl/version/subset (parsed from the stored `definition`
// manifest via the hardened CatalogFontManifestSerializer.Deserialize — the SAME parser Install
// already trusted once at write time), and imported_from/imported_at provenance (db/25 pattern) —
// metadata only, straight off IFontPackStore.GetAllAsync (no face bytes on this wire).
//
// WIRED T203 — every Fact below drives the real production route through
// WebApplicationFactory<Program> (FontLibraryWebFactory below), mirroring Story282_FontPackInstall.cs's
// own FontPackInstallWebFactory idiom (a fake catalog origin + FakeFontPackStore, no live Postgres —
// this project carries none). ScenarioTheLibraryListsInstalledPacks drives a REAL install through the
// production install route first (mirrors Story283_InstalledFontServing.cs's own
// ScenarioTheClosedSetWidens precedent) — proving the `definition` this GET reads back and re-parses
// is the SAME jsonb the install route actually wrote, not a re-derived fixture.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad path
// (anonymous, empty library) is its own block. The Story278 route-set pin extension (the T200 review's
// own N7 obligation — this route joins its siblings under the SAME AdminSurface+Settings assertion
// every discovered api/catalog + api/themes + api/fonts endpoint gets, not a route-name check alone)
// lives in Story278_ThemeCatalogIsolation.cs itself, not repeated here.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Tests.Fakes;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureFontPackLibrary
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheLibraryListsInstalledPacks
    {
        [Fact]
        public async Task AnInstalledPackListsWithFamilyFacesAndLicence()
        {
            // Given a pack installed through the real production install route (so the `definition`
            // this GET re-parses is the SAME jsonb the install route actually wrote),
            var store = new FakeFontPackStore();
            await using var factory = new FontLibraryWebFactory(store);
            var client = await FontLibraryWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/fonts/{FontLibraryFixtures.Slug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When the library is listed,
            var response = await client.GetAsync("/api/fonts");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Then it lists exactly the installed pack, with family/faces/licence/sourceUrl/version/
            // subset all read back correctly (AC1's "family, faces, byte sizes, licence" half — the
            // provenance half is its own Fact below, one assertion per Fact).
            var pack = Assert.Single(document.RootElement.EnumerateArray());
            var face = Assert.Single(pack.GetProperty("faces").EnumerateArray());
            Assert.Equal(
                (Status: HttpStatusCode.OK,
                 Slug: FontLibraryFixtures.Slug, Family: FontLibraryFixtures.Family,
                 File: FontLibraryFixtures.AssetFile, Style: "normal", ByteSize: FontLibraryFixtures.AssetBytes.Length,
                 License: "OFL-1.1", SourceUrl: "https://example.test/library", Version: "1.0", Subset: "text"),
                (Status: response.StatusCode,
                 Slug: pack.GetProperty("slug").GetString(), Family: pack.GetProperty("family").GetString(),
                 File: face.GetProperty("file").GetString(), Style: face.GetProperty("style").GetString(),
                 ByteSize: face.GetProperty("byteSize").GetInt32(),
                 License: pack.GetProperty("license").GetString(), SourceUrl: pack.GetProperty("sourceUrl").GetString(),
                 Version: pack.GetProperty("version").GetString(), Subset: pack.GetProperty("subset").GetString()));
        }

        [Fact]
        public async Task AnInstalledPackCarriesTheDb25ProvenanceStamp()
        {
            // Given the same installed pack,
            var store = new FakeFontPackStore();
            await using var factory = new FontLibraryWebFactory(store);
            var client = await FontLibraryWebFactory.LoggedInClientAsync(factory);
            var before = DateTime.UtcNow;
            var install = await client.PostAsync($"/api/fonts/{FontLibraryFixtures.Slug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When the library is listed,
            var response = await client.GetAsync("/api/fonts");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var pack = Assert.Single(document.RootElement.EnumerateArray());

            // Then importedFrom is the catalog slug and importedAt is recent (the "Installed ·
            // <slug> · <date>" db/25 pattern's own two wire fields, SPEC F104.7 AC1).
            var importedAt = pack.GetProperty("importedAt").GetDateTime();
            Assert.Equal(
                (ImportedFrom: FontLibraryFixtures.Slug, ImportedAtIsRecent: true),
                (ImportedFrom: pack.GetProperty("importedFrom").GetString(),
                 ImportedAtIsRecent: importedAt >= before && importedAt <= DateTime.UtcNow));
        }
    }

    // ── T203 REVIEW-OBLIGATION RIDER (finding F1): the catalog kill switch does NOT gate the library ──

    public sealed class ScenarioTheCatalogKillSwitchDoesNotGateTheLibrary
    {
        [Fact]
        public async Task ListStill200sWhileInstallStill404sWithTheCatalogDisabled()
        {
            // Given a pack already installed — seeded directly into the store (bypasses the install
            //       route entirely, which this Fact's own second half proves is unreachable once the
            //       catalog is disabled) — and the catalog kill switch flipped (an empty
            //       Community:CatalogIndexUrl, SPEC F90.1),
            var face = new FontPackFace("kill-switch-pack-variable-latin.woff2", "normal", 4096, new string('a', 64));
            var pack = new FontPack(
                "kill-switch-pack", "Kill Switch Pack", "{}", "kill-switch-pack", DateTime.UtcNow, DateTime.UtcNow, [face]);
            var store = new FakeFontPackStore(pack);
            await using var factory = new FontLibraryWebFactory(store, catalogIndexUrl: "");
            var client = await FontLibraryWebFactory.LoggedInClientAsync(factory);

            // When the library is listed and, separately, an install is attempted,
            var listResponse = await client.GetAsync("/api/fonts");

            // Status asserted BEFORE parsing (T203 review note: a regression here must fail naming
            // the kill-switch rule, not as a JsonReaderException on a bare-404 body).
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
            var installResponse = await client.PostAsync($"/api/fonts/{pack.Slug}/install", null);

            // Then the library still 200-lists the already-installed pack while install still 404s
            // bare — the DIVERGENCE is the assertion (SPEC F104.8's amended posture, T203 review): the
            // kill switch gates the CATALOG surface (discovery of new packs), never the station's own
            // inventory (remembrance of installed ones).
            Assert.Equal(
                (ListStatus: HttpStatusCode.OK, ListedSlug: pack.Slug, InstallStatus: HttpStatusCode.NotFound),
                (ListStatus: listResponse.StatusCode,
                 ListedSlug: Assert.Single(listDocument.RootElement.EnumerateArray()).GetProperty("slug").GetString(),
                 InstallStatus: installResponse.StatusCode));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAnEmptyLibrary
    {
        [Fact]
        public async Task NoInstalledPacksListsAnEmptyArrayNotAnError()
        {
            // Given no packs installed,
            var store = new FakeFontPackStore();
            await using var factory = new FontLibraryWebFactory(store);
            var client = await FontLibraryWebFactory.LoggedInClientAsync(factory);

            // When the library is listed,
            var response = await client.GetAsync("/api/fonts");

            // Then it responds 200 with an empty array — the honest "nothing installed yet" shape,
            // never an error.
            Assert.Equal(
                (HttpStatusCode.OK, "[]"),
                (response.StatusCode, await response.Content.ReadAsStringAsync()));
        }
    }

    // ── T203 REVIEW-OBLIGATION RIDER (finding N8): a garbage stored `definition` degrades, never 500s ──

    public sealed class ScenarioAGarbageStoredDefinitionDegradesGracefully
    {
        [Theory]
        [InlineData("not json at all {")]        // malformed JSON — Deserialize's catch(JsonException)
        [InlineData("""{"family":""}""")]         // valid JSON, missing required fields — Deserialize's own field-by-field reject
        public async Task ListingStillReturns200WithNullLicenceFields(string hostileDefinition)
        {
            // Given a pack whose stored `definition` is garbage — seeded directly into the store
            //       (mirrors FakeFontPackStore.WithInstalledFace's own "write straight to the fake
            //       store" precedent), proving the READ side degrades gracefully regardless of how a
            //       broken document got there (the store itself never validates `definition`; only the
            //       install route's own writer does),
            var face = new FontPackFace("garbage-pack-variable-latin.woff2", "normal", 12345, new string('b', 64));
            var pack = new FontPack(
                "garbage-pack", "Garbage Pack", hostileDefinition, "garbage-pack", DateTime.UtcNow, DateTime.UtcNow, [face]);
            var store = new FakeFontPackStore(pack);
            await using var factory = new FontLibraryWebFactory(store);
            var client = await FontLibraryWebFactory.LoggedInClientAsync(factory);

            // When the library is listed,
            var response = await client.GetAsync("/api/fonts");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var listedPack = Assert.Single(document.RootElement.EnumerateArray());

            // Then it responds 200 — never a 500 — with license/sourceUrl/version/subset all null (the
            // DTO's own "degrade to null, never throw" contract), while slug/family are unaffected
            // (neither round-trips through the failed parse).
            Assert.Equal(
                (Status: HttpStatusCode.OK, Slug: pack.Slug, Family: pack.Family,
                 License: (string?)null, SourceUrl: (string?)null, Version: (string?)null, Subset: (string?)null),
                (Status: response.StatusCode,
                 Slug: listedPack.GetProperty("slug").GetString(), Family: listedPack.GetProperty("family").GetString(),
                 License: listedPack.GetProperty("license").GetString(), SourceUrl: listedPack.GetProperty("sourceUrl").GetString(),
                 Version: listedPack.GetProperty("version").GetString(), Subset: listedPack.GetProperty("subset").GetString()));
        }
    }

    public sealed class ScenarioAnonymousAccess
    {
        [Fact]
        public async Task AnAnonymousRequestIsUnauthorized()
        {
            // Given no session cookie (the T200 review's own N7 obligation — this route carries the
            // SAME AdminSurface+Settings pairing every other api/fonts route does),
            await using var factory = new FontLibraryWebFactory(new FakeFontPackStore());
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // When the library is listed anonymously,
            var response = await client.GetAsync("/api/fonts");

            // Then it is refused 401 — the same deny-by-default posture every other /api/* route
            // carries.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// <c>Story282_FontPackInstall.cs</c>'s own <c>FontPackInstallWebFactory</c> exactly (a fake catalog
/// origin behind <see cref="IHttpClientFactory"/>, <see cref="IFontPackStore"/> replaced by a
/// <see cref="FakeFontPackStore"/>, no live Postgres).
/// </summary>
file sealed class FontLibraryWebFactory(FakeFontPackStore store, string catalogIndexUrl = FontLibraryFixtures.IndexUrl)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story284-fontlibrary";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", catalogIndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(
                new SingleHandlerHttpClientFactory(FontLibraryFixtures.BuildRoutedHandler()));

            services.RemoveAll<IFontPackStore>();
            services.AddSingleton<IFontPackStore>(store);
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

/// <summary>
/// Fixture documents + a routed fake HTTP double for this file's own Facts — a single valid
/// <c>kind:"font"</c> entry, dedicated to library-listing specs (not <c>Story282_FontPackInstall.cs</c>'s
/// own golden Space Grotesk fixture, which pins hash-verification mechanics this file has no need to
/// re-derive), carrying a real license/sourceUrl/version/subset so the licence line this file pins has
/// something genuine to read back.
/// </summary>
file static class FontLibraryFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/library-index.json";
    const string Directory = "https://catalog.test/repo/";

    public const string Slug = "library-test-pack";
    public const string Family = "Library Test";
    public const string AssetFile = "library-test-variable-latin.woff2";

    public static readonly byte[] AssetBytes = "installed face bytes for the library listing specs (T203)"u8.ToArray();

    static string ManifestJson => $$"""
        {"family":"{{Family}}","files":[{"role":"upright","file":"{{AssetFile}}","weight":"400","style":"normal","bytes":{{AssetBytes.Length}}}],"license":"OFL-1.1","sourceUrl":"https://example.test/library","version":"1.0","subset":"text"}
        """;

    const string MetaJson = """
        {"author":"Test Fixture","description":"A pack for the library listing specs.","audience":"everyone","added":"2026-08-05"}
        """;

    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-05", "entries": [
          { "slug": "{{Slug}}", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/{{Slug}}/{{Slug}}.font.json", "sha256": "{{Sha256Hex(ManifestJson)}}" },
            "meta": { "path": "entries/{{Slug}}/{{Slug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson)}}" },
            "assets": [
              { "path": "entries/{{Slug}}/{{AssetFile}}", "sha256": "{{Sha256Hex(AssetBytes)}}", "bytes": {{AssetBytes.Length}} }
            ] } ] }
        """;

    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + Slug + "/" + Slug + ".font.json"] = ManifestJson,
            [Directory + "entries/" + Slug + "/" + Slug + ".meta.json"] = MetaJson,
        };
        var assetUrl = Directory + "entries/" + Slug + "/" + AssetFile;

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (absoluteUri == assetUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(AssetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}
