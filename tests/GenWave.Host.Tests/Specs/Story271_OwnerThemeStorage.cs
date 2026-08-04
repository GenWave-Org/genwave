// STORY-271 — Owner themes are stored and resolved (SPEC F103.7, F103.8)
//
// BDD specification — xUnit. Imported themes persist in a new station.theme store (db/31) and are
// resolved/composed by ThemeCatalog exactly like shipped ones — the single Load path the Layer-A
// design reserved ("an owner's stored manifest goes through the exact same Load path"). The
// station's two embedded defaults remain the F102.7 offline floor when the DB is empty/unreachable.
//
// PENDING T181 (db/31 + IThemeStore) / T182 (ThemeCatalog shipped∪owner, wire) / T183 (Station:Theme
// choice widens). At least one Scenario drives the real load/resolve path (T182 is a wire task).
// One assertion per Fact; sad path (a shipped slug cannot be shadowed) is its own block.

using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureOwnerThemeStorageAndResolution
{
    const string PendingStore = "pending T181/T182 — station.theme store + ThemeCatalog shipped∪owner";
    const string PendingChoice = "pending T183 — Station:Theme choice widens to imported slugs";

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAnOwnerThemePersists
    {
        [Fact(Skip = PendingStore)]
        public void ARowHoldsTheDefinitionAndProvenance()
        {
            // Given a valid theme manifest,
            // When it is stored,
            // Then a station.theme row holds its definition jsonb plus imported_from/imported_at (AC1).
            Assert.Fail(PendingStore);
        }
    }

    public sealed class ScenarioTheCatalogLoadsShippedAndOwnerThemes
    {
        [Fact(Skip = PendingStore)]
        public void AllThreeResolveThroughTheOneLoadPath()
        {
            // Given two embedded defaults and one stored owner theme,
            // When ThemeCatalog loads (the production load path),
            // Then all three resolve and compose through the one Load/ThemeManifestParser path (AC2).
            Assert.Fail(PendingStore);
        }
    }

    public sealed class ScenarioTheChoiceWidensToImportedSlugs
    {
        [Fact(Skip = PendingChoice)]
        public void TheOwnerThemesSlugIsASelectableChoice()
        {
            // Given a stored owner theme,
            // When the Station:Theme setting choices are read,
            // Then the owner theme's slug is a selectable choice (AC3) — the closed choice widens
            //      from shipped-only, sourced from ThemeCatalog.All not LoadShipped.
            Assert.Fail(PendingChoice);
        }
    }

    public sealed class ScenarioTheOfflineFloorHolds
    {
        [Fact(Skip = PendingStore)]
        public void TheTwoEmbeddedDefaultsStillResolveWithNoDatabase()
        {
            // Given an empty or unreachable station database,
            // When the app resolves a theme,
            // Then the two embedded defaults still resolve and render (AC4, F102.7) — the new DB
            //      dependency must not regress the never-unstyled offline floor.
            Assert.Fail(PendingStore);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAShippedSlugCannotBeShadowed
    {
        [Fact(Skip = PendingStore)]
        public void ItIsRefusedAndTheNoDuplicateSlugInvariantHolds()
        {
            // Given a manifest whose slug equals an embedded default's,
            // When it is stored,
            // Then it is refused and ThemeCatalog keeps its no-duplicate-slug invariant (AC5, F103.8)
            //      — the offline-fallback defaults cannot be shadowed.
            Assert.Fail(PendingStore);
        }
    }
}
