// STORY-272 — Importing a theme (SPEC F103.6)
//
// BDD specification — xUnit. POST /api/themes/{slug}/import reuses the F79 persona-import shell:
// AdminSurface + Settings auth, a size-capped bounded body read, deserialization-as-validation via
// ThemeManifestParser, a schema-major reject naming both versions, ?catalogSlug/'file'/null
// provenance, and a transactional, no-partial upsert into station.theme.
//
// WIRED T184 — every Fact below drives the real production route through
// WebApplicationFactory<Program> (real routing/auth/content-negotiation pipeline; IThemeStore/
// IStationSettingsStore replaced by scriptable fakes, no live Postgres — mirrors Story209's own
// PersonaImportWebFactory idiom). One assertion focus per Fact; the sad path (400/409/413) is its own
// block, mirroring Story209's SadPathFailClosedValidation.
//
// ScenarioTheImportedThemeResolves and ScenarioTheAllowlistWidensAfterImport prove the two halves of
// SPEC F103.7's "no restart" contract this task's own acceptance sentence names: the imported theme
// resolves via GET /api/theme.css (the cookie precedence — no DB needed), and Station:Theme's
// allowlist (StationSettingsAllowlist/SettingValidator, PLAN T183's own widening) accepts the newly
// imported slug — the carry-forward the T183 review asked this task to close: no committed spec
// previously distinguished the DI'd runtime ThemeCatalog from the shipped-only fallback.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Api;
using GenWave.Host.Configuration;
using GenWave.Host.Theming;
using GenWave.Host.Tests.Fakes;

namespace GenWave.Host.Tests.Specs;

// ── In-process fakes ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Scriptable <see cref="IStationSettingsStore"/> double — records every <see cref="WriteAsync"/> call
/// and echoes them back from <see cref="ReadAllAsync"/>, unlike Story265's own throw-on-write
/// <c>FakeSettingsStore</c> variants (those scenarios only ever read). This one lets
/// <see cref="FeatureThemeImport.ScenarioTheAllowlistWidensAfterImport"/> drive a genuine
/// <c>PUT /api/settings</c> round trip without a live Postgres-backed overlay: the PUT response's
/// 200-vs-400 signal comes entirely from <see cref="SettingValidator"/> (which is what this scenario
/// is actually proving) plus this store accepting the write, never from
/// <c>StationSettingsConfigurationProvider</c>'s own separate, real-connection-only read path.
/// </summary>
file sealed class FakeThemeSettingsStore : IStationSettingsStore
{
    readonly Dictionary<string, string> overrides = new(StringComparer.OrdinalIgnoreCase);

    public Task WriteAsync(string key, object value, CancellationToken cancellationToken = default)
    {
        overrides[key] = value.ToString() ?? string.Empty;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(overrides);
}

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that brings up the real HTTP pipeline (routing,
/// auth, the production <c>POST /api/themes/{slug}/import</c> route) — mirrors Story209's own
/// <c>PersonaImportWebFactory</c>. <see cref="IThemeStore"/>/<see cref="IStationSettingsStore"/> are
/// replaced with scriptable fakes; <see cref="ThemeCatalog"/> is DELIBERATELY left as Program.cs's own
/// real <c>CreateForStation</c> registration — it resolves <see cref="IThemeStore"/> lazily, so it
/// picks up whichever fake this factory installed with no further wiring, which is exactly what proves
/// the controller's own <c>ReloadOwnerThemesAsync</c> call reaches the SAME singleton every other
/// request handler (theme.css, settings) reads.
/// </summary>
file sealed class ThemeImportWebFactory(
    FakeThemeStore? themeStore = null, FakeThemeSettingsStore? settingsStore = null) : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    // Private — no Fact reads this back through the factory; every scenario that needs to inspect the
    // store keeps its own `themeStore` local and passes it in above (PLAN T184 review F6b).
    readonly FakeThemeStore store = themeStore ?? new FakeThemeStore();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<IThemeStore>();
            services.AddSingleton<IThemeStore>(store);

            services.RemoveAll<IStationSettingsStore>();
            services.AddSingleton<IStationSettingsStore>(settingsStore ?? new FakeThemeSettingsStore());
        });
    }
}

// ── Fixture ────────────────────────────────────────────────────────────────────────────────────────

