// STORY-285 — The widened font law (SPEC F104.9, F104.10 · PLAN T205)
// AC3 (catalog CI stays curated-only) is pinned in the genwave-catalog repo's own idiom.
//
// BDD specification — xUnit. ThemeFontProvenanceValidator widens from vendored-only to vendored ∪
// installed (InstalledFontCatalog); the per-theme byte ceiling sums recorded bytes across BOTH sets;
// the import route's 400 names the missing face and, when the catalog index knows a pack that
// provides it, the pack's own slug too (SPEC F104.10) — fail-soft when the catalog cannot be reached.
//
// WIRED T205 — every Fact below drives the real production POST /api/themes/{slug}/import route
// through WebApplicationFactory<Program> (mirrors Story272_ThemeImport.cs's own ThemeImportWebFactory
// idiom), against a fake catalog origin (mirrors Story282_FontPackInstall.cs's own
// FontPackInstallWebFactory idiom) and FakeFontPackStore/FakeThemeStore doubles — no live Postgres,
// this project has none. "Installed" is always reached through the REAL POST /api/fonts/{slug}/install
// route first (proving InstalledFontCatalog.ReloadAsync's post-write rebuild reaches THIS request
// pipeline too — the same proof Story283_InstalledFontServing.cs's own ScenarioTheClosedSetWidens
// makes for /fonts/{file}), never seeded directly into the fake store.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad path
// (AC2, plus the fail-soft rider) is its own block.

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
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Tests.Fakes;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureWidenedFontLaw
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheUnionAdmitsInstalledFaces
    {
        [Fact]
        public async Task AThemeReferencingAnInstalledPackFaceImports200()
        {
            // Given a pack installed through the real production install route (proving the T199/T200
            // rebuild hook actually reaches THIS request pipeline, not just InstalledFontCatalog in
            // isolation),
            var themeStore = new FakeThemeStore();
            await using var factory = new WidenedFontLawWebFactory(themeStore, new FakeFontPackStore());
            var client = await WidenedFontLawWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/fonts/{WidenedFontLawFixtures.SmallPackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When a theme referencing that installed face is imported (display stays a real vendored
            // face, sans is the newly-installed one — SPEC F104.9's "vendored ∪ installed" union),
            var response = await client.PostAsync(
                "/api/themes/installed-face-theme/import",
                JsonBody(WidenedFontLawFixtures.ManifestReferencing("installed-face-theme", WidenedFontLawFixtures.SmallPackAssetSrc)));

            // Then it imports clean — the widened law admits it where the pre-T205 vendored-only law
            // would have refused it.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ThePerThemeCeilingSumsRecordedBytesAcrossVendoredAndInstalled()
        {
            // Given a big face installed through the real production route — its bytes ALONE sit
            // comfortably under the 204,800-byte per-theme ceiling, and so does the real vendored
            // Fraunces face (67,304 bytes, FONTS.md's own recorded provenance) alone — but their SUM
            // does not,
            var themeStore = new FakeThemeStore();
            await using var factory = new WidenedFontLawWebFactory(themeStore, new FakeFontPackStore());
            var client = await WidenedFontLawWebFactory.LoggedInClientAsync(factory);
            var install = await client.PostAsync($"/api/fonts/{WidenedFontLawFixtures.BigPackSlug}/install", null);
            Assert.True(install.IsSuccessStatusCode, await install.Content.ReadAsStringAsync());

            // When a theme referencing BOTH the installed big face AND the vendored Fraunces face is
            // imported,
            var response = await client.PostAsync(
                "/api/themes/ceiling-theme/import",
                JsonBody(WidenedFontLawFixtures.ManifestReferencing("ceiling-theme", WidenedFontLawFixtures.BigPackAssetSrc)));
            var body = await response.Content.ReadAsStringAsync();

            // Then it is refused as over the ceiling — proving the two sets' bytes were summed
            // TOGETHER (a validator that summed only one set would have accepted this: neither face
            // alone crosses the ceiling) — and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("per-theme ceiling", body, StringComparison.Ordinal);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAMissingPackIsNamed
    {
        [Fact]
        public async Task AnUninstalledFaceRefuses400NamingTheFace()
        {
            // Given a pack the catalog index KNOWS about but that was never installed on this station,
            var themeStore = new FakeThemeStore();
            await using var factory = new WidenedFontLawWebFactory(themeStore, new FakeFontPackStore());
            var client = await WidenedFontLawWebFactory.LoggedInClientAsync(factory);

            // When a theme referencing its face is imported,
            var response = await client.PostAsync(
                "/api/themes/pack-absent-theme/import",
                JsonBody(WidenedFontLawFixtures.ManifestReferencing("pack-absent-theme", WidenedFontLawFixtures.SmallPackAssetSrc)));
            var body = await response.Content.ReadAsStringAsync();

            // Then it is refused 400, naming the missing face, and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(WidenedFontLawFixtures.SmallPackAssetSrc, body, StringComparison.Ordinal);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task TheRefusalNamesTheProvidingPackSlugWhenTheIndexKnowsOne()
        {
            // Given the same never-installed pack,
            var themeStore = new FakeThemeStore();
            await using var factory = new WidenedFontLawWebFactory(themeStore, new FakeFontPackStore());
            var client = await WidenedFontLawWebFactory.LoggedInClientAsync(factory);

            // When a theme referencing its face is imported,
            var response = await client.PostAsync(
                "/api/themes/pack-absent-theme/import",
                JsonBody(WidenedFontLawFixtures.ManifestReferencing("pack-absent-theme", WidenedFontLawFixtures.SmallPackAssetSrc)));
            var body = await response.Content.ReadAsStringAsync();

            // Then the refusal ALSO names the pack slug the catalog index says provides it (SPEC
            // F104.10) — actionable ("install pack X"), never silent. Asserts on the actual
            // enrichment sentence (ImportProblems.UnvendoredFontDetail), not merely a substring that
            // the base refusal's own asset-src mention could also satisfy (review finding, T205
            // gate-integrity hole — mirrors the negative assertion this Fact's sibling makes at line
            // ~161 for "is provided by pack").
            Assert.Contains(
                $"\\\"{WidenedFontLawFixtures.SmallPackAssetSrc}\\\" is provided by pack \\\"{WidenedFontLawFixtures.SmallPackSlug}\\\"",
                body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AnUnreachableCatalogStillNamesTheFaceWithoutASuggestion()
        {
            // Given the SAME never-installed pack, but the catalog origin itself unreachable (SPEC
            // F104.10's own "fail soft" posture — a missing face is always named; the pack suggestion
            // is best-effort only),
            var themeStore = new FakeThemeStore();
            await using var factory = new WidenedFontLawWebFactory(
                themeStore, new FakeFontPackStore(), catalogIndexUrl: WidenedFontLawFixtures.UnreachableCatalogUrl);
            var client = await WidenedFontLawWebFactory.LoggedInClientAsync(factory);

            // When a theme referencing that (would-be) pack face is imported,
            var response = await client.PostAsync(
                "/api/themes/pack-absent-theme/import",
                JsonBody(WidenedFontLawFixtures.ManifestReferencing("pack-absent-theme", WidenedFontLawFixtures.SmallPackAssetSrc)));
            var body = await response.Content.ReadAsStringAsync();

            // Then it still refuses 400 naming the missing face — the base refusal never depends on
            // the catalog resolving — with no pack suggestion in the body.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(WidenedFontLawFixtures.SmallPackAssetSrc, body, StringComparison.Ordinal);
            Assert.DoesNotContain("is provided by pack", body, StringComparison.Ordinal);
        }
    }

    static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — combines
/// Story272_ThemeImport.cs's own <c>ThemeImportWebFactory</c> idiom (<see cref="IThemeStore"/> replaced
/// by a <see cref="FakeThemeStore"/>) with Story282_FontPackInstall.cs's own
/// <c>FontPackInstallWebFactory</c> idiom (a fake catalog origin behind <see cref="IHttpClientFactory"/>,
/// <see cref="IFontPackStore"/> replaced by a <see cref="FakeFontPackStore"/>) — this file's own Facts
/// need BOTH real routes (install, then import) against the SAME running app, no live Postgres.
/// </summary>
file sealed class WidenedFontLawWebFactory(
    FakeThemeStore? themeStore = null, FakeFontPackStore? fontPackStore = null,
    HttpMessageHandler? handler = null, string catalogIndexUrl = WidenedFontLawFixtures.IndexUrl)
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story285-widenedfontlaw";

    readonly FakeThemeStore store = themeStore ?? new FakeThemeStore();
    readonly FakeFontPackStore packStore = fontPackStore ?? new FakeFontPackStore();

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
                new SingleHandlerHttpClientFactory(handler ?? WidenedFontLawFixtures.BuildRoutedHandler()));

            services.RemoveAll<IThemeStore>();
            services.AddSingleton<IThemeStore>(store);

            services.RemoveAll<IFontPackStore>();
            services.AddSingleton<IFontPackStore>(packStore);
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
/// Fixture documents + a routed fake HTTP double for this file's own Facts — a fake catalog origin
/// declaring TWO <c>kind:"font"</c> entries: <see cref="SmallPackSlug"/> (a small face, for AC1's happy
/// path and AC2's missing-pack sad path) and <see cref="BigPackSlug"/> (a deliberately large face, for
/// AC1's byte-ceiling-sums-across-both-sets proof). Neither asset is a real woff2 binary — the theme
/// import route never parses a face's payload, only its declared byte size, so any non-empty content
/// proves this file's own claims just as well (mirrors Story283_InstalledFontServing.cs's own
/// <c>AssetBytes</c> reasoning). <c>file</c>-scoped — this file's own committed copy, mirroring every
/// other Story2xx spec's "each file needs its own copy" idiom.
/// </summary>
file static class WidenedFontLawFixtures
{
    public const string IndexUrl = "https://catalog.test/repo/widened-law-index.json";
    const string Directory = "https://catalog.test/repo/";

    // A loopback port nothing listens on — immediate connection-refused, no DNS lookup, no timeout
    // wait (mirrors Story283_InstalledFontServing.cs's own UnreachableCatalogUrl idiom).
    public const string UnreachableCatalogUrl = "http://127.0.0.1:1/repo/index.json";

    // Deliberately NOT a substring of SmallPackAssetFile below (review finding, T205 gate-integrity
    // hole: "space-grotesk" as both slug and a substring of the woff2 filename let the pack-slug
    // assertion pass even with the enrichment code path deleted outright — the base refusal already
    // prints the asset src, which contained the slug as a substring). A distinct root word keeps the
    // pack-slug assertion discriminating between the base refusal and the real enrichment sentence.
    public const string SmallPackSlug = "grotesk-pack";
    const string SmallPackFamily = "Space Grotesk";
    const string SmallPackAssetFile = "space-grotesk-variable-latin.woff2";
    public const string SmallPackAssetSrc = "/fonts/" + SmallPackAssetFile;
    static readonly byte[] SmallPackAssetBytes = "small installed face bytes (T205 widened font law specs)"u8.ToArray();

    public const string BigPackSlug = "big-face-pack";
    const string BigPackFamily = "Big Face";
    const string BigPackAssetFile = "big-face-variable-latin.woff2";
    public const string BigPackAssetSrc = "/fonts/" + BigPackAssetFile;

    // Alone, comfortably under both the 204,800-byte per-theme ceiling (ThemeFontProvenanceValidator)
    // AND FontPackController's OWN 204,800-byte per-pack install ceiling (plus this fixture's own small
    // OFL text asset) — so installing this pack succeeds. Combined with the real vendored Fraunces face
    // (67,304 bytes, fonts-provenance.json) it references alongside, the SUM (217,304 bytes) crosses
    // the per-theme ceiling — the exact "sums across BOTH sets" proof ThePerThemeCeilingSumsRecordedBytesAcrossVendoredAndInstalled
    // needs.
    static readonly byte[] BigPackAssetBytes = Filler(0xAB, 150_000);

    const string OflText = "SIL Open Font License, Version 1.1 — test fixture licence text for T205's own specs.";

    static byte[] Filler(byte value, int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, value);
        return bytes;
    }

    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    static string ManifestJson(string family, string assetFile, int assetBytes) => $$"""
        {"family":"{{family}}","files":[{"role":"upright","file":"{{assetFile}}","weight":"400","style":"normal","bytes":{{assetBytes}}}],"license":"OFL-1.1","sourceUrl":"https://example.test/widened-font-law","version":"1.0","subset":"text"}
        """;

    static string MetaJson(string slug) => $$"""
        {"author":"Test Fixture","description":"A pack for the widened font law's own specs ({{slug}}).","audience":"everyone","added":"2026-08-05"}
        """;

    static string IndexJson() => $$"""
        { "generatedAt": "2026-08-05", "entries": [
          { "slug": "{{SmallPackSlug}}", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/{{SmallPackSlug}}/{{SmallPackSlug}}.font.json", "sha256": "{{Sha256Hex(ManifestJson(SmallPackFamily, SmallPackAssetFile, SmallPackAssetBytes.Length))}}" },
            "meta": { "path": "entries/{{SmallPackSlug}}/{{SmallPackSlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson(SmallPackSlug))}}" },
            "assets": [
              { "path": "entries/{{SmallPackSlug}}/{{SmallPackAssetFile}}", "sha256": "{{Sha256Hex(SmallPackAssetBytes)}}", "bytes": {{SmallPackAssetBytes.Length}} },
              { "path": "entries/{{SmallPackSlug}}/OFL.txt", "sha256": "{{Sha256Hex(OflText)}}", "bytes": {{Encoding.UTF8.GetByteCount(OflText)}} }
            ] },
          { "slug": "{{BigPackSlug}}", "kind": "font", "audience": "everyone",
            "manifest": { "path": "entries/{{BigPackSlug}}/{{BigPackSlug}}.font.json", "sha256": "{{Sha256Hex(ManifestJson(BigPackFamily, BigPackAssetFile, BigPackAssetBytes.Length))}}" },
            "meta": { "path": "entries/{{BigPackSlug}}/{{BigPackSlug}}.meta.json", "sha256": "{{Sha256Hex(MetaJson(BigPackSlug))}}" },
            "assets": [
              { "path": "entries/{{BigPackSlug}}/{{BigPackAssetFile}}", "sha256": "{{Sha256Hex(BigPackAssetBytes)}}", "bytes": {{BigPackAssetBytes.Length}} },
              { "path": "entries/{{BigPackSlug}}/OFL.txt", "sha256": "{{Sha256Hex(OflText)}}", "bytes": {{Encoding.UTF8.GetByteCount(OflText)}} }
            ] } ] }
        """;

    /// <summary>A minimal, otherwise-valid theme manifest whose SANS face names
    /// <paramref name="sansSrc"/> (the one under test — installed, absent, or oversized) and whose
    /// DISPLAY face names the real vendored Fraunces face — so every Fact drives exactly ONE variable
    /// through the widened law.</summary>
    public static string ManifestReferencing(string slug, string sansSrc) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Widened Font Law Fixture",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces-variable-latin.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Test Sans", "assets": [ { "src": "{{sansSrc}}", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;

    /// <summary>Serves every fixture document at its own resolved URL, 404 for anything else — mirrors
    /// Story282_FontPackInstall.cs's own <c>FontPackInstallFixtures.BuildRoutedHandler</c>.</summary>
    public static FakeHttpMessageHandler BuildRoutedHandler()
    {
        var routes = new Dictionary<string, string>
        {
            [IndexUrl] = IndexJson(),
            [Directory + "entries/" + SmallPackSlug + "/" + SmallPackSlug + ".font.json"] = ManifestJson(SmallPackFamily, SmallPackAssetFile, SmallPackAssetBytes.Length),
            [Directory + "entries/" + SmallPackSlug + "/" + SmallPackSlug + ".meta.json"] = MetaJson(SmallPackSlug),
            [Directory + "entries/" + SmallPackSlug + "/OFL.txt"] = OflText,
            [Directory + "entries/" + BigPackSlug + "/" + BigPackSlug + ".font.json"] = ManifestJson(BigPackFamily, BigPackAssetFile, BigPackAssetBytes.Length),
            [Directory + "entries/" + BigPackSlug + "/" + BigPackSlug + ".meta.json"] = MetaJson(BigPackSlug),
            [Directory + "entries/" + BigPackSlug + "/OFL.txt"] = OflText,
        };
        var assetBytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [Directory + "entries/" + SmallPackSlug + "/" + SmallPackAssetFile] = SmallPackAssetBytes,
            [Directory + "entries/" + BigPackSlug + "/" + BigPackAssetFile] = BigPackAssetBytes,
        };

        return new((request, _) =>
        {
            var absoluteUri = request.RequestUri!.AbsoluteUri;
            if (assetBytesByUrl.TryGetValue(absoluteUri, out var assetBytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(assetBytes) });

            return Task.FromResult(
                routes.TryGetValue(absoluteUri, out var body)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        });
    }
}
