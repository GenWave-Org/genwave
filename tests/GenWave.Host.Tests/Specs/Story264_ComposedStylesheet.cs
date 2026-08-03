// STORY-264 — The composed stylesheet (SPEC F102.3, F102.4, F102.6, F102.7)
//
// BDD specification — xUnit. The api composes a theme into CSS and SERVES it — never
// inlines it. That is a CSP consequence, not a style preference: `style-src 'self'` ships
// today with "no 'unsafe-inline' is needed and none is granted"
// (SpectatorSecurityHeadersMiddleware, pinned by Gh180_SpectatorSecurityHeaders), and a
// same-origin stylesheet is already `'self'`. Inlining would have weakened the policy for
// every surface.
//
// Only the ACTIVE theme's @font-face rules are emitted — with 6+ themes each able to carry
// its own faces, emitting all of them would be ruinous page weight. Runtime composition
// makes per-theme emission free.
//
// T159 lands ThemeCssComposer and unskips STORY-264's composer-scoped specs below. The
// endpoints (T160/T161) and resolution itself (T164) are later tasks, so those specs stay
// pending here. The unresolvable-slug scenario (AC7) is translated to this seam: resolving
// "nothing" to the shipped default is a CALLER'S job (T164), so what T159 proves is that
// composing the substituted default behaves exactly like composing any other resolved theme.
//
// ⚠️ SIGNATURE CORRECTED MID-TASK (T159 review, see docs/PLAN.md T159 entry). Originally
// `Compose(theme, mode)` emitting one flat `:root`. That shape is unsatisfiable: F102.10
// forbids a new spectator network call, so the server can never learn a visitor's OS
// preference, yet F102.13 requires `prefers-color-scheme` to still pick the mode. The
// composed sheet has to carry BOTH modes and let the browser's cascade decide — so
// `Compose(theme)` now emits all three selector blocks `admin-ui/app/globals.css` already
// uses (`:root`, `:root[data-theme="dark"]`, `@media (prefers-color-scheme: dark) {
// :root:not([data-theme]) }`), matching those selectors exactly so a later-loading composed
// sheet overrides the static one cleanly rather than losing to it or tying its specificity.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using GenWave.Core.Abstractions;
using GenWave.Host.Theming;
using GenWave.Tts;

namespace GenWave.Host.Tests.Specs;

/// <summary>Mirrors Story173's own <c>SpectatorPageWebFactory</c> — the standard anonymous,
/// spectator-mode-on host boot used across the spectator surface's specs.</summary>
file sealed class ThemeCssWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Station:SpectatorMode", "true");
        builder.UseSetting("ConnectionStrings:Library", "Host=nowhere;Database=test");
        builder.UseSetting("Admin:Password", "test-password-x7z");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IMediaCatalog>();
            services.AddSingleton<IMediaCatalog>(new FakeMediaCatalog(ready: null));
            services.RemoveAll<IActivePersonaAccessor>();
            services.AddSingleton<IActivePersonaAccessor>(new FakeActivePersonaAccessor());
        });
    }
}

