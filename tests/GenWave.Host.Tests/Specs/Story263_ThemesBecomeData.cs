// STORY-263 — Themes become data (SPEC F102.2, F102.1)
//
// BDD specification — xUnit. A theme is a manifest: stable `slug`, per-mode (light/dark)
// colour tokens, font declarations. A GenWave-authored manifest and an owner-authored one
// are the SAME shape — nothing in the format distinguishes them. That single rule is what
// keeps the Layer B editor (gh-#206) from being a bolt-on.
//
// T156 lands ThemeManifest + ThemeCatalog and unskips every spec below EXCEPT
// ScenarioTodaysPalettesAreOneTheme, which needs the real converted default manifest (T157) and
// stays pending. These specs drive ThemeCatalog.Load directly (an in-memory set of raw manifest
// documents) rather than ThemeCatalog.LoadShipped's embedded resources — no shipped content exists
// yet (T157), and Load is the seam both LoadShipped and the future Layer B editor loader share.
//
// One deliberate exception: ScenarioLoadingWithNoShippedManifests below exercises LoadShipped()
// directly. It pins the invariant that booting with ZERO embedded manifests must fail loudly rather
// than silently serving an empty catalog (review finding, T156) — that is only reliably true BEFORE
// T157 embeds real manifest files, which is exactly today's state, so T157 will need to replace it
// with a happy-path LoadShipped assertion once shipped content exists.