file static class ThemeImportFixture
{
    /// <summary>A colour that appears in no shipped manifest (verified against every
    /// <c>Theming/themes/*.json</c> file at authoring time) — mirrors Story265's own
    /// <c>ThemeSelectionFixtures.AlternateLightBg</c> reasoning: a Fact asserting on this proves the
    /// composed sheet carries THIS import's own tokens, not a shipped default that happens to
    /// coincide (the real <c>cats-whisker.json</c> uses the SAME light <c>--bg</c> as
    /// Story263/Story271's own shared <c>ThemeFixtures.ValidManifestJson</c>, which is why this file
    /// does not reuse that helper).</summary>
    public const string DistinctiveLightBg = "#2a5c9e";

    public static string ValidManifestJson(
        string slug, string name = "Test Theme", string lightBg = DistinctiveLightBg,
        int? schemaVersion = null, string? schemaVersionRaw = null)
    {
        // schemaVersionRaw takes precedence — it inserts a raw JSON literal verbatim (a quoted string,
        // a fraction, an overflowing integer) so the F2 "present but unreadable" specs can exercise a
        // shape schemaVersion's own int? parameter can't express, while every other field stays a
        // fully valid manifest — isolating "unreadable schemaVersion" from "also structurally broken".
        var schemaVersionField = schemaVersionRaw is { } raw
            ? $"\"schemaVersion\": {raw},"
            : schemaVersion is { } version ? $"\"schemaVersion\": {version}," : "";
        // The font srcs below are the REAL vendored filenames (PLAN T188, SPEC F103.10) — unlike
        // Story263/Story271's own shared ThemeFixtures.ValidManifestJson (which drives
        // ThemeCatalog.Load directly, never the real import route), every Fact in this file POSTs
        // through the production POST /api/themes/{slug}/import route, and ThemesImportController
        // now rejects a manifest referencing a font outside FontProvenanceCatalog's vendored set.
        return $$"""
            {
              {{schemaVersionField}}
              "slug": "{{slug}}",
              "name": "{{name}}",
              "author": "GenWave",
              "fonts": {
                "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces-variable-latin.woff2", "weight": "400 600", "style": "normal" } ] },
                "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3-variable-latin.woff2", "weight": "400", "style": "normal" } ] }
              },
              "modes": {
                "light": { "bg": "{{lightBg}}", "ink": "#2b2320" },
                "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
              }
            }
            """;
    }

    /// <summary>An otherwise-valid manifest (PLAN T188, SPEC F103.10) whose display font names a
    /// src the URL-shape check (<c>ThemeManifestParser.FontSrcPattern</c>) accepts but
    /// <c>FontProvenanceCatalog</c> has no entry for — proves the EXISTENCE check
    /// <see cref="ThemeFontProvenanceValidator"/> adds, distinct from the shape check.</summary>
    public static string ManifestJsonWithUnvendoredFontSrc(string slug) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Test Theme",
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
}

// ── Specs ──────────────────────────────────────────────────────────────────────────────────────────

public static class FeatureThemeImport
{
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ThemeImportWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    static Task<HttpResponseMessage> PostManifestAsync(
        HttpClient client, string slug, string json, string? catalogSlug = null)
    {
        var uri = catalogSlug is null ? $"/api/themes/{slug}/import" : $"/api/themes/{slug}/import?catalogSlug={catalogSlug}";
        return client.PostAsync(uri, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioACatalogThemeImports
    {
        [Fact]
        public async Task ItRespondsSuccessAndStoresWithCatalogProvenance()
        {
            // Given a valid theme and a catalogSlug,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When POST /api/themes/{slug}/import is called (the real endpoint),
            var response = await PostManifestAsync(
                client, "midnight-drive", ThemeImportFixture.ValidManifestJson("midnight-drive"),
                catalogSlug: "midnight-drive-catalog-entry");

            // Then it responds success, the theme is stored, and imported_from is the catalog slug (AC1).
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await themeStore.GetBySlugAsync("midnight-drive", CancellationToken.None);
            Assert.Equal("midnight-drive-catalog-entry", stored?.ImportedFrom);
        }
    }

    public sealed class ScenarioAFileUploadImports
    {
        [Fact]
        public async Task ItIsStoredWithFileProvenance()
        {
            // Given a valid theme manifest uploaded directly (no catalogSlug),
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When the import runs,
            var response = await PostManifestAsync(client, "aurora-glow", ThemeImportFixture.ValidManifestJson("aurora-glow"));
            var body = await response.Content.ReadFromJsonAsync<ThemeImportResponse>();

            // Then it is stored with imported_from "file" (AC2) — checked on both the response and
            // the stored row.
            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal("file", body?.ImportedFrom);
            var stored = await themeStore.GetBySlugAsync("aurora-glow", CancellationToken.None);
            Assert.Equal("file", stored?.ImportedFrom);
        }
    }

    public sealed class ScenarioReimportingAnOwnerSlugUpserts
    {
        [Fact]
        public async Task TheSecondImportReplacesTheFirstWithNoConflict()
        {
            // Given a slug already imported once,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            var first = await PostManifestAsync(
                client, "midnight-drive", ThemeImportFixture.ValidManifestJson("midnight-drive", name: "First Cut"),
                catalogSlug: "entry-a");
            Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());

            // When it is imported again with different content and a different provenance,
            var second = await PostManifestAsync(
                client, "midnight-drive", ThemeImportFixture.ValidManifestJson("midnight-drive", name: "Second Cut"),
                catalogSlug: "entry-b");

            // Then the second import succeeds (never 409 — that status is reserved for a shipped-slug
            // collision, not a re-import) and replaces the stored row.
            Assert.True(second.IsSuccessStatusCode, await second.Content.ReadAsStringAsync());
            Assert.Single(await themeStore.GetAllAsync(CancellationToken.None));
            var stored = await themeStore.GetBySlugAsync("midnight-drive", CancellationToken.None);
            Assert.Equal(("Second Cut", "entry-b"), (ExtractName(stored!.Definition), stored.ImportedFrom));
        }

        static string ExtractName(string definitionJson) =>
            System.Text.Json.JsonDocument.Parse(definitionJson).RootElement.GetProperty("name").GetString()!;
    }