public static class FeatureComposedStylesheet
{
    const string PendingAdminRoute = "Pending T161 — see docs/PLAN.md";
    const string PendingWire = "Pending T162 — see docs/PLAN.md";

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioTheSpectatorSurfaceServesComposedCss
    {
        [Fact]
        public async Task RespondsOk()
        {
            // Arrange: a running station with an active theme.
            await using var factory = new ThemeCssWebFactory();
            var client = factory.CreateClient();

            // Act: anonymous GET /spectator/theme.css.
            var response = await client.GetAsync("/spectator/theme.css");

            // Assert: 200 (AC1). Anonymous because the spectator surface takes no
            //         credentials — F63.1's "renders in a private window" property.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task RespondsWithCssContentType()
        {
            // Arrange: as above.
            await using var factory = new ThemeCssWebFactory();
            var client = factory.CreateClient();

            // Act: anonymous GET /spectator/theme.css.
            var response = await client.GetAsync("/spectator/theme.css");

            // Assert: content-type is text/css (AC1).
            Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        }
    }

    public sealed class ScenarioTheAdminSurfaceServesComposedCss
    {
        [Fact(Skip = PendingAdminRoute)]
        public void RespondsOk()
        {
            // Arrange: a running station with an active theme.
            // Act:     GET /api/theme.css.
            // Assert:  200 (AC2). admin_ui reaches this through its existing
            //          next.config.ts `/api/:path*` rewrite, so it is same-origin in the
            //          browser — no CORS, and `style-src 'self'` holds there too.
            Assert.Fail("pending T161 — /api/theme.css");
        }

        [Fact(Skip = PendingAdminRoute)]
        public void RespondsWithCssContentType()
        {
            // Arrange: as above.
            // Act:     GET /api/theme.css.
            // Assert:  content-type is text/css (AC2).
            Assert.Fail("pending T161 — content type");
        }
    }

    public sealed class ScenarioTheSheetCarriesTheActiveThemesTokens
    {
        readonly string css;

        public ScenarioTheSheetCarriesTheActiveThemesTokens()
        {
            // Arrange: a station whose active theme is a known slug, authored with distinct
            //          light/dark token values so a block mix-up in the composer would be
            //          caught rather than accidentally passed.
            var theme = ComposerFixtures.LoadSingle(
                "resolved-mode-theme",
                ComposerFixtures.ManifestJson(
                    "resolved-mode-theme",
                    displayFamily: "Fraunces", displaySrc: "/fonts/fraunces-variable-latin.woff2",
                    sansFamily: "Source Sans 3", sansSrc: "/fonts/source-sans-3-variable-latin.woff2",
                    lightBg: "#f6efe3", darkBg: "#1e1713"));

            // Act: read the composed stylesheet — the composer can no longer be handed a single
            //      resolved mode (F102.10 forbids the server-side network call resolution would
            //      need; F102.13 still requires prefers-color-scheme to pick a mode), so it
            //      emits every mode's tokens, in every selector block a mode-aware page reads.
            css = ThemeCssComposer.Compose(theme);
        }

        [Fact]
        public void TheRootBlockCarriesTheLightTokens()
        {
            // Assert: the flat :root block — the light default — carries the LIGHT value (AC3).
            var block = ComposerFixtures.ExtractBlockBody(css, ":root");
            Assert.Contains("--bg: #f6efe3;", block, StringComparison.Ordinal);
        }

        [Fact]
        public void TheExplicitDarkBlockCarriesTheDarkTokens()
        {
            // Assert: :root[data-theme="dark"] — an explicit choice — carries the DARK value
            //          (AC3), not the light one.
            var block = ComposerFixtures.ExtractBlockBody(css, ":root[data-theme=\"dark\"]");
            Assert.Contains("--bg: #1e1713;", block, StringComparison.Ordinal);
        }

        [Fact]
        public void TheSystemDarkBlockCarriesTheDarkTokens()
        {
            // Assert: the prefers-color-scheme block — the OS default when nobody has made an
            //          explicit choice — ALSO carries the DARK value (AC3, F102.13): an
            //          OS-dark visitor must never be stranded in light just because no
            //          explicit choice was ever made.
            var block = ComposerFixtures.ExtractBlockBody(css, ":root:not([data-theme])");
            Assert.Contains("--bg: #1e1713;", block, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheComposedSelectorsMatchTheStaticSheets
    {
        [Fact]
        public void EmitsExactlyTheThreeSelectorsGlobalsCssDefines()
        {
            // Arrange: a resolved theme — any theme; this property is about the SELECTORS the
            //          composer emits, not any one theme's token values.
            var theme = ComposerFixtures.LoadSingle(
                "selector-parity-theme",
                ComposerFixtures.ManifestJson(
                    "selector-parity-theme",
                    displayFamily: "Fraunces", displaySrc: "/fonts/fraunces-variable-latin.woff2",
                    sansFamily: "Source Sans 3", sansSrc: "/fonts/source-sans-3-variable-latin.woff2",
                    lightBg: "#f6efe3", darkBg: "#1e1713"));

            // Act: compose it.
            var css = ThemeCssComposer.Compose(theme);

            // Assert: the exact three selectors admin-ui/app/globals.css uses (globals.css:58,
            //          106, 139-140) are all present — the property the whole three-block
            //          correction exists to preserve. A later-loading composed sheet only
            //          overrides the static one cleanly if its selectors are IDENTICAL, not
            //          merely equivalent: a flat single-mode :root would tie the static
            //          spectator @media block's specificity (stranding an OS-dark visitor in
            //          light) or lose outright to the static admin blocks' higher-specificity
            //          attribute selectors — the exact regression this spec pins against.
            Assert.Contains(":root {", css, StringComparison.Ordinal);
            Assert.Contains(":root[data-theme=\"dark\"] {", css, StringComparison.Ordinal);
            Assert.Contains("@media (prefers-color-scheme: dark) {", css, StringComparison.Ordinal);
            Assert.Contains(":root:not([data-theme]) {", css, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioOnlyTheActiveThemesFacesAreEmitted
    {
        readonly string css;

        public ScenarioOnlyTheActiveThemesFacesAreEmitted()
        {
            // Arrange: a shelf holding MORE THAN ONE theme with DIFFERENT fonts — a
            //          single-theme shelf would let a regression here pass trivially.
            var activeSource = new ThemeManifestSource("active.json", ComposerFixtures.ManifestJson(
                "active-theme",
                displayFamily: "Fraunces", displaySrc: "/fonts/fraunces-variable-latin.woff2",
                sansFamily: "Source Sans 3", sansSrc: "/fonts/source-sans-3-variable-latin.woff2",
                lightBg: "#f6efe3", darkBg: "#1e1713"));
            var inactiveSource = new ThemeManifestSource("inactive.json", ComposerFixtures.ManifestJson(
                "inactive-theme",
                displayFamily: "Bespoke Display", displaySrc: "/fonts/bespoke-display.woff2",
                sansFamily: "Bespoke Sans", sansSrc: "/fonts/bespoke-sans.woff2",
                lightBg: "#ffffff", darkBg: "#000000"));
            var catalog = ThemeCatalog.Load([activeSource, inactiveSource]);
            Assert.True(catalog.TryGetBySlug("active-theme", out var activeTheme));

            // Act: read the composed stylesheet for the ACTIVE theme only.
            css = ThemeCssComposer.Compose(activeTheme);
        }

        [Fact]
        public void EmitsFontFaceRulesForTheActiveThemesFaces()
        {
            // Assert: @font-face rules for the active theme's faces are present (AC4).
            Assert.Contains("font-family: \"Fraunces\";", css, StringComparison.Ordinal);
            Assert.Contains("/fonts/fraunces-variable-latin.woff2", css, StringComparison.Ordinal);
        }

        [Fact]
        public void OmitsFontFaceRulesForAnInactiveTheme()
        {
            // Assert: NO @font-face rule references the inactive theme's faces (AC5) — the
            //          whole page-weight argument for runtime composition.
            Assert.DoesNotContain("Bespoke Display", css, StringComparison.Ordinal);
            Assert.DoesNotContain("Bespoke Sans", css, StringComparison.Ordinal);
            Assert.DoesNotContain("/fonts/bespoke-display.woff2", css, StringComparison.Ordinal);
            Assert.DoesNotContain("/fonts/bespoke-sans.woff2", css, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioTheStaticDefaultSurvivesForFallback
    {
        [Fact]
        public void StaticStylesheetsStillCarryTheShippedDefaultsTokens()
        {
            // Arrange: the statically served stylesheets (spectator/styles.css,
            //          admin-ui/app/globals.css), read directly off disk.
            var spectatorCss = File.ReadAllText(Path.Combine(
                ComposerFixtures.RepoRoot(), "src", "GenWave.Host", "wwwroot", "spectator", "styles.css"));
            var adminCss = File.ReadAllText(Path.Combine(
                ComposerFixtures.RepoRoot(), "admin-ui", "app", "globals.css"));

            // Act: read them (no transformation — presence is the whole claim).

            // Assert: they still carry the shipped default's tokens (AC6) — the
            //          never-unstyled fallback. The never-silent instinct, applied to paint.
            Assert.Contains("--bg: #f6efe3;", spectatorCss, StringComparison.Ordinal);
            Assert.Contains("--bg: #f6efe3;", adminCss, StringComparison.Ordinal);
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAnUnresolvableSlugRendersTheDefault
    {
        readonly ThemeManifest fallbackTheme;

        public ScenarioAnUnresolvableSlugRendersTheDefault()
        {
            // Arrange: the shipped catalog, and an active slug that matches nothing in it.
            //          Resolving "nothing" to the shipped default (SPEC F102.5/F102.6) is
            //          T164's job — this composer never sees a slug at all. What IS its job:
            //          once a caller has already substituted the shipped default (exactly
            //          what TryGetBySlug failing forces a caller to do), composing it must
            //          behave exactly like composing any other resolved theme.
            var catalog = ThemeCatalog.LoadShipped();
            Assert.False(catalog.TryGetBySlug("no-such-theme-vNext", out _));
            Assert.True(catalog.TryGetBySlug("cats-whisker", out var theme));
            fallbackTheme = theme;
        }

        [Fact]
        public void RespondsOkRatherThanErroring()
        {
            // Act: compose the substituted default — never the unresolvable slug itself.
            var thrown = Record.Exception(() => ThemeCssComposer.Compose(fallbackTheme));

            // Assert: composing succeeds (AC7's "200, not 404/500" translated to this seam
            //          — the actual status code is T160/T161's to assert).
            Assert.Null(thrown);
        }

        [Fact]
        public void CarriesTheShippedDefaultsTokens()
        {
            // Act: compose the substituted default.
            var css = ThemeCssComposer.Compose(fallbackTheme);

            // Assert: it carries the shipped default's tokens (AC7). A bad theme value must
            //          never yield an unstyled or broken page.
            Assert.Contains("--bg: #f6efe3;", css, StringComparison.Ordinal);
            Assert.Contains("--accent: #b94f29;", css, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioAHostileTokenNameCannotLoadAtAll
    {
        [Fact]
        public void LoadingFailsBeforeAnyCompositionCanHappen()
        {
            // Arrange: a token whose NAME (not value) carries the "}"/";" a CSS-injection
            //          payload needs to close the enclosing :root block early. Review finding
            //          (T159 round 2): this must fail at LOAD, not only if/when something
            //          composes the theme — a malformed manifest that loads clean becomes
            //          exactly the "request-time condition to route around" ThemeCatalog's own
            //          remarks forbid, and it would sail straight through T158's vocabulary
            //          gate too (which asserts the 18 real names are present, never that
            //          nothing EXTRA is there).
            var source = new ThemeManifestSource(
                "hostile-token.json",
                ComposerFixtures.ManifestJsonWithHostileTokenName("hostile-token", "bg}; } .evil{color:red"));

            // Act: ThemeCatalog loads it — ThemeCssComposer is never reached.
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeCatalog.Load([source]));

            // Assert: loading fails naming the offending theme.
            Assert.Contains("hostile-token", ex.Message, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioAHostileTokenNameCannotBreakOutOfItsDeclaration
    {
        [Fact]
        public void ComposeRejectsATokenNameOutsideTheSafeCustomPropertyShape()
        {
            // Arrange: a ThemeManifest carrying a hostile token name CONSTRUCTED DIRECTLY,
            //          bypassing ThemeManifestParser entirely — this is what proves
            //          ThemeCssComposer's own TokenNamePattern check is a genuine second gate,
            //          not merely a passthrough of the parser's (which the scenario above
            //          already proves catches this at load). Nothing in production constructs
            //          a ThemeManifest this way today (ThemeManifestParser is the only
            //          caller), but the composer must fail closed regardless of how a
            //          malformed manifest ever reaches it.
            var theme = new ThemeManifest(
                "hostile-token-direct",
                "Hostile Token (Direct)",
                "GenWave",
                new ThemeFonts(
                    new ThemeFontFace("Fraunces", [new ThemeFontAsset("/fonts/fraunces-variable-latin.woff2", "400 600", "normal")]),
                    new ThemeFontFace("Source Sans 3", [new ThemeFontAsset("/fonts/source-sans-3-variable-latin.woff2", "400", "normal")])),
                new ThemeModes(
                    new Dictionary<string, string> { ["ink"] = "#2b2320", ["bg}; } .evil{color:red"] = "#000000" },
                    new Dictionary<string, string> { ["ink"] = "#f0e7d8", ["bg}; } .evil{color:red"] = "#000000" }));

            // Act/Assert: composing throws rather than emitting the malformed declaration.
            Assert.Throws<ThemeManifestException>(() => ThemeCssComposer.Compose(theme));
        }
    }

    public sealed class ScenarioAMissingComposedSheetDegradesRatherThanStrips
    {
        [Fact(Skip = PendingWire)]
        public void PageRendersInDefaultWirelessRatherThanUnstyled()
        {
            // Arrange: the theme endpoint is unavailable (slow, failed, or absent).
            // Act:     serve and render the page.
            // Assert:  it renders in default Wireless rather than unstyled (AC8) — verifiable
            //          by serving the page with the theme endpoint disabled.
            Assert.Fail("pending T162 — degraded-paint wire acceptance");
        }
    }
}

/// <summary>Raw theme manifest JSON builders local to this spec file, mirroring
/// Story263_ThemesBecomeData.cs's own ThemeFixtures — this file is the only one T159 is
/// scoped to touch, so its fixtures live here rather than a shared Fakes/ helper.</summary>
static class ComposerFixtures
{
    /// <summary>Loads a single manifest document and returns the theme it produces under
    /// <paramref name="slug"/>, failing the calling test immediately if it doesn't parse or
    /// isn't retrievable — every scenario here needs exactly this, never the catalog itself.</summary>
    public static ThemeManifest LoadSingle(string slug, string json)
    {
        var catalog = ThemeCatalog.Load([new ThemeManifestSource($"{slug}.json", json)]);
        Assert.True(catalog.TryGetBySlug(slug, out var theme));
        return theme;
    }

    /// <summary>Extracts the declaration body between <paramref name="selector"/> and its
    /// closing brace — used to prove WHICH of the composer's three selector blocks carries a
    /// given value. With all three blocks now emitted in one sheet, a bare
    /// <c>Assert.Contains("--bg: #1e1713;", css)</c> can no longer tell "this value is
    /// somewhere in the sheet" apart from "it's in the right block" — this makes the
    /// assertion block-aware instead.</summary>
    public static string ExtractBlockBody(string css, string selector)
    {
        var marker = selector + " {";
        var openIndex = css.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(openIndex >= 0, $"selector '{selector}' not found in composed CSS");

        var bodyStart = openIndex + marker.Length;
        var bodyEnd = css.IndexOf('}', bodyStart);
        Assert.True(bodyEnd >= 0, $"no closing brace found for selector '{selector}'");

        return css[bodyStart..bodyEnd];
    }

    public static string ManifestJson(
        string slug,
        string displayFamily, string displaySrc,
        string sansFamily, string sansSrc,
        string lightBg, string darkBg) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Test Theme",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "{{displayFamily}}", "assets": [ { "src": "{{displaySrc}}", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "{{sansFamily}}", "assets": [ { "src": "{{sansSrc}}", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "{{lightBg}}", "ink": "#2b2320" },
            "dark": { "bg": "{{darkBg}}", "ink": "#f0e7d8" }
          }
        }
        """;

    public static string ManifestJsonWithHostileTokenName(string slug, string hostileTokenName) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Hostile Token",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces-variable-latin.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3-variable-latin.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "ink": "#2b2320", "{{hostileTokenName}}": "#000000" },
            "dark": { "ink": "#f0e7d8", "{{hostileTokenName}}": "#000000" }
          }
        }
        """;

    /// <summary>Walks up from the test assembly's output directory to the repo root
    /// (identified by GenWave.sln) — same idiom as Story175_BuildStampedVersion.cs's own
    /// RepoRoot(), so AC6 can read the real static stylesheets rather than a copy.</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
