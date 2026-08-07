// STORY-286 — The editor mixes components (SPEC F104.11, F104.12 · PLAN T206)
//
// BDD specification — xUnit. This file pins the TWO new read-only GET routes the v2 editor's
// pickers consume: GET /api/themes (every resolvable theme's full manifest — the base-theme picker)
// and GET /api/fonts/vendored (T206 review finding F4 widened this to the editor's ENTIRE assignable
// face set — vendored ∪ installed, one row per family — not "vendored" alone; the name survives, the
// shape widened). Neither route writes anything, so this file has no "the remix persists nothing"
// scenario of its own to prove — that guarantee is structural (this file's two routes are GET, and
// the ONLY write-shaped route the editor's client ever calls is the ALREADY-PINNED POST
// /api/themes/preview, Story274_ThemeCatalogPreview.cs's own transient-compose contract, reused
// verbatim, not re-derived here) and is proven client-side instead, in
// admin-ui/__specs__/theme-editor.spec.tsx.
//
// WIRED T206 — every Fact below drives the real production route table through
// WebApplicationFactory<Program> (EditorDataWebFactory below). GET /api/themes reads only the
// already-loaded ThemeCatalog singleton — no catalog HTTP double needed for it. GET
// /api/fonts/vendored reads BOTH the embedded FontProvenanceCatalog singleton AND (since the F4
// widening) IFontPackStore.GetAllAsync, so this factory always swaps in a FakeFontPackStore (empty by
// default) the same way its Story282/283/284/285 siblings do — never the real Postgres-backed
// repository against the deliberately-unreachable "Host=nowhere" connection string every factory in
// this project uses. Neither route ever reaches CatalogProxyService/the Community Catalog HTTP
// client, so this factory still needs no catalog HTTP double for those. The "any resolvable theme"
// half of AC1 (shipped ∪ imported ∪ saved) is proven by seeding a FakeThemeStore with one owner row
// and driving ThemeCatalog.ReloadOwnerThemesAsync explicitly (mirrors
// Story278_ThemeCatalogIsolation.cs's own "live DB" idiom) — the theme is written directly, never
// through the import route, so ThemeFixtures.ValidManifestJson's non-vendored font src (never POSTed
// through ThemeFontProvenanceValidator's gate) is fine here, same as Story278's own
// BuildLiveThemeStoreAsync precedent.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the T206
// review-obligation rider (finding F1: the catalog kill switch does not gate either of this file's own
// GET routes) sits between the happy path and the sad path, mirroring Story284's own placement of its
// identical rider; anonymous access is its own block.

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
using GenWave.Core.Domain;
using GenWave.Host.Theming;
using GenWave.Host.Tests.Fakes;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureEditorComposesTheRemix
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheBaseThemePickerListsEveryResolvableTheme
    {
        [Fact]
        public async Task ShippedThemesListWithFullManifests()
        {
            // Given no owner themes — the shipped-only floor,
            await using var factory = new EditorDataWebFactory();
            var client = await EditorDataWebFactory.LoggedInClientAsync(factory);

            // When the base-theme picker's own list is fetched,
            var response = await client.GetAsync("/api/themes");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Then both shipped themes (SPEC F102.5's cats-whisker default + test-pattern) list, each
            // carrying its own full manifest — slug/name/fonts/modes, not merely the slug/label pair
            // Station:Theme's own settings choices carry (AC1's "any resolvable theme").
            var slugs = document.RootElement.EnumerateArray()
                .Select(entry => entry.GetProperty("slug").GetString())
                .ToArray();
            var cats = document.RootElement.EnumerateArray()
                .Single(entry => entry.GetProperty("slug").GetString() == ThemeCatalog.ShippedDefaultSlug);
            Assert.Equal(
                (Status: HttpStatusCode.OK, HasBothShipped: true,
                 DisplayFamily: "Fraunces", SansFamily: "Source Sans 3", HasLightMode: true, HasDarkMode: true),
                (Status: response.StatusCode,
                 HasBothShipped: slugs.Contains(ThemeCatalog.ShippedDefaultSlug) && slugs.Contains("test-pattern"),
                 DisplayFamily: cats.GetProperty("fonts").GetProperty("display").GetProperty("family").GetString(),
                 SansFamily: cats.GetProperty("fonts").GetProperty("sans").GetProperty("family").GetString(),
                 HasLightMode: cats.GetProperty("modes").TryGetProperty("light", out _),
                 HasDarkMode: cats.GetProperty("modes").TryGetProperty("dark", out _)));
        }

        [Fact]
        public async Task AnOwnerImportedOrSavedThemeListsAlongsideTheShippedSet()
        {
            // Given an owner theme written directly to the store (mirrors Story278's own
            //       BuildLiveThemeStoreAsync — the theme is never posted through the import route, so
            //       ThemeFixtures' non-vendored font src is fine here) and reloaded into the running
            //       ThemeCatalog singleton (the boot-warm-up hosted service is removed from this
            //       factory, so this Fact drives the reload explicitly, the SAME "simulate boot
            //       warm-up" idiom Story278/Story283 already use),
            var store = new FakeThemeStore();
            const string ownerSlug = "editor-owner-theme";
            await store.UpsertAsync(ownerSlug, ThemeFixtures.ValidManifestJson(ownerSlug), "file", CancellationToken.None);
            await using var factory = new EditorDataWebFactory(store);
            var client = await EditorDataWebFactory.LoggedInClientAsync(factory);
            await factory.Services.GetRequiredService<ThemeCatalog>().ReloadOwnerThemesAsync(CancellationToken.None);

            // When the base-theme picker's own list is fetched,
            var response = await client.GetAsync("/api/themes");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Then the owner theme lists ALONGSIDE the two shipped ones (AC1: "shipped, imported, or
            // saved" is one union, not three separate lists) — three entries total.
            var slugs = document.RootElement.EnumerateArray()
                .Select(entry => entry.GetProperty("slug").GetString())
                .ToArray();
            Assert.Equal(
                (Count: 3, HasOwnerTheme: true),
                (Count: slugs.Length, HasOwnerTheme: slugs.Contains(ownerSlug)));
        }
    }

    public sealed class ScenarioTheRoleVendoredListReturnsTheCuratedSet
    {
        [Fact]
        public async Task TheCuratedNonItalicFacesListOnePerFamily()
        {
            // Given the running app's own embedded curated font set and no installed packs (no
            //       fixture needed — this route reads the real fonts-provenance.json this app ships;
            //       the installed half joins the SET in a separate Fact below),
            await using var factory = new EditorDataWebFactory();
            var client = await EditorDataWebFactory.LoggedInClientAsync(factory);

            // When the role pickers' assignable list is fetched,
            var response = await client.GetAsync("/api/fonts/vendored");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Then every curated family lists exactly once (Fraunces' own italic file does NOT add a
            // second "Fraunces" row) with its own /fonts/ src. The Fraunces-row lookup is asserted
            // BEFORE the tuple comparison (review finding N1): a regression that makes FraunicesRows
            // != 1 must fail as a named Assert.True with the actual rows listed, never as an unhandled
            // InvalidOperationException from a bare .Single() evaluated inside the tuple literal.
            var entries = document.RootElement.EnumerateArray()
                .Select(entry => (Family: entry.GetProperty("family").GetString(), Src: entry.GetProperty("src").GetString()))
                .ToArray();
            var fraunces = entries.Where(entry => entry.Family == "Fraunces").ToArray();
            Assert.True(
                fraunces.Length == 1,
                $"expected exactly one \"Fraunces\" row, found {fraunces.Length}: [{string.Join(", ", fraunces.Select(f => f.Src))}]");
            Assert.Equal(
                (Status: HttpStatusCode.OK, FamilyCount: 4, FraunicesSrc: "/fonts/fraunces-variable-latin.woff2"),
                (Status: response.StatusCode,
                 FamilyCount: entries.Select(entry => entry.Family).Distinct().Count(),
                 FraunicesSrc: fraunces[0].Src));
        }

        [Fact]
        public async Task NoItalicFileEverAppearsAsItsOwnRow()
        {
            // Given the same curated set,
            await using var factory = new EditorDataWebFactory();
            var client = await EditorDataWebFactory.LoggedInClientAsync(factory);

            // When the vendored list is fetched,
            var response = await client.GetAsync("/api/fonts/vendored");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Then no row's src names an italic file — the role pickers offer a family, never a
            // style/weight axis (SPEC F104.11's "component mix only").
            var srcs = document.RootElement.EnumerateArray()
                .Select(entry => entry.GetProperty("src").GetString())
                .ToArray();
            Assert.DoesNotContain(srcs, src => src != null && src.Contains("italic", StringComparison.Ordinal));
        }

        // ── T206 review finding F4: the installed half joins the SAME set, one row per family ──

        [Fact]
        public async Task AnInstalledPackJoinsTheSetAsOneRowCarryingItsOwnNormalStyleFace()
        {
            // Given one installed pack carrying an upright ("normal") face and an italic one — the
            //       SAME "which face represents this family" question the vendored half answers by
            //       filename, now answered for the installed half by the pack's own recorded
            //       FontPackFace.Style column instead (review finding F4: one heuristic per half,
            //       both resolved HERE server-side, never re-derived client-side),
            var upright = new FontPackFace("editor-test-pack-variable-latin.woff2", "normal", 4096, new string('a', 64));
            var italic = new FontPackFace("editor-test-pack-italic-variable-latin.woff2", "italic", 3072, new string('b', 64));
            var pack = new FontPack(
                "editor-test-pack", "Editor Test Pack", "{}", "editor-test-pack", DateTime.UtcNow, DateTime.UtcNow, [italic, upright]);
            await using var factory = new EditorDataWebFactory(fontPackStore: new FakeFontPackStore(pack));
            var client = await EditorDataWebFactory.LoggedInClientAsync(factory);

            // When the role pickers' assignable list is fetched,
            var response = await client.GetAsync("/api/fonts/vendored");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Then the installed pack joins the curated set as EXACTLY one row, carrying its
            // "normal"-style face's src — never the italic one, and never a second row for the same
            // family (the same N1-shaped restructuring: a named Assert.True before the value check).
            var entries = document.RootElement.EnumerateArray()
                .Select(entry => (Family: entry.GetProperty("family").GetString(), Src: entry.GetProperty("src").GetString()))
                .ToArray();
            var installedRows = entries.Where(entry => entry.Family == pack.Family).ToArray();
            Assert.True(
                installedRows.Length == 1,
                $"expected exactly one \"{pack.Family}\" row, found {installedRows.Length}: [{string.Join(", ", installedRows.Select(e => e.Src))}]");
            Assert.Equal("/fonts/editor-test-pack-variable-latin.woff2", installedRows[0].Src);
        }
    }

    // ── T206 REVIEW-OBLIGATION RIDER (finding F1): the catalog kill switch does NOT gate either of this file's own GET routes ──

    public sealed class ScenarioTheCatalogKillSwitchDoesNotGateTheEditorReads
    {
        [Fact]
        public async Task VendoredAndThemesStay200WhileInstallStill404sWithTheCatalogDisabled()
        {
            // Given the catalog kill switch flipped (an empty Community:CatalogIndexUrl, SPEC F90.1)
            //       — the T203 rider pattern (Story284_FontPackLibrary.cs's own
            //       ScenarioTheCatalogKillSwitchDoesNotGateTheLibrary) applied to this file's own two
            //       new GET routes: both read station-local/embedded data that outlives the catalog
            //       (SPEC F104.8), so neither should vary with the switch, unlike the catalog-facing
            //       install route,
            await using var factory = new EditorDataWebFactory(catalogIndexUrl: "");
            var client = await EditorDataWebFactory.LoggedInClientAsync(factory);

            // When the role pickers' assignable list, the base-theme list, and an install attempt are
            // all requested,
            var vendoredResponse = await client.GetAsync("/api/fonts/vendored");
            var themesResponse = await client.GetAsync("/api/themes");
            var installResponse = await client.PostAsync("/api/fonts/anything/install", null);

            // Then both GETs still 200 while the install route still 404s bare — the exact divergence
            // Story284's own Fact pins for the sibling library route (SPEC F104.8's "the kill switch
            // gates the CATALOG surface, never the station's own inventory" rule).
            Assert.Equal(
                (VendoredStatus: HttpStatusCode.OK, ThemesStatus: HttpStatusCode.OK, InstallStatus: HttpStatusCode.NotFound),
                (VendoredStatus: vendoredResponse.StatusCode, ThemesStatus: themesResponse.StatusCode,
                 InstallStatus: installResponse.StatusCode));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAnonymousAccess
    {
        [Fact]
        public async Task AnAnonymousThemesRequestIsUnauthorized()
        {
            // Given no session cookie,
            await using var factory = new EditorDataWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // When the base-theme list is requested anonymously,
            var response = await client.GetAsync("/api/themes");

            // Then it is refused 401 — the same deny-by-default posture every other /api/* route
            // carries.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task AnAnonymousVendoredRequestIsUnauthorized()
        {
            // Given no session cookie,
            await using var factory = new EditorDataWebFactory();
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // When the vendored face list is requested anonymously,
            var response = await client.GetAsync("/api/fonts/vendored");

            // Then it is refused 401 too.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts. Neither
/// <c>GET /api/themes</c> nor <c>GET /api/fonts/vendored</c> touches the Community Catalog HTTP
/// client — unlike every sibling factory under this prefix, this one wires no fake catalog origin —
/// but <c>GET /api/fonts/vendored</c> DOES touch <see cref="IFontPackStore"/> (T206 review finding F4
/// widened it to vendored ∪ installed), so <paramref name="fontPackStore"/> always gets swapped for a
/// <see cref="FakeFontPackStore"/> (empty when a Fact passes none), the SAME Story282/283/284/285
/// precedent, so this factory never reaches the real Postgres-backed repository against the
/// deliberately-unreachable "Host=nowhere" connection string below. <paramref name="themeStore"/> only
/// swaps <see cref="IThemeStore"/> when a Fact needs an owner theme in the running
/// <see cref="ThemeCatalog"/>. <paramref name="catalogIndexUrl"/> (T206 review finding F1) is a plain
/// non-empty test URL by default — enough to keep <see cref="Options.CommunityCatalogAccessor.IsEnabled"/>
/// true for every Fact that has no opinion on it — and only the kill-switch rider Fact passes an empty
/// one. <see cref="IHostedService"/> is removed (no boot warm-up), the SAME Story278/Story283
/// precedent, so a Fact that wants an owner theme visible drives
/// <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/> explicitly against the SAME DI'd singleton the
/// request pipeline reads.
/// </summary>
file sealed class EditorDataWebFactory(
    IThemeStore? themeStore = null,
    IFontPackStore? fontPackStore = null,
    string catalogIndexUrl = "https://catalog.test/repo/index.json")
    : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story286-editor";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", catalogIndexUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            if (themeStore is not null)
            {
                services.RemoveAll<IThemeStore>();
                services.AddSingleton(themeStore);
            }

            services.RemoveAll<IFontPackStore>();
            services.AddSingleton<IFontPackStore>(fontPackStore ?? new FakeFontPackStore());
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
