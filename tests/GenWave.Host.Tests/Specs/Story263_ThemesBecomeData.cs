// STORY-263 — Themes become data (SPEC F102.2, F102.1)
//
// BDD specification — xUnit. A theme is a manifest: stable `slug`, per-mode (light/dark)
// colour tokens, font declarations. A GenWave-authored manifest and an owner-authored one
// are the SAME shape — nothing in the format distinguishes them. That single rule is what
// keeps the Layer B editor (gh-#206) from being a bolt-on.
//
// T156 landed ThemeManifest + ThemeCatalog and unskipped every spec below except
// ScenarioTodaysPalettesAreOneTheme, which needed the real converted default manifest. T157 lands
// that manifest (src/GenWave.Host/Theming/themes/cats-whisker.json, renamed from cream-enamel.json
// by T174 — slug and display name only, the T167a naming ruling; palette values unchanged) and
// unskips it. Most specs in this file still drive ThemeCatalog.Load directly (an in-memory set of
// raw manifest documents) rather than LoadShipped's embedded resources — Load is the seam both
// LoadShipped and the future Layer B editor loader share.
//
// Two scenarios deliberately exercise LoadShipped() itself, against the real embedded
// cats-whisker.json: ScenarioTodaysPalettesAreOneTheme (AC3, the converted palette values) and
// ScenarioLoadingTheShippedDefaultManifest (proving the embedded-resource path loads end-to-end at
// all — the thing T162 will depend on). Before T157, LoadShipped() had zero embedded manifests to
// find, so this file used to pin the OPPOSITE invariant here (booting with zero shipped manifests
// must fail loudly, review finding T156) under the name ScenarioLoadingWithNoShippedManifests; that
// scenario's premise went false the moment a real manifest embedded, so T157 replaced it with this
// happy-path assertion. The zero-manifest failure mode itself is still real production behaviour
// (see ThemeCatalog.LoadShipped's own remarks) — it just has no manifest-free assembly left to
// exercise it against without faking assembly resource enumeration, which is more machinery than
// the invariant is worth once a real shipped default exists.

using GenWave.Host.Theming;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureThemesBecomeData
{
    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioAShippedManifestLoads
    {
        [Fact]
        public void ThemeIsRetrievableByItsSlug()
        {
            // Arrange: a shipped theme manifest carrying a stable slug.
            var source = new ThemeManifestSource("sample-theme.json", ThemeFixtures.ValidManifestJson("sample-theme"));

            // Act: ThemeCatalog loads the shipped themes.
            var catalog = ThemeCatalog.Load([source]);

            // Assert: the theme is retrievable by that slug (AC1). Which slug it's retrievable
            //          BY isn't asserted again here — the catalog is keyed by theme.Slug, so that
            //          would be tautological (it cannot fail independently of TryGetBySlug itself).
            Assert.True(catalog.TryGetBySlug("sample-theme", out _));
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
        readonly ThemeModes modes;

        public ScenarioTodaysPalettesAreOneTheme()
        {
            // Arrange: the shipped default theme (the real embedded cats-whisker.json, not a
            //          fixture — LoadShipped, not Load), its modes read once.
            var catalog = ThemeCatalog.LoadShipped();
            Assert.True(catalog.TryGetBySlug("cats-whisker", out var theme));
            modes = theme.Modes;
        }

        [Fact]
        public void CatsWhiskerIsTheDefaultThemesLightMode()
        {
            // Assert: light mode carries today's light-palette token values (AC3) — transcribed
            //          from spectator/styles.css's :root block and admin-ui/globals.css's
            //          --sched-* light block, independently verified byte-for-byte against both.
            //          --mute darkened #77685c→#706256 (T174, T158 AA gate — see globals.css).
            Assert.Equivalent(new Dictionary<string, string>
            {
                ["bg"] = "#f6efe3",
                ["surface"] = "#fdf8ee",
                ["surface-2"] = "#efe5d2",
                ["line"] = "#ddd0b8",
                ["ink"] = "#2b2320",
                ["mute"] = "#706256",
                ["accent"] = "#b94f29",
                ["accent-ink"] = "#fdf8ee",
                ["accent-2"] = "#6f632f",
                ["danger"] = "#a63325",
                ["danger-ink"] = "#fdf8ee",
                ["success"] = "#5c7a3f",
                ["sched-1"] = "#e3b7a0",
                ["sched-2"] = "#e8d190",
                ["sched-3"] = "#c3cba0",
                ["sched-4"] = "#d9c1a6",
                ["sched-5"] = "#cbb0c7",
                ["sched-6"] = "#e1b7be",
            }, modes.Light, strict: true);
        }

        [Fact]
        public void WalnutAndBrassIsTheDefaultThemesDarkMode()
        {
            // Assert: dark mode carries today's "walnut & brass" values (AC3) — the other half of
            //          the same claim; light and dark can fail this independently of one another.
            //          ONE theme with two modes — not two themes.
            Assert.Equivalent(new Dictionary<string, string>
            {
                ["bg"] = "#1e1713",
                ["surface"] = "#2a211b",
                ["surface-2"] = "#241c16",
                ["line"] = "#3f342a",
                ["ink"] = "#f0e7d8",
                ["mute"] = "#a89a88",
                ["accent"] = "#d96a3d",
                ["accent-ink"] = "#1e1713",
                ["accent-2"] = "#b3a25e",
                ["danger"] = "#e06a55",
                ["danger-ink"] = "#2a211b",
                ["success"] = "#8fae6a",
                ["sched-1"] = "#5a3226",
                ["sched-2"] = "#5c4e22",
                ["sched-3"] = "#3c4630",
                ["sched-4"] = "#4c3924",
                ["sched-5"] = "#453142",
                ["sched-6"] = "#692b35",
            }, modes.Dark, strict: true);
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

    public sealed class ScenarioLoadingTheShippedDefaultManifest
    {
        [Fact]
        public void TheEmbeddedDefaultManifestLoadsAndIsRetrievableBySlug()
        {
            // Arrange/Act: LoadShipped reads GenWave.Host's real embedded resources — the
            //          production boot path (ARCHITECTURE "Theme system": themes/*.json, embedded
            //          resources in GenWave.Host) — rather than an in-memory ThemeManifestSource
            //          like every other scenario in this file. Never exercised end-to-end before
            //          T157 landed the first real embedded manifest; T162 depends on this path.

            // Assert: the shipped default (T157's cats-whisker.json, renamed by T174) loads and is
            //          retrievable by its slug through that real path.
            Assert.True(ThemeCatalog.LoadShipped().TryGetBySlug("cats-whisker", out _));
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
