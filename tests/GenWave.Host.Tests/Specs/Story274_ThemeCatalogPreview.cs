// STORY-274 — Previewing and installing a theme (SPEC F103.5, F103.6)
//
// BDD specification — xUnit. POST /api/themes/preview composes a POSTed theme manifest into CSS
// scoped under ThemePreviewController.ContainerSelector — never :root — through the SAME
// ThemeCssComposer the live GET /spectator/theme.css and GET /api/theme.css routes call (just the
// scoped overload, ComposeScoped, added alongside the unchanged Compose). Nothing here is stored:
// the manifest is the same hash-verified bytes the Admin UI already fetched via
// GET /api/catalog/entries/{slug} (SPEC F90.3) and is about to review before POSTing to
// POST /api/themes/{slug}/import (Story272_ThemeImport.cs) — this route never re-fetches the
// catalog itself.
//
// WIRED T186 — every Fact below drives the real production route through
// WebApplicationFactory<Program>, mirroring Story264's own ThemeCssWebFactory posture (a
// Postgres-shaped connection string this route never actually dials, since
// ThemePreviewController touches neither ThemeCatalog nor IThemeStore).
//
// ScenarioTheScopedOutputMatchesTheLivePathsOwnTokens drives ThemeCssComposer.Compose/ComposeScoped
// directly (no HTTP) — the DoD's "scoped output vs live output: same tokens, different selector;
// live path byte-identical to before" — reusing Story264_ComposedStylesheet.cs's own
// (internal-by-default, so cross-file-visible) ComposerFixtures rather than a second copy.
//
// GATE PARITY (Dean's directive 2026-08-05, "preview refuses what import refuses") —
// ScenarioRejectingBadPreviewRequests now also covers the schema-major and curated-font-provenance
// gates ThemesImportController.Import already enforced (SPEC F103.6 AC6, F103.10/PLAN T188): an
// operator must never be sold a live preview of a theme its own import route would go on to reject.
// Both gates run here in the SAME order import documents, through the SAME shared
// ThemeSchemaVersionGate/ImportProblems/ThemeFontProvenanceValidator types — see
// ThemePreviewController's own remarks for the full reasoning and PreviewGateFixtures below for the
// fixture manifests (a local, file-scoped mirror of Story272_ThemeImport.cs's own ThemeImportFixture,
// never shared cross-file — the house idiom every other spec-fixture type in this suite already
// follows).

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using GenWave.Host.Api;
using GenWave.Host.Theming;

namespace GenWave.Host.Tests.Specs;

// ── WebApplicationFactory ─────────────────────────────────────────────────────────────────────────

/// <summary>Minimal factory (mirrors Story264's own <c>ThemeCssWebFactory</c>): a Postgres-shaped
/// connection string that boot needs to be present but this route never dials, since
/// <see cref="ThemePreviewController"/> depends on neither <c>ThemeCatalog</c> nor
/// <c>IThemeStore</c> — it is a pure manifest-in, CSS-out transform.</summary>
file sealed class ThemePreviewWebFactory : WebApplicationFactory<Program>
{
    internal const string Password = "test-password-x7z";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", Password);
    }
}

// ── Fixture ────────────────────────────────────────────────────────────────────────────────────────

