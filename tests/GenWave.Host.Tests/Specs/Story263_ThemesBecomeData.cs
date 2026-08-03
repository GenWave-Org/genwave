// STORY-263 — Themes become data (SPEC F102.2, F102.1)
//
// BDD specification — xUnit. A theme is a manifest: stable `slug`, per-mode (light/dark)
// colour tokens, font declarations. A GenWave-authored manifest and an owner-authored one
// are the SAME shape — nothing in the format distinguishes them. That single rule is what
// keeps the Layer B editor (gh-#206) from being a bolt-on.
//
// These specs are PENDING (T156/T157): `ThemeCatalog` and the manifest type do not exist
// yet, so the bodies carry the intended arrange/act/assert as comments and fail loudly —
// the house idiom (see Story023). Removing the Skip is /build-loop's job, one task at a
// time. Red the moment it does.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureThemesBecomeData
{
    const string PendingCatalog = "Pending T156 — see docs/PLAN.md";
    const string PendingDefault = "Pending T157 — see docs/PLAN.md";

    // ── HAPPY PATH ────────────────────────────────────────────────────────

    public sealed class ScenarioAShippedManifestLoads
    {
        [Fact(Skip = PendingCatalog)]
        public void ThemeIsRetrievableByItsSlug()
        {
            // Arrange: a shipped theme manifest carrying a stable slug.
            // Act:     ThemeCatalog loads the shipped themes.
            // Assert:  the theme is retrievable by that slug (AC1).
            Assert.Fail("pending T156 — ThemeCatalog");
        }
    }

    public sealed class ScenarioAThemeCarriesBothModes
    {
        [Fact(Skip = PendingCatalog)]
        public void ThemeDefinesLightAndDarkTokenSets()
        {
            // Arrange: a shipped theme.
            // Act:     read its modes.
            // Assert:  a COMPLETE token set exists for light AND dark (AC2). Flat one-look
            //          themes were rejected at design — they regress automatic
            //          prefers-color-scheme dark and strand OS-dark visitors.
            Assert.Fail("pending T156 — mode completeness");
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
        [Fact(Skip = PendingCatalog)]
        public void NoFieldMarksWhoAuthoredAManifest()
        {
            // Arrange: a GenWave-authored manifest and an owner-authored manifest.
            // Act:     load both.
            // Assert:  no field in the format marks which is which (AC4). Stock themes are
            //          just themes that ship in the box; this is what makes a catalog theme
            //          (gh-#206) a fetch-and-store rather than a second mechanism.
            Assert.Fail("pending T156 — one representation");
        }
    }

    public sealed class ScenarioAThemeDeclaresItsFonts
    {
        [Fact(Skip = PendingCatalog)]
        public void FontDeclarationNamesFamilyAndVendoredAssets()
        {
            // Arrange: a shipped theme manifest.
            // Act:     read its font declarations.
            // Assert:  each names a family and the vendored assets that serve it (AC5).
            //          Fonts are ASSETS, not values — the reason composition is runtime.
            Assert.Fail("pending T156 — font declarations");
        }
    }

    // ── SAD PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioRejectingIncompleteManifests
    {
        [Fact(Skip = PendingCatalog)]
        public void ModeIncompleteManifestFailsNamingTheMissingMode()
        {
            // Arrange: a manifest defining light but not dark.
            // Act:     ThemeCatalog loads it.
            // Assert:  loading fails naming the theme and the missing mode (AC6).
            Assert.Fail("pending T156 — mode validation");
        }

        [Fact(Skip = PendingCatalog)]
        public void MissingTokenFailsNamingThemeModeAndToken()
        {
            // Arrange: a manifest whose dark mode omits a token its light mode defines.
            // Act:     ThemeCatalog loads it.
            // Assert:  loading fails naming the theme, the mode and the absent token (AC8).
            Assert.Fail("pending T156 — token validation");
        }
    }

    public sealed class ScenarioRejectingDuplicateSlugs
    {
        [Fact(Skip = PendingCatalog)]
        public void DuplicateSlugFailsNamingTheSlug()
        {
            // Arrange: two shipped manifests sharing one slug.
            // Act:     ThemeCatalog loads them.
            // Assert:  loading fails naming the duplicated slug (AC7). The slug is the
            //          settings value AND the cookie value — ambiguity here is unresolvable
            //          downstream.
            Assert.Fail("pending T156 — slug uniqueness");
        }
    }
}