using GenWave.Host.Theming;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureThemesBecomeData
{
    const string PendingDefault = "Pending T157 — see docs/PLAN.md";

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioAShippedManifestLoads
    {
        [Fact]
        public void ThemeIsRetrievableByItsSlug()
        {
            // Arrange: a shipped theme manifest carrying a stable slug.
            var source = new ThemeManifestSource("cream-enamel.json", ThemeFixtures.ValidManifestJson("cream-enamel"));

            // Act: ThemeCatalog loads the shipped themes.
            var catalog = ThemeCatalog.Load([source]);

            // Assert: the theme is retrievable by that slug (AC1). Which slug it's retrievable
            //          BY isn't asserted again here — the catalog is keyed by theme.Slug, so that
            //          would be tautological (it cannot fail independently of TryGetBySlug itself).
            Assert.True(catalog.TryGetBySlug("cream-enamel", out _));
        }
    }

    public sealed class ScenarioAThemeCarriesBothModes
    {
        readonly ThemeModes modes;

        public ScenarioAThemeCarriesBothModes()
        {
            // Arrange: a shipped theme, its modes read once.
            var source = new ThemeManifestSource("dual-mode.json", ThemeFixtures.ValidManifestJson("dual-mode"));
            var catalog = ThemeCatalog.Load([source]);
            Assert.True(catalog.TryGetBySlug("dual-mode", out var theme));
            modes = theme.Modes;
        }

        [Fact]
        public void LightModeCarriesTheCompleteTokenSet()
        {
            // Assert: a COMPLETE token set exists for light (AC2). Flat one-look themes were
            //          rejected at design — they regress automatic prefers-color-scheme dark and
            //          strand OS-dark visitors in a light palette.
            Assert.Equivalent(ThemeFixtures.Tokens, modes.Light.Keys);
        }

        [Fact]
        public void DarkModeCarriesTheCompleteTokenSet()
        {
            // Assert: a COMPLETE token set exists for dark too (AC2) — the other half of the same
            //          claim; light and dark can fail this independently of one another.
            Assert.Equivalent(ThemeFixtures.Tokens, modes.Dark.Keys);
        }
    }

    public sealed class ScenarioTodaysPalettesAreOneTheme
    {
        [Fact(Skip = PendingDefault)]
        public void CreamEnamelIsTheDefaultThemesLightMode()
        {
            // Arrange: the shipped default theme.
            // Act:     read its light mode.
            // Assert:  it carries today's "cream enamel" token values (AC3).
            Assert.Fail("pending T157 — default manifest");
        }

        [Fact(Skip = PendingDefault)]
        public void WalnutAndBrassIsTheDefaultThemesDarkMode()
        {
            // Arrange: the shipped default theme.
            // Act:     read its dark mode.
            // Assert:  it carries today's "walnut & brass" values (AC3). ONE theme with two
            //          modes — not two themes.
            Assert.Fail("pending T157 — default manifest");
        }
    }

    public sealed class ScenarioTheFormatDoesNotDistinguishAuthorship
    {
        readonly ThemeManifest genWaveTheme;
        readonly ThemeManifest ownerTheme;

        public ScenarioTheFormatDoesNotDistinguishAuthorship()
        {
            // Arrange: a GenWave-authored manifest and an owner-authored manifest, both loaded.
            var genWaveSource = new ThemeManifestSource(
                "genwave.json", ThemeFixtures.ValidManifestJson("genwave-made", author: "GenWave"));
            var ownerSource = new ThemeManifestSource(
                "owner.json", ThemeFixtures.ValidManifestJson("owner-made", author: "Dean Mills"));
            var catalog = ThemeCatalog.Load([genWaveSource, ownerSource]);

            Assert.True(catalog.TryGetBySlug("genwave-made", out var genWave));
            Assert.True(catalog.TryGetBySlug("owner-made", out var owner));
            genWaveTheme = genWave;
            ownerTheme = owner;
        }

        [Fact]
        public void NoThemingTypeDeclaresAnAuthorshipDiscriminator()
        {
            // Assert: no field in the format marks who authored a manifest (AC4) — pinned at the
            //          TYPE level across every Theming record, not just ThemeManifest, since a
            //          discriminator hiding on e.g. ThemeFonts or ThemeModes would otherwise sail
            //          through a check scoped to ThemeManifest alone. Stock themes are just themes
            //          that ship in the box; this is what makes a catalog theme (gh-#206) a
            //          fetch-and-store rather than a second mechanism.
            var authorshipDiscriminatorNames = new[] { "IsBuiltIn", "Source", "Origin", "IsShipped", "IsStock" };
            var themingRecordTypes = new[]
            {
                typeof(ThemeManifest), typeof(ThemeFonts), typeof(ThemeFontFace), typeof(ThemeFontAsset), typeof(ThemeModes),
            };
            var propertyNames = themingRecordTypes.SelectMany(t => t.GetProperties()).Select(p => p.Name);
            Assert.Empty(propertyNames.Intersect(authorshipDiscriminatorNames));
        }

        [Fact]
        public void GenWaveMadeAndOwnerMadeAreTheSameRuntimeType()
        {
            // Assert: pinned at the INSTANCE level too (AC4) — a GenWave-authored theme and an
            //          owner-authored theme are literally the same .NET type, never e.g. a
            //          ThemeManifest subclass distinguished by origin.
            Assert.Equal(genWaveTheme.GetType(), ownerTheme.GetType());
        }
    }

    public sealed class ScenarioAThemeDeclaresItsFonts
    {
        readonly ThemeFonts fonts;

        public ScenarioAThemeDeclaresItsFonts()
        {
            // Arrange: a shipped theme manifest, its font declarations read once.
            var source = new ThemeManifestSource("fonted.json", ThemeFixtures.ValidManifestJson("fonted"));
            var catalog = ThemeCatalog.Load([source]);
            Assert.True(catalog.TryGetBySlug("fonted", out var theme));
            fonts = theme.Fonts;
        }

        [Fact]
        public void DisplayFontNamesItsFamilyAndAsset()
        {
            // Assert: the display face names a family and the vendored asset that serves it
            //          (AC5). Fonts are ASSETS, not values — the reason composition is runtime.
            var asset = Assert.Single(fonts.Display.Assets);
            Assert.Equal(("Fraunces", "/fonts/fraunces.woff2"), (fonts.Display.Family, asset.Src));
        }

        [Fact]
        public void SansFontNamesItsFamilyAndAsset()
        {
            // Assert: the sans face names a family and the vendored asset that serves it (AC5) —
            //          the other half of the same claim; display and sans can fail this
            //          independently of one another.
            var asset = Assert.Single(fonts.Sans.Assets);
            Assert.Equal(("Source Sans 3", "/fonts/source-sans-3.woff2"), (fonts.Sans.Family, asset.Src));
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioRejectingIncompleteManifests
    {
        [Fact]
        public void ModeIncompleteManifestFailsNamingTheMissingMode()
        {
            // Arrange: a manifest defining light but not dark.
            var source = new ThemeManifestSource("half-lit.json", ThemeFixtures.ManifestJsonMissingDarkMode("half-lit"));

            // Act: ThemeCatalog loads it.
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeCatalog.Load([source]));

            // Assert: loading fails naming the theme and the missing mode (AC6).
            Assert.Contains("half-lit", ex.Message, StringComparison.Ordinal);
            Assert.Contains("dark", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void MissingTokenFailsNamingThemeModeAndToken()
        {
            // Arrange: a manifest whose dark mode omits a token its light mode defines.
            var source = new ThemeManifestSource(
                "missing-token.json", ThemeFixtures.ManifestJsonWithDarkMissingToken("missing-token", "accent"));

            // Act: ThemeCatalog loads it.
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeCatalog.Load([source]));

            // Assert: loading fails naming the theme, the mode and the absent token (AC8).
            Assert.Contains("missing-token", ex.Message, StringComparison.Ordinal);
            Assert.Contains("dark", ex.Message, StringComparison.Ordinal);
            Assert.Contains("accent", ex.Message, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioRejectingDuplicateSlugs
    {
        [Fact]
        public void DuplicateSlugFailsNamingTheSlug()
        {
            // Arrange: two shipped manifests sharing one slug.
            var first = new ThemeManifestSource("first.json", ThemeFixtures.ValidManifestJson("shared-slug", name: "First"));
            var second = new ThemeManifestSource("second.json", ThemeFixtures.ValidManifestJson("shared-slug", name: "Second"));

            // Act: ThemeCatalog loads them.
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeCatalog.Load([first, second]));

            // Assert: loading fails naming the duplicated slug (AC7). The slug is the
            //          settings value AND the cookie value — ambiguity here is unresolvable
            //          downstream.
            Assert.Contains("shared-slug", ex.Message, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioRejectingUnsafeFontSrc
    {
        [Fact]
        public void AbsoluteUrlFailsNamingTheThemeAndRole()
        {
            // Arrange: a font asset src pointing off-origin instead of the vendored /fonts/ tree —
            //          the exact shape FontSrcPattern's `\A`/`\z` anchors exist to close off.
            var source = new ThemeManifestSource(
                "off-origin-src.json",
                ThemeFixtures.ManifestJsonWithInvalidFontSrc("off-origin-src", "https://evil.example/fonts/a.woff2"));

            // Act: ThemeCatalog loads it.
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeCatalog.Load([source]));

            // Assert: loading fails naming the theme and the font role.
            Assert.Contains("off-origin-src", ex.Message, StringComparison.Ordinal);
            Assert.Contains("display", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TraversalFailsNamingTheThemeAndRole()
        {
            // Arrange: a font asset src attempting to escape the vendored /fonts/ tree via "../".
            var source = new ThemeManifestSource(
                "traversal-src.json",
                ThemeFixtures.ManifestJsonWithInvalidFontSrc("traversal-src", "/fonts/../../etc/passwd"));

            // Act: ThemeCatalog loads it.
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeCatalog.Load([source]));

            // Assert: loading fails naming the theme and the font role.
            Assert.Contains("traversal-src", ex.Message, StringComparison.Ordinal);
            Assert.Contains("display", ex.Message, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioRejectingUnsafeFontFamily
    {
        [Fact]
        public void InjectingValueFailsNamingTheThemeAndRole()
        {
            // Arrange: a font-family value carrying the semicolon/colon a CSS-injection payload
            //          needs to escape the `font-family: "<value>"` position T159 interpolates it
            //          into.
            var source = new ThemeManifestSource(
                "bad-family.json",
                ThemeFixtures.ManifestJsonWithInvalidFontFamily("bad-family", "Evil; color: red"));

            // Act: ThemeCatalog loads it.
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeCatalog.Load([source]));

            // Assert: loading fails naming the theme and the font role.
            Assert.Contains("bad-family", ex.Message, StringComparison.Ordinal);
            Assert.Contains("display", ex.Message, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioRejectingUnsafeFontDescriptor
    {
        [Fact]
        public void InvalidWeightFailsNamingTheThemeAndRole()
        {
            // Arrange: a font-weight value carrying the semicolon/brace a CSS-injection payload
            //          needs to escape the `font-weight: <value>;` position T159 interpolates it
            //          into — the same injection primitive as ScenarioRejectingUnsafeFontFamily,
            //          just three lines lower in the parser (weight and style share one
            //          FontDescriptorPattern check, so this one fact covers both positions).
            var source = new ThemeManifestSource(
                "bad-weight.json",
                ThemeFixtures.ManifestJsonWithInvalidFontWeight("bad-weight", "400; } body { display: none"));

            // Act: ThemeCatalog loads it.
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeCatalog.Load([source]));

            // Assert: loading fails naming the theme and the font role.
            Assert.Contains("bad-weight", ex.Message, StringComparison.Ordinal);
            Assert.Contains("display", ex.Message, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioRejectingUnsafeTokenValue
    {
        [Fact]
        public void NonColourValueFailsNamingThemeModeAndToken()
        {
            // Arrange: a token value that isn't the allow-listed hex-colour shape — a `url()`
            //          reference, the exact non-colour form that could otherwise beacon
            //          per-visitor metadata off an authenticated admin session once T159 composes
            //          it into served CSS (review finding, T156).
            var source = new ThemeManifestSource(
                "beacon-token.json",
                ThemeFixtures.ManifestJsonWithInvalidTokenValue(
                    "beacon-token", "accent", "url(https://evil.example/beacon.png)"));

            // Act: ThemeCatalog loads it.
            var ex = Assert.Throws<ThemeManifestException>(() => ThemeCatalog.Load([source]));

            // Assert: loading fails naming the theme, the mode and the offending token (AC6/AC8's
            //          naming contract, applied to the value check).
            Assert.Contains("beacon-token", ex.Message, StringComparison.Ordinal);
            Assert.Contains("light", ex.Message, StringComparison.Ordinal);
            Assert.Contains("accent", ex.Message, StringComparison.Ordinal);
        }
    }

    public sealed class ScenarioLoadingWithNoShippedManifests
    {
        [Fact]
        public void ThrowsRatherThanBootingAnEmptyCatalog()
        {
            // Arrange: today — before T157 lands the converted default manifest — GenWave.Host
            //          embeds ZERO theme resources. That is exactly the condition this guards.

            // Act/Assert: LoadShipped fails loudly rather than returning an empty catalog that
            //          would only break later, once T164's "fall back to the shipped default"
            //          fallback asks it for a theme it doesn't have (review finding, T156).
            Assert.Throws<ThemeManifestException>(() => ThemeCatalog.LoadShipped());
        }
    }
}

/// <summary>Raw theme manifest JSON builders local to this spec file — this file is the only one
/// T156 is scoped to touch, so fixtures live here rather than a shared Fakes/ helper.</summary>
static class ThemeFixtures
{
    public static readonly IReadOnlySet<string> Tokens = new HashSet<string> { "bg", "ink", "accent" };

    public static string ValidManifestJson(string slug, string name = "Test Theme", string author = "GenWave") => $$"""
        {
          "slug": "{{slug}}",
          "name": "{{name}}",
          "author": "{{author}}",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320", "accent": "#b94f29" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8", "accent": "#d96a3d" }
          }
        }
        """;

    public static string ManifestJsonMissingDarkMode(string slug) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Half Lit",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320", "accent": "#b94f29" }
          }
        }
        """;

    public static string ManifestJsonWithDarkMissingToken(string slug, string missingToken) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Missing Token",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320", "{{missingToken}}": "#b94f29" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8" }
          }
        }
        """;

    public static string ManifestJsonWithInvalidFontSrc(string slug, string src) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Bad Src",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "{{src}}", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320", "accent": "#b94f29" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8", "accent": "#d96a3d" }
          }
        }
        """;

    public static string ManifestJsonWithInvalidFontFamily(string slug, string family) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Bad Family",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "{{family}}", "assets": [ { "src": "/fonts/fraunces.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320", "accent": "#b94f29" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8", "accent": "#d96a3d" }
          }
        }
        """;

    public static string ManifestJsonWithInvalidFontWeight(string slug, string weight) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Bad Weight",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces.woff2", "weight": "{{weight}}", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320", "accent": "#b94f29" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8", "accent": "#d96a3d" }
          }
        }
        """;

    public static string ManifestJsonWithInvalidTokenValue(string slug, string token, string value) => $$"""
        {
          "slug": "{{slug}}",
          "name": "Bad Token Value",
          "author": "GenWave",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/fraunces.woff2", "weight": "400 600", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#f6efe3", "ink": "#2b2320", "{{token}}": "{{value}}" },
            "dark": { "bg": "#1e1713", "ink": "#f0e7d8", "accent": "#d96a3d" }
          }
        }
        """;
}