/// <summary>Manifests for the gate-parity sad-path specs (Dean's directive 2026-08-05) — a local,
/// file-scoped mirror of <c>Story272_ThemeImport.cs</c>'s own <c>ThemeImportFixture</c>, never shared
/// cross-file (the house idiom every <c>file</c>-scoped fixture in this suite already follows;
/// <see cref="ComposerFixtures"/> is the one exception, deliberately NOT <c>file</c>-scoped because
/// Story264/Story274 both need it).</summary>
file static class PreviewGateFixtures
{
    /// <summary>An otherwise fully valid manifest (real vendored font srcs, both mode blocks) carrying
    /// a top-level <c>schemaVersion</c> — <paramref name="schemaVersionLiteral"/> is inserted VERBATIM
    /// as a raw JSON literal so callers can exercise both a readable integer (e.g. <c>"2"</c>) and an
    /// unreadable shape (a quoted string, a fraction, an overflowing integer) with the one
    /// method — mirrors <c>ThemeImportFixture.ValidManifestJson</c>'s own
    /// <c>schemaVersion</c>/<c>schemaVersionRaw</c> split, collapsed to one parameter since preview's
    /// specs need no schema-absent case here (the happy-path scenarios above already cover that).</summary>
    public static string ValidManifestJsonWithSchemaVersion(string slug, string schemaVersionLiteral) => $$"""
        {
          "schemaVersion": {{schemaVersionLiteral}},
          "slug": "{{slug}}",
          "name": "Test Theme",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces-variable-latin.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3-variable-latin.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#2a5c9e", "ink": "#2b2320" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;

    /// <summary>An otherwise-valid manifest whose display font names a src the URL-shape check
    /// (<c>ThemeManifestParser.FontSrcPattern</c>) accepts but <c>FontProvenanceCatalog</c> has no
    /// entry for — verbatim mirror of <c>ThemeImportFixture.ManifestJsonWithUnvendoredFontSrc</c>
    /// (SPEC F103.10, PLAN T188).</summary>
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

public static class FeatureThemeCatalogPreview
{
    static async Task<HttpClient> LoggedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ThemePreviewWebFactory.Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    static Task<HttpResponseMessage> PostPreviewAsync(HttpClient client, string json) =>
        client.PostAsync("/api/themes/preview", new StringContent(json, Encoding.UTF8, "application/json"));

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioTheDetailPreviewComposesScoped
    {
        readonly string manifestJson = ComposerFixtures.ManifestJson(
            "preview-theme",
            displayFamily: "Fraunces", displaySrc: "/fonts/fraunces-variable-latin.woff2",
            sansFamily: "Source Sans 3", sansSrc: "/fonts/source-sans-3-variable-latin.woff2",
            lightBg: "#2a5c9e", darkBg: "#0d1f3c");

        [Fact]
        public async Task RespondsOkWithCssContentType()
        {
            // Given/When a valid theme manifest is posted for preview,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);
            var response = await PostPreviewAsync(client, manifestJson);

            // Then it responds 200 as text/css (AC1).
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task ScopesTheLightBlockUnderTheContainerSelectorInsteadOfRoot()
        {
            // Given/When,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);
            var css = await (await PostPreviewAsync(client, manifestJson)).Content.ReadAsStringAsync();

            // Then the light block carries the manifest's own token, scoped under the preview
            // container selector (AC1, SPEC F103.5) — never :root.
            var block = ComposerFixtures.ExtractBlockBody(css, ThemePreviewController.ContainerSelector);
            Assert.Contains("--bg: #2a5c9e;", block, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ScopesTheExplicitDarkBlockUnderTheContainerSelector()
        {
            // Given/When,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);
            var css = await (await PostPreviewAsync(client, manifestJson)).Content.ReadAsStringAsync();

            // Then the explicit-dark block is scoped the same way (AC1) — mode authority (the
            // data-theme attribute) is checked as an ANCESTOR of the container, not compounded
            // onto it (F1 fix, ComposeScoped's own remarks: "mode authority stays at the root"),
            // because the browser only ever stamps data-theme on :root, never on this container.
            var block = ComposerFixtures.ExtractBlockBody(
                css, $"[data-theme=\"dark\"] {ThemePreviewController.ContainerSelector}");
            Assert.Contains("--bg: #0d1f3c;", block, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ScopesTheSystemDarkMediaBlockUnderTheContainerSelector()
        {
            // Given/When,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);
            var css = await (await PostPreviewAsync(client, manifestJson)).Content.ReadAsStringAsync();

            // Then the prefers-color-scheme block is scoped the same way (AC1) — an OS-dark
            // reviewer previewing with no explicit override still sees the dark mock. The
            // "nobody has chosen" guard is checked against the ROOT explicitly (F1 fix), never a
            // bare `:not([data-theme])` glued onto the container — the container never carries
            // that attribute either way, so a container-only guard would be a tautology that
            // always matches regardless of the real root/OS state.
            var block = ComposerFixtures.ExtractBlockBody(
                css, $":root:not([data-theme]) {ThemePreviewController.ContainerSelector}");
            Assert.Contains("--bg: #0d1f3c;", block, StringComparison.Ordinal);
        }

        [Fact]
        public async Task NeverEmitsASelectorWhoseSubjectIsRoot()
        {
            // Given/When,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);
            var css = await (await PostPreviewAsync(client, manifestJson)).Content.ReadAsStringAsync();

            // Then nothing in the preview response can ever APPLY TO :root — the whole point of a
            // SCOPED preview container (SPEC F103.5's "not :root"). Re-expressed (F1 fix, review
            // finding) from a bare `Assert.DoesNotContain(":root", css)`: the OS-default block now
            // legitimately carries ":root" as an ANCESTOR QUALIFIER
            // (":root:not([data-theme]) .theme-live-preview", see ComposeScoped's own remarks) —
            // a plain substring check would now false-positive against that same fix. What must
            // still never happen is ":root" appearing as a selector's SUBJECT — its rightmost
            // compound, the part CSS actually paints — so this checks that ":root" (optionally
            // qualified by an attribute or :not()) is never immediately followed by the opening
            // brace with nothing else in between; the ancestor form always has the container's
            // own selector text between the qualifier and the brace, so it never matches this.
            var rootAsSubject = new Regex(@":root(\[[^\]]*\])?(:not\([^)]*\))?\s*\{");
            Assert.DoesNotMatch(rootAsSubject, css);
        }
    }

    /// <summary>Resolution-matrix pin (F1 fix, review finding): a real browser CSS cascade can't be
    /// resolved inside xUnit (no rendering engine here), so this scenario pins the SELECTOR
    /// STRUCTURE the fix depends on instead — the ancestor form with a combinator SPACE, never a
    /// compound attribute/pseudo glued directly onto the container — across both selector-bearing
    /// modes SPEC F103.5 must resolve correctly (an explicit choice, and the OS-default absence of
    /// one). The equivalent TRUE browser-resolution assert lives client-side in
    /// theme-catalog-preview-install.spec.tsx ("the scoped preview resolves the correct mode
    /// without leaking to :root"): jsdom actually runs a cascade against a real DOM that
    /// <c>getComputedStyle</c> can read, which a string-structure pin here cannot prove on its
    /// own — see that file's own remarks for why explicit-dark/light are the two cases jsdom can
    /// prove and the OS-default case cannot (jsdom never implements
    /// <c>prefers-color-scheme</c> media evaluation).</summary>
    public sealed class ScenarioTheModeSelectorsPinTheAncestorFormAcrossExplicitAndOsCombinations
    {
        readonly string manifestJson = ComposerFixtures.ManifestJson(
            "matrix-theme",
            displayFamily: "Fraunces", displaySrc: "/fonts/fraunces-variable-latin.woff2",
            sansFamily: "Source Sans 3", sansSrc: "/fonts/source-sans-3-variable-latin.woff2",
            lightBg: "#2a5c9e", darkBg: "#0d1f3c");

        [Fact]
        public async Task TheExplicitDarkQualifierIsAnAncestorSeparatedByASpaceNeverACompound()
        {
            // Given/When,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);
            var css = await (await PostPreviewAsync(client, manifestJson)).Content.ReadAsStringAsync();

            // Then the fixed ancestor form is present…
            Assert.Contains(
                $"[data-theme=\"dark\"] {ThemePreviewController.ContainerSelector} {{",
                css, StringComparison.Ordinal);
            // …and the F1 bug's compound form — the attribute glued directly onto the container,
            // dead because the browser never stamps data-theme there — is not.
            Assert.DoesNotContain(
                $"{ThemePreviewController.ContainerSelector}[data-theme=\"dark\"]",
                css, StringComparison.Ordinal);
        }

        [Fact]
        public async Task TheSystemDarkQualifierChecksRootExplicitlyNeverTheContainerAlone()
        {
            // Given/When,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);
            var css = await (await PostPreviewAsync(client, manifestJson)).Content.ReadAsStringAsync();

            // Then the fixed ancestor form is present…
            Assert.Contains(
                $":root:not([data-theme]) {ThemePreviewController.ContainerSelector} {{",
                css, StringComparison.Ordinal);
            // …and the F1 bug's container-only form — a tautology since the container never
            // carries data-theme either way, so it always matched regardless of the real
            // explicit/OS state — is not.
            Assert.DoesNotContain(
                $"{ThemePreviewController.ContainerSelector}:not([data-theme])",
                css, StringComparison.Ordinal);
        }
    }

    /// <summary>Drives <see cref="ThemeCssComposer"/> directly — no HTTP — for the DoD's own
    /// "scoped output vs live output: same tokens, different selector; live path byte-identical to
    /// before" seam.</summary>
    public sealed class ScenarioTheScopedOutputMatchesTheLivePathsOwnTokens
    {
        readonly ThemeManifest theme = ComposerFixtures.LoadSingle(
            "parity-theme",
            ComposerFixtures.ManifestJson(
                "parity-theme",
                displayFamily: "Fraunces", displaySrc: "/fonts/fraunces-variable-latin.woff2",
                sansFamily: "Source Sans 3", sansSrc: "/fonts/source-sans-3-variable-latin.woff2",
                lightBg: "#2a5c9e", darkBg: "#0d1f3c"));

        [Fact]
        public void CarriesTheSameLightTokenAsTheLiveRootBlock()
        {
            // Act: compose both the live (:root) and the scoped preview sheets from the SAME theme.
            var liveCss = ThemeCssComposer.Compose(theme);
            var previewCss = ThemeCssComposer.ComposeScoped(theme, ThemePreviewController.ContainerSelector);

            // Assert: identical token VALUE, only the selector differs — proving this is the SAME
            // composer, not a forked TypeScript re-implementation (SPEC F103.5).
            var liveBlock = ComposerFixtures.ExtractBlockBody(liveCss, ":root");
            var previewBlock = ComposerFixtures.ExtractBlockBody(previewCss, ThemePreviewController.ContainerSelector);
            Assert.Contains("--bg: #2a5c9e;", liveBlock, StringComparison.Ordinal);
            Assert.Contains("--bg: #2a5c9e;", previewBlock, StringComparison.Ordinal);
        }

        [Fact]
        public void CarriesTheSameFontFaceRulesAsTheLivePath()
        {
            // Act: as above.
            var liveCss = ThemeCssComposer.Compose(theme);
            var previewCss = ThemeCssComposer.ComposeScoped(theme, ThemePreviewController.ContainerSelector);

            // Assert: the SAME @font-face src as the live path — v1 themes reference only the
            // already-loaded curated fonts (SPEC F103.5/F103.10), so a preview never introduces a
            // font the admin page hasn't already fetched.
            Assert.Contains("/fonts/fraunces-variable-latin.woff2", liveCss, StringComparison.Ordinal);
            Assert.Contains("/fonts/fraunces-variable-latin.woff2", previewCss, StringComparison.Ordinal);
        }

        [Fact]
        public void TheLivePathIsUnaffectedByTheScopedOverloadExisting()
        {
            // Assert: Compose(theme) alone still emits exactly the three :root-anchored selectors
            // Story264_ComposedStylesheet.cs already pins — this scenario only adds the direct
            // side-by-side comparison above, it does not re-derive that whole contract.
            var liveCss = ThemeCssComposer.Compose(theme);
            Assert.Contains(":root {", liveCss, StringComparison.Ordinal);
            Assert.Contains(":root[data-theme=\"dark\"] {", liveCss, StringComparison.Ordinal);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioRejectingBadPreviewRequests
    {
        [Fact]
        public async Task AnOversizeBodyIsRefusedWith413()
        {
            // Given a body over the shared import size cap,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostPreviewAsync(client, new string('a', 300 * 1024));

            // Then it responds 413 (AC4's sibling gate — the same shared BoundedImportBodyReader
            // control ThemesImportController uses).
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }

        [Fact]
        public async Task MalformedJsonIsRefusedWith400()
        {
            // Given a body that is not valid JSON,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostPreviewAsync(client, "{ this is not valid json");

            // Then it responds 400 — deserialization-as-validation (AC5's sibling gate).
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task AStructurallyInvalidManifestIsRefusedWith400()
        {
            // Given a manifest missing every field ThemeManifestParser requires,
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostPreviewAsync(client, """{ "slug": "incomplete-theme" }""");

            // Then it responds 400 naming the structural gap.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("incomplete-theme", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task ANewerMajorManifestIsRefusedNamingBothVersions()
        {
            // Given an otherwise-valid manifest whose schema major exceeds the app's (Dean's directive
            // 2026-08-05: preview refuses what import refuses — SPEC F103.6 AC6, ported here),
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostPreviewAsync(
                client, PreviewGateFixtures.ValidManifestJsonWithSchemaVersion("preview-dj-future", "2"));
            var body = await response.Content.ReadAsStringAsync();

            // Then it responds 400 naming both versions — the EXACT phrase
            // Story272_ThemeImport.cs's own ANewerMajorManifestIsRefusedNamingBothVersions asserts,
            // proving this is the same shared ImportProblems copy, not a forked one.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(
                "schema version 2 is newer than this station's supported version 1", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task APresentButUnreadableSchemaVersionIsRefusedWith400()
        {
            // Given an otherwise-valid manifest whose schemaVersion is present but not a readable
            // whole number (a JSON string, mirroring Story272_ThemeImport.cs's own
            // AnUnreadableSchemaVersionIsRefusedWith400 theory) — refused, never silently treated as
            // absent (PLAN T184 review F2, ported here),
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostPreviewAsync(
                client, PreviewGateFixtures.ValidManifestJsonWithSchemaVersion("preview-dj-unreadable", "\"2\""));

            // Then it responds 400.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task AManifestReferencingAnUnvendoredFontIsRefusedWith400()
        {
            // Given a manifest whose font src has the right SHAPE (ThemeManifestParser.FontSrcPattern
            // accepts it) but names a face GenWave never vendored (SPEC F103.10, PLAN T188 — Dean's
            // directive 2026-08-05: an operator must never be sold a live preview of a theme its own
            // import route would go on to reject),
            await using var factory = new ThemePreviewWebFactory();
            var client = await LoggedInClientAsync(factory);

            // When it is posted,
            var response = await PostPreviewAsync(
                client, PreviewGateFixtures.ManifestJsonWithUnvendoredFontSrc("preview-off-catalog-font"));
            var body = await response.Content.ReadAsStringAsync();

            // Then it responds 400 naming the missing face and the vendored set — the SAME copy
            // Story272_ThemeImport.cs's own AManifestReferencingAnUnvendoredFontIsRefusedWith400
            // asserts, proving this is the same shared ThemeFontProvenanceValidator/ImportProblems
            // pair, not a forked one.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("/fonts/nonexistent.woff2", body, StringComparison.Ordinal);
            Assert.Contains("/fonts/fraunces-variable-latin.woff2", body, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioUnauthenticatedAccessIsRefused
    {
        [Fact]
        public async Task AnonymousRequestReceives401()
        {
            // Given no session (AdminSurface + Settings, the same posture as ThemesImportController),
            await using var factory = new ThemePreviewWebFactory();
            var client = factory.CreateClient();

            // When a preview is posted with no prior login,
            var response = await PostPreviewAsync(
                client,
                ComposerFixtures.ManifestJson(
                    "anon-theme",
                    displayFamily: "Fraunces", displaySrc: "/fonts/fraunces-variable-latin.woff2",
                    sansFamily: "Source Sans 3", sansSrc: "/fonts/source-sans-3-variable-latin.woff2",
                    lightBg: "#2a5c9e", darkBg: "#0d1f3c"));

            // Then it responds 401.
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
