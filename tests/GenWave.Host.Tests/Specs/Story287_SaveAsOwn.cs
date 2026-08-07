// STORY-287 — Save-as-own (SPEC F104.13 · PLAN T207)
//
// BDD specification — xUnit. POST /api/themes/{slug}/save-as-own writes a complete ThemeManifest to
// station.theme with imported_from NULL, after passing the SAME parse/law/ceiling/shipped-slug gates
// as POST /api/themes/{slug}/import (SPEC F104.13, STORY-287 AC3's "same copy, same statuses") — this
// file's own byte-identical-copy Facts drive BOTH routes with the identical bad manifest and assert
// their refusal bodies are equal, rather than trusting a hand-copied literal either side could drift
// from unnoticed (mirrors Story285_WidenedFontLaw.cs's own T205 gate-integrity lesson: a substring
// match that could also be satisfied by the base refusal alone is not proof of byte-identity).
//
// WIRED T207 — every Fact below drives the real production POST /api/themes/{slug}/save-as-own AND
// POST /api/themes/{slug}/import routes through WebApplicationFactory<Program> (mirrors
// Story272_ThemeImport.cs's own ThemeImportWebFactory idiom), against a FakeThemeStore double — no
// live Postgres, this project has none. Community:CatalogIndexUrl is pinned to a dead loopback port
// (mirrors Story283/285's own UnreachableCatalogUrl idiom) so the font-law refusal's pack-suggestion
// enrichment (SPEC F104.10) never depends on live network reachability — its own fail-soft posture
// means every Fact here still gets the SAME base refusal either way, just without a pack suggestion
// neither route needs to prove byte-identity.
//
// One assertion per Fact where the scenario allows it (tuple-equality bundling where the scenario's
// own claim is genuinely composite); happy path first and exhaustive; the sad path (AC3) is its own
// block.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Theming;
using GenWave.Host.Tests.Fakes;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public sealed class FeatureSaveAsOwn
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioSaveWritesAnAuthoredTheme
    {
        [Fact]
        public async Task ASavedRemixLandsInStationThemeWithNullProvenance()
        {
            // Given a valid remix,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeSaveAsOwnWebFactory(themeStore);
            var client = await ThemeSaveAsOwnWebFactory.LoggedInClientAsync(factory);

            // When Save-as-own is confirmed with a name/slug,
            var response = await client.PostAsync(
                "/api/themes/my-remix/save-as-own", JsonBody(SaveAsOwnFixtures.ValidRemixManifestJson("my-remix")));

            // Then a complete manifest lands in station.theme with imported_from NULL (STORY-287 AC1)
            // — and, since ImportedAt is null EXACTLY when ImportedFrom is (OwnerTheme's own
            // invariant), imported_at stays null too: a mutation that stamped either field would fail
            // this one assertion.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await themeStore.GetBySlugAsync("my-remix", CancellationToken.None);
            Assert.Equal(
                (Found: true, ImportedFrom: (string?)null, ImportedAt: (DateTime?)null),
                (Found: stored is not null, ImportedFrom: stored?.ImportedFrom, ImportedAt: stored?.ImportedAt));
        }

        [Fact]
        public async Task TheSavedThemeIsImmediatelySelectableAndResolvable()
        {
            // Given a saved remix,
            await using var factory = new ThemeSaveAsOwnWebFactory();
            var client = await ThemeSaveAsOwnWebFactory.LoggedInClientAsync(factory);
            var saveResponse = await client.PostAsync(
                "/api/themes/my-remix/save-as-own", JsonBody(SaveAsOwnFixtures.ValidRemixManifestJson("my-remix")));
            Assert.True(saveResponse.IsSuccessStatusCode, await saveResponse.Content.ReadAsStringAsync());

            // When the base-theme picker's own list is read, and the visitor cookie selects it against
            // GET /api/theme.css (mirrors Story272_ThemeImport.cs's own
            // ItServesViaApiThemeCssThroughTheVisitorCookie — no restart, no live DB needed for cookie
            // precedence),
            var themesResponse = await client.GetAsync("/api/themes");
            var slugs = (await themesResponse.Content.ReadFromJsonAsync<JsonElement>())
                .EnumerateArray().Select(entry => entry.GetProperty("slug").GetString()).ToArray();

            var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var cssRequest = new HttpRequestMessage(HttpMethod.Get, "/api/theme.css");
            cssRequest.Headers.Add("Cookie", $"{ThemeCatalog.CookieName}=my-remix");
            var cssResponse = await anonymousClient.SendAsync(cssRequest);
            var css = await cssResponse.Content.ReadAsStringAsync();

            // Then it lists in the picker AND resolves via theme.css, carrying its own distinctive
            // token — not merely a 200 that could coincidentally match the shipped default.
            Assert.Equal(
                (ListedInPicker: true, CssStatus: HttpStatusCode.OK, CssCarriesDistinctiveToken: true),
                (ListedInPicker: slugs.Contains("my-remix"),
                 CssStatus: cssResponse.StatusCode,
                 CssCarriesDistinctiveToken: css.Contains($"--bg: {SaveAsOwnFixtures.DistinctiveLightBg};", StringComparison.Ordinal)));
        }
    }

    public sealed class ScenarioTheBaseThemeIsUntouched
    {
        [Fact]
        public async Task TheBaseThemeIsByteIdenticalAfterTheSave()
        {
            // Given a base theme already stored under its own slug — its manifest's OWN embedded
            // "slug" field is deliberately the BASE slug, exactly what EditorClient.tsx's
            // buildRemixManifest copies from a base theme before an operator overrides it (SPEC
            // F104.11's "...base" spread) — so this Fact proves the ROUTE slug governs storage even
            // when the posted body's own opinion still names the base theme,
            const string baseSlug = "base-remix-theme";
            var baseDefinition = ThemeFixtures.ValidManifestJson(baseSlug);
            var themeStore = new FakeThemeStore();
            await themeStore.UpsertAsync(baseSlug, baseDefinition, "file", CancellationToken.None);
            await using var factory = new ThemeSaveAsOwnWebFactory(themeStore);
            var client = await ThemeSaveAsOwnWebFactory.LoggedInClientAsync(factory);

            // When a remix carrying that same embedded slug is saved under a DIFFERENT route slug,
            var response = await client.PostAsync(
                "/api/themes/base-remix-theme-copy/save-as-own",
                JsonBody(SaveAsOwnFixtures.ValidRemixManifestJson("base-remix-theme-copy", embeddedSlug: baseSlug)));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // Then the base theme's own row is byte-identical to what was seeded — the write only
            // ever touched the NEW slug (a route-slug-normalization bug would otherwise silently
            // overwrite the base theme the remix was mixed from, the exact hazard SPEC F104.13's own
            // "saving never mutates the base theme" promises against).
            var stillBase = await themeStore.GetBySlugAsync(baseSlug, CancellationToken.None);
            Assert.Equal(baseDefinition, stillBase?.Definition);
        }
    }

    public sealed class ScenarioTheManifestsOwnSlugNeverGoverns
    {
        [Fact]
        public async Task TheManifestsOwnEmbeddedSlugIsOverriddenByTheRouteSlug()
        {
            // Given a manifest whose own embedded slug differs from the route it is POSTed to
            // (mirrors Story272_ThemeImport.cs's own ScenarioTheRouteSlugGovernsStorage),
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeSaveAsOwnWebFactory(themeStore);
            var client = await ThemeSaveAsOwnWebFactory.LoggedInClientAsync(factory);

            var response = await client.PostAsync(
                "/api/themes/route-slug/save-as-own",
                JsonBody(SaveAsOwnFixtures.ValidRemixManifestJson("route-slug", embeddedSlug: "embedded-slug")));
            var body = await response.Content.ReadFromJsonAsync<ThemeSaveAsOwnResponse>();

            // Then the route slug wins for both the response AND the stored definition's own slug
            // field — ThemeCatalog re-parses the stored definition, so a drift here would silently
            // misfile the theme under the manifest's own opinion instead.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal("route-slug", body?.Slug);
            var stored = await themeStore.GetBySlugAsync("route-slug", CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Contains("\"slug\":\"route-slug\"", stored!.Definition, StringComparison.Ordinal);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    /// <summary>
    /// PLAN T207 review finding F1 — REPLACES the two former hand-picked byte-identity Facts
    /// (a font-law Fact and a shipped-slug Fact, each proving its own ONE gate matches between routes)
    /// with ONE table-driven Fact driving every gate <see cref="ThemeWriteGate.RunAsync"/> enforces
    /// through BOTH routes: bad slug, oversized, newer schema-major, malformed JSON, unvendored face,
    /// over-ceiling, shipped slug (STORY-287 AC3's "same parse/law/ceiling/shipped-slug gates… same
    /// copy", now closing the coverage gaps N3 named — slug-format 400, 413, schema-major 400, and the
    /// ceiling half of AC3 had no Fact on this route before). This is true by CONSTRUCTION now that
    /// both routes call the same <see cref="ThemeWriteGate.RunAsync"/> (PLAN T207 review finding F1) —
    /// a gate REMOVED from that shared pipeline fails EVERY row in this table on BOTH routes at once,
    /// rather than one hand-picked Fact per gate a future gate could slip past unnoticed.
    /// </summary>
    public sealed class ScenarioTheSameGateRefusesBothRoutesIdentically
    {
        public static TheoryData<BadBodyRow> Rows
        {
            get
            {
                var data = new TheoryData<BadBodyRow>();
                foreach (var row in BadBodyTable.Rows)
                    data.Add(row);

                return data;
            }
        }

        [Theory]
        [MemberData(nameof(Rows))]
        public async Task BothRoutesRefuseWithTheIdenticalStatusAndDetail(BadBodyRow row)
        {
            // Given a bad body (see BadBodyTable's own remarks for what makes each row bad),
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeSaveAsOwnWebFactory(themeStore);
            var client = await ThemeSaveAsOwnWebFactory.LoggedInClientAsync(factory);

            // When it is POSTed to BOTH the import route and the save-as-own route, at the same route
            // slug (neither ever commits, so there is no upsert-key collision to avoid),
            var importResponse = await client.PostAsync($"/api/themes/{row.Slug}/import", JsonBody(row.Body));
            var saveResponse = await client.PostAsync($"/api/themes/{row.Slug}/save-as-own", JsonBody(row.Body));
            var importDetail = await DetailAsync(importResponse);
            var saveDetail = await DetailAsync(saveResponse);

            // Then both refuse with the row's own expected status AND the IDENTICAL detail text — and
            // that detail actually NAMES the offending thing (N2: a bare "DetailsMatch" that both sides
            // could vacuously satisfy with "" is not proof of byte-identity; a non-empty, content-bearing
            // fragment is).
            Assert.Equal(
                (ImportStatus: row.ExpectedStatus, SaveStatus: row.ExpectedStatus,
                 DetailsMatch: true, NamesTheExpectedContent: true),
                (ImportStatus: importResponse.StatusCode, SaveStatus: saveResponse.StatusCode,
                 DetailsMatch: importDetail == saveDetail,
                 NamesTheExpectedContent: saveDetail.Contains(row.ExpectedFragment, StringComparison.Ordinal)));
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }
    }

    public sealed class ScenarioOverwriteIsFailClosed
    {
        [Fact]
        public async Task SavingOntoASlugHoldingAnImportedThemeRefuses409AndLeavesTheRowUntouched()
        {
            // Given a slug already holding an IMPORTED theme (a non-null imported_from — the exact
            // provenance a save must never destroy, SPEC F104.13, PLAN T207 review finding F2),
            const string slug = "imported-target";
            var themeStore = new FakeThemeStore();
            await themeStore.UpsertAsync(
                slug, ThemeFixtures.ValidManifestJson(slug), "midnight-drive-catalog-entry", CancellationToken.None);
            var seeded = await themeStore.GetBySlugAsync(slug, CancellationToken.None);
            await using var factory = new ThemeSaveAsOwnWebFactory(themeStore);
            var client = await ThemeSaveAsOwnWebFactory.LoggedInClientAsync(factory);

            // When an operator tries to save-as-own onto that SAME slug,
            var response = await client.PostAsync(
                $"/api/themes/{slug}/save-as-own", JsonBody(SaveAsOwnFixtures.ValidRemixManifestJson(slug)));

            // Then it refuses 409, naming the slug and that it holds an imported theme, and the row's
            // own provenance survives byte-for-byte — never NULLed by a write that never happened.
            var detail = await DetailAsync(response);
            Assert.Equal(
                (Status: HttpStatusCode.Conflict, NamesTheSlug: true, NamesImported: true),
                (Status: response.StatusCode,
                 NamesTheSlug: detail.Contains(slug, StringComparison.Ordinal),
                 NamesImported: detail.Contains("imported", StringComparison.OrdinalIgnoreCase)));
            var stillStored = await themeStore.GetBySlugAsync(slug, CancellationToken.None);
            Assert.Equal(seeded, stillStored);
        }

        [Fact]
        public async Task SavingOntoASlugHoldingYourOwnAuthoredThemeUpdatesItAndProvenanceStaysNull()
        {
            // Given a slug already holding an AUTHORED theme — imported_from already null, a PREVIOUS
            // save-as-own onto this same slug, never an import,
            const string slug = "authored-target";
            var themeStore = new FakeThemeStore();
            await themeStore.UpsertAsync(
                slug, ThemeFixtures.ValidManifestJson(slug), importedFrom: null, CancellationToken.None);
            await using var factory = new ThemeSaveAsOwnWebFactory(themeStore);
            var client = await ThemeSaveAsOwnWebFactory.LoggedInClientAsync(factory);

            // When the operator re-saves onto the SAME slug — ordinary iteration on a theme this route
            // itself created, EditorClient.tsx's own "re-save replaces rather than duplicates" contract,
            var response = await client.PostAsync(
                $"/api/themes/{slug}/save-as-own",
                JsonBody(SaveAsOwnFixtures.ValidRemixManifestJson(slug, name: "Updated Remix")));

            // Then it succeeds — authored-over-authored is allowed, never blocked by the SAME
            // fail-closed rule that blocks an imported target — the row is updated, and provenance
            // stays null.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await themeStore.GetBySlugAsync(slug, CancellationToken.None);
            Assert.Equal(
                (Found: true, ImportedFrom: (string?)null, Name: "Updated Remix"),
                (Found: stored is not null, ImportedFrom: stored?.ImportedFrom,
                 Name: NameFromDefinition(stored?.Definition)));
        }

        static string? NameFromDefinition(string? definition)
        {
            if (definition is null) return null;
            using var document = JsonDocument.Parse(definition);
            return document.RootElement.GetProperty("name").GetString();
        }
    }

    static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("detail").GetString() ?? "";
    }
}

// ── Test harness ───────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for this file's own Facts — mirrors
/// Story272_ThemeImport.cs's own <c>ThemeImportWebFactory</c> idiom (<see cref="IThemeStore"/> replaced
/// by a <see cref="FakeThemeStore"/>). <c>Community:CatalogIndexUrl</c> is pinned to a dead loopback
/// port (mirrors Story283/285's own <c>UnreachableCatalogUrl</c> idiom) — a real catalog origin is
/// never needed for this file's own Facts (SPEC F104.10's pack-suggestion enrichment is best-effort,
/// fail-soft prose only; every Fact here proves the BASE refusal, not the enrichment), and pinning it
/// unreachable up front avoids this file's sad-path Facts depending on the real
/// <c>https://raw.githubusercontent.com/…</c> default appsettings.json ships resolving at all.
/// </summary>
file sealed class ThemeSaveAsOwnWebFactory(FakeThemeStore? themeStore = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-story287-saveasown";

    // A loopback port nothing listens on — immediate connection-refused, no DNS lookup, no timeout
    // wait (mirrors Story278/283/285's own UnreachableCatalogUrl idiom).
    const string UnreachableCatalogUrl = "http://127.0.0.1:1/repo/index.json";

    readonly FakeThemeStore store = themeStore ?? new FakeThemeStore();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
        builder.UseSetting("Community:CatalogIndexUrl", UnreachableCatalogUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IThemeStore>();
            services.AddSingleton<IThemeStore>(store);
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

/// <summary>Fixture documents for this file's own Facts — <c>file</c>-scoped, mirroring every other
/// Story2xx spec's "each file needs its own committed copy" idiom (Story285's own fixtures remarks).</summary>
file static class SaveAsOwnFixtures
{
    /// <summary>A colour that appears in no shipped manifest and differs from every sibling Story's
    /// own distinctive token (Story272's <c>DistinctiveLightBg</c> is <c>#2a5c9e</c>) — this file's own
    /// proof that a resolved sheet carries THIS save's tokens, not a coincidence.</summary>
    public const string DistinctiveLightBg = "#3e7a4f";

    /// <summary>A valid remix manifest — real vendored font srcs (PLAN T188, SPEC F103.10), since
    /// every Fact in this file POSTs through the production save-as-own/import routes, both of which
    /// enforce the widened font law. <paramref name="embeddedSlug"/> lets a Fact post a manifest whose
    /// OWN "slug" field differs from the route slug it targets (defaults to <paramref name="slug"/>
    /// itself when omitted) — see <c>ScenarioTheBaseThemeIsUntouched</c>/
    /// <c>ScenarioTheManifestsOwnSlugNeverGoverns</c>'s own remarks for why that distinction
    /// matters.</summary>
    public static string ValidRemixManifestJson(string slug, string? embeddedSlug = null, string name = "My Remix") => $$"""
        {
          "slug": "{{embeddedSlug ?? slug}}",
          "name": "{{name}}",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces-variable-latin.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3-variable-latin.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "{{DistinctiveLightBg}}", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;

    /// <summary>An otherwise-valid manifest whose display font names a src the URL-shape check
    /// (<c>ThemeManifestParser.FontSrcPattern</c>) accepts but <c>FontProvenanceCatalog</c> has no
    /// entry for (mirrors Story272_ThemeImport.cs's own <c>ManifestJsonWithUnvendoredFontSrc</c>) —
    /// this file's own committed copy, so both this file's byte-identical-copy Facts always POST
    /// EXACTLY the same bytes to both routes.</summary>
    public static string ManifestJsonWithUnvendoredFontSrc(string slug) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Law Check",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/nonexistent.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3-variable-latin.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#2a5c9e", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;

    /// <summary>A theme referencing all FIVE real vendored faces — the base pair (Fraunces, Fraunces
    /// italic, Source Sans 3) plus both PLAN T189 additions (JetBrains Mono, Grenze Gotisch) — a REAL
    /// over-ceiling case, no fake fixture provenance record needed (mirrors
    /// Story276_CuratedFontProcess.cs's own <c>ThemeReferencingBasePairPlusBothNewFaces</c>: base pair
    /// 138,272 + both new faces = 237,656 bytes, over the 204,800-byte ceiling).</summary>
    public static string ManifestJsonOverCeiling(string slug) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Over Ceiling",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [
              { "src": "/fonts/fraunces-variable-latin.woff2", "weight": "400 600", "style": "normal" },
              { "src": "/fonts/fraunces-italic-variable-latin.woff2", "weight": "400 600", "style": "italic" },
              { "src": "/fonts/grenze-gotisch-variable-latin.woff2", "weight": "400", "style": "normal" }
            ] },
            "sans": { "family": "Source Sans 3", "assets": [
              { "src": "/fonts/source-sans-3-variable-latin.woff2", "weight": "400", "style": "normal" },
              { "src": "/fonts/jetbrains-mono-variable-latin.woff2", "weight": "400", "style": "normal" }
            ] }
          },
          "modes": {
            "light": { "bg": "#2a5c9e", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;
}

/// <summary>One row of <see cref="FeatureSaveAsOwn.ScenarioTheSameGateRefusesBothRoutesIdentically"/>'s
/// own table — PLAN T207 review finding F1. <see cref="ToString"/> is what xUnit's test explorer shows
/// per row, so it names the gate under test, not the row's own field values.</summary>
public sealed record BadBodyRow(string Label, string Slug, string Body, HttpStatusCode ExpectedStatus, string ExpectedFragment)
{
    public override string ToString() => Label;
}

/// <summary>The seven bad-body rows <see cref="FeatureSaveAsOwn.ScenarioTheSameGateRefusesBothRoutesIdentically"/>
/// drives through both <c>station.theme</c> write routes — every gate <see cref="ThemeWriteGate.RunAsync"/>
/// enforces, in gate order (PLAN T207 review finding F1). Each row's <see cref="BadBodyRow.ExpectedFragment"/>
/// is content the refusal MUST name, never a substring the base refusal's own boilerplate alone would
/// already satisfy (N2's "NamesTheMissingFace pattern", generalized to every row).</summary>
static class BadBodyTable
{
    public static readonly IReadOnlyList<BadBodyRow> Rows =
    [
        new("bad slug",
            Slug: "Bad_Slug",
            Body: SaveAsOwnFixtures.ValidRemixManifestJson("Bad_Slug"),
            ExpectedStatus: HttpStatusCode.BadRequest,
            ExpectedFragment: "Bad_Slug"),

        new("oversized",
            Slug: "oversized-theme",
            // Exceeds BoundedImportBodyReader.MaxImportBytes (256 KB) — not valid JSON at all, which is
            // fine: the size cap is checked before any JSON parse is ever attempted.
            Body: new string('a', 300 * 1024),
            ExpectedStatus: HttpStatusCode.RequestEntityTooLarge,
            ExpectedFragment: "KB"),

        new("newer schema-major",
            Slug: "newer-schema-theme",
            Body: """{"schemaVersion": 99, "slug": "newer-schema-theme", "name": "Newer Schema", "author": "GenWave"}""",
            ExpectedStatus: HttpStatusCode.BadRequest,
            ExpectedFragment: "99"),

        new("malformed JSON",
            Slug: "malformed-json-theme",
            Body: "{ this is not valid json",
            ExpectedStatus: HttpStatusCode.BadRequest,
            // Names the offending manifest's own route slug — ThemeWriteGate.ReadParseAndValidateAsync
            // builds BOTH routes' ThemeManifestSource from the bare route slug itself, no per-route
            // prefix (that type's own "Route-neutral ThemeManifestSource name" remarks — PLAN T207
            // review copy-nit fix), so this is the one row whose byte-identity would have broken had
            // that source name still varied by route ("import:…" vs "save-as-own:…").
            ExpectedFragment: "malformed-json-theme"),

        new("unvendored face",
            Slug: "law-check-theme",
            Body: SaveAsOwnFixtures.ManifestJsonWithUnvendoredFontSrc("law-check-theme"),
            ExpectedStatus: HttpStatusCode.BadRequest,
            ExpectedFragment: "/fonts/nonexistent.woff2"),

        new("over ceiling",
            Slug: "over-ceiling-theme",
            Body: SaveAsOwnFixtures.ManifestJsonOverCeiling("over-ceiling-theme"),
            ExpectedStatus: HttpStatusCode.BadRequest,
            ExpectedFragment: "237656"),

        new("shipped slug",
            Slug: ThemeCatalog.ShippedDefaultSlug,
            Body: SaveAsOwnFixtures.ValidRemixManifestJson(ThemeCatalog.ShippedDefaultSlug),
            ExpectedStatus: HttpStatusCode.Conflict,
            ExpectedFragment: ThemeCatalog.ShippedDefaultSlug),
    ];
}
