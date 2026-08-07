// STORY-288 — Uninstall with the guard (SPEC F104.14 · PLAN T208)
//
// BDD specification — xUnit. DELETE /api/fonts/{slug} removes an installed pack UNLESS a saved/
// imported station.theme row still references one of its faces — refused 409, naming every
// referencing theme, with nothing removed; with no reference, 204, and the widened GET /fonts/{file}
// route stops serving the pack's faces on the very next request.
//
// WIRED T208 — every Fact below drives the real production DELETE /api/fonts/{slug} route through
// WebApplicationFactory<Program> (FontPackUninstallWebFactory below), mirroring
// Story283_InstalledFontServing.cs's own InstalledFontServingWebFactory idiom exactly (a
// FakeFontPackStore, no live Postgres — this project carries none). Packs are seeded directly via
// FakeFontPackStore.WithInstalledFace (Story283's own "write straight to the fake store" precedent —
// this file's own concern is uninstalling an already-installed pack, not installing one) and
// InstalledFontCatalog.ReloadAsync is driven explicitly first, the same "boot warm-up, simulated"
// idiom Story283 uses, so the "stops serving on the next request" claim is proven against a face that
// was GENUINELY serving beforehand, not a vacuous 404-to-404.
//
// The referenced-pack guard is scripted via FakeFontPackStore.ReferencingThemeSlugs (that fake's own
// remarks explain why: it carries no knowledge of station.theme by design — the REAL cross-table
// substring-search guard is FontPackRepository.DeleteAsync's own concern, proven against real Postgres
// in GenWave.MediaLibrary.Tests/Specs/Story288_FontPackUninstall.cs instead). What THIS file proves is
// FontPackController.Uninstall's own response mapping over a real HTTP request — a guard removed from
// that switch (e.g. a branch that ignores FontPackDeleteResult.Referenced and always 204s) fails
// ScenarioAReferencedPackRefuses below; a dropped post-delete InstalledFontCatalog.ReloadAsync call
// fails ScenarioAnUnreferencedPackUninstalls's own StopsServing Fact.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad path
// (referenced, unknown slug, bad slug) is its own block.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Theming;
using GenWave.Host.Tests.Fakes;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureFontPackUninstall
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAnUnreferencedPackUninstalls
    {
        [Fact]
        public async Task TheDeleteRespondsNoContentAndTheLibraryNoLongerListsIt()
        {
            // Given an installed pack no theme references (FakeFontPackStore's own
            // ReferencingThemeSlugs default — empty),
            var store = FakeFontPackStore.WithInstalledFace(
                FontPackUninstallFixtures.Slug, FontPackUninstallFixtures.Family, FontPackUninstallFixtures.AssetFile,
                FontPackUninstallFixtures.AssetBytes, FontPackUninstallFixtures.AssetSha256);
            await using var factory = new FontPackUninstallWebFactory(store);
            var client = await FontPackUninstallWebFactory.LoggedInClientAsync(factory);

            // When it is uninstalled through the real production route,
            var response = await client.DeleteAsync($"/api/fonts/{FontPackUninstallFixtures.Slug}");

            // Then it responds 204, and the library listing no longer carries it (AC1's "its rows go").
            var listing = await client.GetAsync("/api/fonts");
            Assert.Equal(
                (DeleteStatus: HttpStatusCode.NoContent, Listing: "[]"),
                (DeleteStatus: response.StatusCode, Listing: await listing.Content.ReadAsStringAsync()));
        }

        [Fact]
        public async Task FontsStopsServingThePacksFaceOnTheNextRequest()
        {
            // Given the same pack, already folded into InstalledFontCatalog's snapshot (the "boot
            // warm-up already completed" idiom, Story283's own precedent) — genuinely serving before
            // this Fact's own uninstall, so the 404 that follows is proven against a real transition,
            // not a vacuous 404-to-404,
            var store = FakeFontPackStore.WithInstalledFace(
                FontPackUninstallFixtures.Slug, FontPackUninstallFixtures.Family, FontPackUninstallFixtures.AssetFile,
                FontPackUninstallFixtures.AssetBytes, FontPackUninstallFixtures.AssetSha256);
            await using var factory = new FontPackUninstallWebFactory(store);
            var client = await FontPackUninstallWebFactory.LoggedInClientAsync(factory);
            await factory.Services.GetRequiredService<InstalledFontCatalog>().ReloadAsync(CancellationToken.None);
            var servingBefore = await client.GetAsync($"/fonts/{FontPackUninstallFixtures.AssetFile}");

            // When it is uninstalled,
            var uninstall = await client.DeleteAsync($"/api/fonts/{FontPackUninstallFixtures.Slug}");
            Assert.True(uninstall.IsSuccessStatusCode, await uninstall.Content.ReadAsStringAsync());

            // Then the very next request for that face 404s — the uninstall's own post-write
            // InstalledFontCatalog.ReloadAsync rebuild hook (mirrors Install's own T199/T200 precedent)
            // reached the request pipeline with no process restart.
            var servingAfter = await client.GetAsync($"/fonts/{FontPackUninstallFixtures.AssetFile}");
            Assert.Equal(
                (ServedBefore: HttpStatusCode.OK, ServedAfter: HttpStatusCode.NotFound),
                (ServedBefore: servingBefore.StatusCode, ServedAfter: servingAfter.StatusCode));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAReferencedPackRefuses
    {
        [Fact]
        public async Task ItRefusesWith409NamingEveryReferencingThemeAndRemovesNothing()
        {
            // Given a pack a saved theme still references (AC2 — the store's own guard, scripted here
            // per this file's own header remarks),
            var store = FakeFontPackStore.WithInstalledFace(
                FontPackUninstallFixtures.Slug, FontPackUninstallFixtures.Family, FontPackUninstallFixtures.AssetFile,
                FontPackUninstallFixtures.AssetBytes, FontPackUninstallFixtures.AssetSha256);
            store.ReferencingThemeSlugs = ["midnight-drive", "sunday-static"];
            await using var factory = new FontPackUninstallWebFactory(store);
            var client = await FontPackUninstallWebFactory.LoggedInClientAsync(factory);

            // When uninstall is attempted,
            var response = await client.DeleteAsync($"/api/fonts/{FontPackUninstallFixtures.Slug}");
            var detail = await DetailAsync(response);

            // Then it refuses 409, naming BOTH referencing themes, and the library still lists the
            // pack — nothing removed.
            var listing = await client.GetAsync("/api/fonts");
            using var listingDocument = JsonDocument.Parse(await listing.Content.ReadAsStringAsync());
            Assert.Equal(
                (Status: HttpStatusCode.Conflict, NamesFirst: true, NamesSecond: true, StillListed: true),
                (Status: response.StatusCode,
                 NamesFirst: detail.Contains("midnight-drive", StringComparison.Ordinal),
                 NamesSecond: detail.Contains("sunday-static", StringComparison.Ordinal),
                 StillListed: Assert.Single(listingDocument.RootElement.EnumerateArray())
                     .GetProperty("slug").GetString() == FontPackUninstallFixtures.Slug));
        }

        [Fact]
        public async Task TheFaceKeepsServingWhenTheDeleteWasRefused()
        {
            // Given the same referenced pack, already serving,
            var store = FakeFontPackStore.WithInstalledFace(
                FontPackUninstallFixtures.Slug, FontPackUninstallFixtures.Family, FontPackUninstallFixtures.AssetFile,
                FontPackUninstallFixtures.AssetBytes, FontPackUninstallFixtures.AssetSha256);
            store.ReferencingThemeSlugs = ["midnight-drive"];
            await using var factory = new FontPackUninstallWebFactory(store);
            var client = await FontPackUninstallWebFactory.LoggedInClientAsync(factory);
            await factory.Services.GetRequiredService<InstalledFontCatalog>().ReloadAsync(CancellationToken.None);

            // When uninstall is attempted and refused,
            var uninstall = await client.DeleteAsync($"/api/fonts/{FontPackUninstallFixtures.Slug}");
            Assert.Equal(HttpStatusCode.Conflict, uninstall.StatusCode);

            // Then the face still serves — a refused uninstall never rebuilds (nor needs to rebuild)
            // InstalledFontCatalog's snapshot.
            var stillServing = await client.GetAsync($"/fonts/{FontPackUninstallFixtures.AssetFile}");
            Assert.Equal(HttpStatusCode.OK, stillServing.StatusCode);
        }
    }

    // ── REVIEW FINDING N6 — the reference-shape tripwire ───────────────────

    /// <summary>
    /// <see cref="GenWave.MediaLibrary.Station.FontPackRepository.DeleteAsync"/>'s own guard is a TEXT
    /// substring search for a quoted <c>"/fonts/&lt;file&gt;"</c> literal (that type's own remarks) —
    /// it has NO idea what a <see cref="ThemeManifest"/> is, by design (the "opaque jsonb" discipline).
    /// If GenWave.Host ever changed HOW a theme references a face — a different key shape, an escaped
    /// path, an indirection through an id instead of a literal src string — the guard would go quietly
    /// blind: it would keep compiling, keep running, and simply stop finding real references, with no
    /// test anywhere failing to say so. This Fact is that tripwire: it pins the ACTUAL byte shape
    /// <see cref="ThemeManifestSerializer.Serialize"/> produces for an assigned face today, so a future
    /// change to that shape shows up as a RED Host fact (the layer that owns the shape), not as silence
    /// in the MediaLibrary guard it would otherwise starve.
    /// </summary>
    public sealed class ScenarioTheReferenceShapeTheGuardDependsOn
    {
        [Fact]
        public void ASerializedManifestCarriesTheGuardsQuotedFontsPathSubstring()
        {
            // Given a theme manifest wearing an installed face — the exact composition shape the v2
            // editor's save-as-own produces (SPEC F104.11/F104.13), built directly (not parsed) since
            // this Fact's only concern is what the SERIALIZER emits,
            const string file = "space-grotesk-variable-latin.woff2";
            var manifest = new ThemeManifest(
                "wears-an-installed-face",
                "Wears An Installed Face",
                "GenWave",
                new ThemeFonts(
                    new ThemeFontFace("Space Grotesk", [new ThemeFontAsset($"/fonts/{file}", "400", "normal")]),
                    new ThemeFontFace("Source Sans 3", [new ThemeFontAsset("/fonts/source-sans-3-variable-latin.woff2", "400", "normal")])),
                new ThemeModes(
                    new Dictionary<string, string> { ["bg"] = "#ffffff" },
                    new Dictionary<string, string> { ["bg"] = "#000000" }));

            // When it is serialized — the SAME call station.theme write routes make before ever
            // persisting a row (ThemesSaveAsOwnController.SaveAsOwn, ThemesImportController.Import),
            var json = ThemeManifestSerializer.Serialize(manifest);

            // Then the quoted "/fonts/<file>" substring FontPackRepository.DeleteAsync's own guard
            // searches for is present verbatim — the one fact this whole guard's correctness rests on.
            Assert.Contains($"\"/fonts/{file}\"", json, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioRejectingBadRequests
    {
        [Fact]
        public async Task AnUnknownSlugRefuses404()
        {
            // Given no pack installed under this slug at all,
            var store = new FakeFontPackStore();
            await using var factory = new FontPackUninstallWebFactory(store);
            var client = await FontPackUninstallWebFactory.LoggedInClientAsync(factory);

            // When uninstall is attempted,
            var response = await client.DeleteAsync("/api/fonts/no-such-pack");

            // Then it 404s.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task AMalformedSlugRefuses400()
        {
            // Given a route slug outside the lowercase/digit/single-hyphen shape every other api/fonts
            // route already enforces,
            var store = new FakeFontPackStore();
            await using var factory = new FontPackUninstallWebFactory(store);
            var client = await FontPackUninstallWebFactory.LoggedInClientAsync(factory);

            // When uninstall is attempted,
            var response = await client.DeleteAsync("/api/fonts/Not_A_Valid_Slug");

            // Then it 400s — the same slug-format gate Install's own route already carries.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task AnAnonymousRequestIsUnauthorized()
        {
            // Given no session cookie (the SAME AdminSurface+Settings pairing every other api/fonts
            // route carries),
            var store = FakeFontPackStore.WithInstalledFace(
                FontPackUninstallFixtures.Slug, FontPackUninstallFixtures.Family, FontPackUninstallFixtures.AssetFile,
                FontPackUninstallFixtures.AssetBytes, FontPackUninstallFixtures.AssetSha256);
            await using var factory = new FontPackUninstallWebFactory(store);
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // When uninstall is attempted anonymously,
            var response = await client.DeleteAsync($"/api/fonts/{FontPackUninstallFixtures.Slug}");

            // Then it is refused 401.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString() ?? "";
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// <c>Story283_InstalledFontServing.cs</c>'s own <c>InstalledFontServingWebFactory</c> (a
/// <see cref="FakeFontPackStore"/>, no live Postgres). No catalog fixture is needed here — this file
/// never drives the real install route, only pre-seeded packs — but the catalog kill switch is still
/// pinned OFF (an empty <c>Community:CatalogIndexUrl</c>) so this file never depends on a reachable
/// origin it has no use for.
/// </summary>
file sealed class FontPackUninstallWebFactory(FakeFontPackStore store) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story288-fontuninstall";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", "");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

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

/// <summary>Fixed inputs shared across this file's Facts — mirrors
/// <c>Story283_InstalledFontServing.cs</c>'s own <c>InstalledFontServingFixtures</c> idiom
/// (<c>file</c>-scoped, its own committed copy).</summary>
file static class FontPackUninstallFixtures
{
    public const string Slug = "uninstall-test-pack";
    public const string Family = "Uninstall Test";
    public const string AssetFile = "uninstall-test-variable-latin.woff2";

    public static readonly byte[] AssetBytes = "installed face bytes for the uninstall specs (T208)"u8.ToArray();

    public static string AssetSha256 => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(AssetBytes));
}