    public sealed class ScenarioTheRouteSlugGovernsStorage
    {
        [Fact]
        public async Task TheManifestsOwnEmbeddedSlugIsOverriddenByTheRouteSlug()
        {
            // Given a manifest whose own embedded slug differs from the route it is POSTed to,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            var response = await PostManifestAsync(
                client, "route-slug", ThemeImportFixture.ValidManifestJson("embedded-slug"));
            var body = await response.Content.ReadFromJsonAsync<ThemeImportResponse>();

            // Then the route slug wins for both the store's key AND the stored definition's own
            // slug field (the split-identity bug this normalization exists to make impossible) —
            // ThemeCatalog re-parses the stored definition, so a drift here would silently misfile
            // the theme under the manifest's own opinion instead.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal("route-slug", body?.Slug);
            var stored = await themeStore.GetBySlugAsync("route-slug", CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Contains("\"slug\":\"route-slug\"", stored!.Definition, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheImportedThemeResolves
    {
        [Fact]
        public async Task ItServesViaApiThemeCssThroughTheVisitorCookie()
        {
            // Given a freshly imported theme,
            await using var factory = new ThemeImportWebFactory();
            var client = await LoggedInClientAsync(factory);
            var response = await PostManifestAsync(client, "aurora-glow", ThemeImportFixture.ValidManifestJson("aurora-glow"));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

            // When GET /api/theme.css is called selecting it via the visitor cookie (no live DB
            // needed for cookie precedence — Station:Theme's own live-PUT round trip is a
            // real-Postgres-only claim, Story265's own documented split) — a fresh, cookie-handling
            // -disabled client so the manual Cookie header below is the only one on the wire,
            var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var cssRequest = new HttpRequestMessage(HttpMethod.Get, "/api/theme.css");
            cssRequest.Headers.Add("Cookie", $"{ThemeCatalog.CookieName}=aurora-glow");
            var cssResponse = await anonymousClient.SendAsync(cssRequest);
            var css = await cssResponse.Content.ReadAsStringAsync();

            // Then it resolves to the imported theme with no api restart (SPEC F103.7) — proven by
            // the imported manifest's own distinctive token, not merely a 200.
            Assert.Equal(HttpStatusCode.OK, cssResponse.StatusCode);
            Assert.Contains($"--bg: {ThemeImportFixture.DistinctiveLightBg};", css, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheAllowlistWidensAfterImport
    {
        [Fact]
        public async Task TheImportedSlugIsListedAndAcceptedAsStationTheme()
        {
            // Given a freshly imported theme,
            var settingsStore = new FakeThemeSettingsStore();
            await using var factory = new ThemeImportWebFactory(settingsStore: settingsStore);
            var client = await LoggedInClientAsync(factory);
            var importResponse = await PostManifestAsync(client, "aurora-glow", ThemeImportFixture.ValidManifestJson("aurora-glow"));
            Assert.True(importResponse.IsSuccessStatusCode, await importResponse.Content.ReadAsStringAsync());

            // When Station:Theme's settings surface is read and then written with the imported slug —
            // the SAME StationSettingsAllowlist.ThemeChoices/SettingValidator seam GET/PUT
            // /api/settings both call, sourced from the DI-registered ThemeCatalog singleton (PLAN
            // T183) the import route just rebuilt (PLAN T184) — never the shipped-only fallback a
            // stale/unwired DI registration would silently fall back to,
            var getResponse = await client.GetAsync("/api/settings");
            var settings = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<SettingDto>>();
            var themeSetting = settings!.Single(s => s.Key.Equals("Station:Theme", StringComparison.OrdinalIgnoreCase));

            var putResponse = await client.PutAsJsonAsync(
                "/api/settings", new[] { new SettingUpdateRequest("Station:Theme", "aurora-glow") });

            // Then the imported slug is both a listed choice AND accepted (not 400-rejected) — the
            // closed choice widened from shipped-only to include it, with no api restart (AC3 in
            // Story271's own numbering; the T183 review's carry-forward for this task).
            Assert.Contains(themeSetting.Choices!, choice => choice.Value == "aurora-glow");
            Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        }
    }

    public sealed class ScenarioTheResponseCarriesImportedAt
    {
        [Fact]
        public async Task TheImportResponseImportedAtMatchesTheStoredRow()
        {
            // Given a freshly imported catalog theme (gh-#375: the admin UI's catalog
            // detail panel flips to "Installed" straight off THIS response, no second fetch — see
            // ThemeImportResponse's own remarks — so it needs a real, store-sourced imported_at,
            // never a client-side DateTime.UtcNow approximation),
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is imported,
            var response = await PostManifestAsync(
                client, "midnight-drive", ThemeImportFixture.ValidManifestJson("midnight-drive"),
                catalogSlug: "midnight-drive-catalog-entry");
            var body = await response.Content.ReadFromJsonAsync<ThemeImportResponse>();

            // Then the response's own ImportedAt is the SAME value the store actually persisted —
            // a read-back, not a guess.
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var stored = await themeStore.GetBySlugAsync("midnight-drive", CancellationToken.None);
            Assert.Equal(stored?.ImportedAt, body?.ImportedAt);
            Assert.NotEqual(default, body?.ImportedAt);
        }

        [Fact]
        public async Task TheSettingsSurfaceReportsTheSameImportedAtForTheChoice()
        {
            // Given a freshly imported catalog theme,
            var settingsStore = new FakeThemeSettingsStore();
            await using var factory = new ThemeImportWebFactory(settingsStore: settingsStore);
            var client = await LoggedInClientAsync(factory);
            var importResponse = await PostManifestAsync(
                client, "aurora-glow", ThemeImportFixture.ValidManifestJson("aurora-glow"),
                catalogSlug: "aurora-glow-catalog-entry");
            var importBody = await importResponse.Content.ReadFromJsonAsync<ThemeImportResponse>();
            Assert.True(importResponse.IsSuccessStatusCode, await importResponse.Content.ReadAsStringAsync());

            // When Station:Theme's own choices are read back — the SAME seam gh-#375's own admin-ui
            // catalog page reads to derive its installed-provenance list, no new backend route,
            var getResponse = await client.GetAsync("/api/settings");
            var settings = await getResponse.Content.ReadFromJsonAsync<IReadOnlyList<SettingDto>>();
            var themeSetting = settings!.Single(s => s.Key.Equals("Station:Theme", StringComparison.OrdinalIgnoreCase));
            var choice = themeSetting.Choices!.Single(c => c.Value == "aurora-glow");

            // Then the choice's own importedFrom/importedAt agree with the import response — the
            // two reads can never silently disagree about when this theme was imported.
            Assert.Equal("aurora-glow-catalog-entry", choice.ImportedFrom);
            Assert.Equal(importBody?.ImportedAt, choice.ImportedAt);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioRejectingBadImports
    {
        [Fact]
        public async Task AnOversizeBodyIsRefusedWith413()
        {
            // Given an import body over the size cap,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(client, "too-big", new string('a', 300 * 1024));

            // Then it responds 413 and nothing is stored (AC4).
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task AnInvalidManifestIsRefusedWith400()
        {
            // Given a body that does not deserialize to a ThemeManifest,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(client, "not-json", "{ this is not valid json");

            // Then it responds 400 and nothing is stored (AC5) — deserialization-as-validation.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task AManifestReferencingAnUnvendoredFontIsRefusedWith400()
        {
            // Given a manifest whose font src has the right SHAPE (ThemeManifestParser.FontSrcPattern
            // accepts it) but names a face GenWave never vendored (SPEC F103.10, PLAN T188),
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(
                client, "off-catalog-font", ThemeImportFixture.ManifestJsonWithUnvendoredFontSrc("off-catalog-font"));
            var body = await response.Content.ReadAsStringAsync();

            // Then it responds 400 naming the missing face and the vendored set, and nothing is
            // stored — the parser's own shape check alone would have let this through.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("/fonts/nonexistent.woff2", body, StringComparison.Ordinal);
            Assert.Contains("/fonts/fraunces-variable-latin.woff2", body, StringComparison.Ordinal);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ANewerMajorManifestIsRefusedNamingBothVersions()
        {
            // Given a manifest whose schema major exceeds the app's,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(
                client, "dj-future", ThemeImportFixture.ValidManifestJson("dj-future", schemaVersion: 2));
            var body = await response.Content.ReadAsStringAsync();

            // Then it responds 400 naming both versions (AC6) — the exact phrase, not merely a "2"
            // appearing somewhere in the body (PLAN T184 review F6b: the prior assertion was vacuous).
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "schema version 2 is newer than this station's supported version 1", body, StringComparison.Ordinal);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ANewerMajorManifestIsRefusedNamingBothVersionsEvenWhenAlsoStructurallyInvalid()
        {
            // Given a manifest whose schema major exceeds the app's AND whose shape is also missing
            // every field ThemeManifestParser requires (name, author, fonts, modes) — a newer major is
            // free to look nothing like today's v1 shape,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);
            const string json = """{ "schemaVersion": 2, "slug": "dj-both-broken" }""";

            // When it is posted,
            var response = await PostManifestAsync(client, "dj-both-broken", json);
            var body = await response.Content.ReadAsStringAsync();

            // Then it responds 400 naming the version mismatch — never a misleading structural-parse
            // complaint (e.g. "is missing a name") — because the schema-version gate runs before
            // ThemeManifestParser ever sees the body (PLAN T184 review F5).
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "schema version 2 is newer than this station's supported version 1", body, StringComparison.Ordinal);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Theory]
        [InlineData("\"2\"")]           // string-typed
        [InlineData("2.5")]             // non-integer
        [InlineData("99999999999")]     // overflows Int32
        public async Task AnUnreadableSchemaVersionIsRefusedWith400(string schemaVersionRaw)
        {
            // Given an otherwise-valid manifest whose schemaVersion is present but not a readable
            // whole number (PLAN T184 review F2: this used to fail OPEN, silently treated as absent),
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(
                client, "dj-unreadable-version",
                ThemeImportFixture.ValidManifestJson("dj-unreadable-version", schemaVersionRaw: schemaVersionRaw));

            // Then it responds 400 (refused, not silently treated as version 1) and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ABadRouteSlugIsRefusedWith400()
        {
            // Given a route slug outside the lowercase/digit/single-hyphen shape,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(client, "Bad_Slug", ThemeImportFixture.ValidManifestJson("Bad_Slug"));

            // Then it responds 400 and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ABadCatalogSlugIsRefusedWith400()
        {
            // Given a catalogSlug outside the same slug shape,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(
                client, "midnight-drive", ThemeImportFixture.ValidManifestJson("midnight-drive"), catalogSlug: "Not_Valid");

            // Then it responds 400 and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task AnOversizeCatalogSlugIsRefusedWith400()
        {
            // Given a catalogSlug longer than a real catalog entry slug could ever be,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var overlong = new string('a', 65);
            var response = await PostManifestAsync(
                client, "midnight-drive", ThemeImportFixture.ValidManifestJson("midnight-drive"), catalogSlug: overlong);

            // Then it responds 400 and nothing is stored.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }

        [Fact]
        public async Task AShippedSlugCollisionIsRefusedWith409()
        {
            // Given a route slug matching a real shipped theme's own slug,
            var themeStore = new FakeThemeStore();
            await using var factory = new ThemeImportWebFactory(themeStore);
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostManifestAsync(
                client, ThemeCatalog.ShippedDefaultSlug, ThemeImportFixture.ValidManifestJson(ThemeCatalog.ShippedDefaultSlug));

            // Then it responds 409 and nothing is stored (SPEC F103.8) — the offline-fallback default
            // cannot be shadowed by an import.
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Empty(await themeStore.GetAllAsync(CancellationToken.None));
        }
    }
}
