// STORY-271 — Owner themes are stored and resolved (SPEC F103.7, F103.8)
//
// BDD specification — xUnit. Imported themes persist in a new station.theme store (db/31) and are
// resolved/composed by ThemeCatalog exactly like shipped ones — the single Load path the Layer-A
// design reserved ("an owner's stored manifest goes through the exact same Load path"). The
// station's embedded defaults remain the F102.7 offline floor when the DB is empty/unreachable.
//
// T181 landed db/31 + IThemeStore (ThemeRepository, FakeThemeStore). T182 lands ThemeCatalog's
// shipped∪owner load (CreateForStation + ReloadOwnerThemesAsync) — AC2, AC4 and AC5 below drive
// that real production path. T183 lands AC3: StationSettingsAllowlist.ThemeChoices sources
// Station:Theme's choices from ThemeCatalog.All (shipped∪owner), not the shipped-only snapshot
// SettingsController/SettingValidator used before. One assertion per Fact; sad path (a shipped slug
// cannot be shadowed) is its own block.
//
// AC1 (T181) is proven against FakeThemeStore (Fakes/FakeThemeStore.cs) rather than the real
// GenWave.MediaLibrary.Station.ThemeRepository: this project carries no Postgres fixture at all
// (mirrors FakeScheduleStore's own remarks and Story225_WishParsing.cs's own "GenWave.Host.Tests has
// no DatabaseFixture" precedent) — a real-Postgres proof of ThemeRepository's own SQL belongs to
// GenWave.MediaLibrary.Tests, the same split Story209_PersonaImportRepository.cs draws for
// PersonaImportRepository (its own Category=Integration ThemeRepository spec is that proof).
// AC2/AC4/AC5 drive ThemeCatalog.CreateForStation/ReloadOwnerThemesAsync against FakeThemeStore for
// the same reason — this project has no WebApplicationFactory route to exercise yet either (T184's
// import route is the first), so calling the catalog's own production members directly IS "the
// production load path" the task calls for.

using GenWave.Core.Abstractions;
using GenWave.Core.Domain;
using GenWave.Host.Configuration;
using GenWave.Host.Theming;
using GenWave.Host.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenWave.Host.Tests.Specs;

public static class FeatureOwnerThemeStorageAndResolution
{
    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAnOwnerThemePersists
    {
        [Fact]
        public async Task ARowHoldsTheDefinitionAndProvenance()
        {
            // Given a valid theme manifest (its serialized definition, standing in for what
            // ThemeManifestSerializer.Serialize would produce — T181's IThemeStore deals in the raw
            // jsonb text, never GenWave.Host.Theming.ThemeManifest itself; see IThemeStore's remarks),
            IThemeStore store = new FakeThemeStore();
            const string slug = "midnight-drive";
            const string definition = """{"slug":"midnight-drive","name":"Midnight Drive"}""";
            const string importedFrom = "midnight-drive-catalog-entry";

            // When it is stored,
            await store.UpsertAsync(slug, definition, importedFrom, CancellationToken.None);
            var theme = await store.GetBySlugAsync(slug, CancellationToken.None);

            // Then a station.theme row holds its definition jsonb plus imported_from/imported_at
            // (AC1) — one assertion bundling the whole composite claim, mirroring this codebase's
            // tuple-equality idiom for a single Specification checking several related fields at once.
            Assert.Equal(
                (definition, importedFrom, ImportedAtStamped: true),
                (theme?.Definition, theme?.ImportedFrom, ImportedAtStamped: theme?.ImportedAt is not null));
        }
    }

    public sealed class ScenarioTheCatalogLoadsShippedAndOwnerThemes
    {
        [Fact]
        public async Task AllThreeResolveThroughTheOneLoadPath()
        {
            // Given the embedded set (whatever it is today — T191 later trims six to two, so this
            // reads the count rather than hardcoding it) and one stored owner theme,
            var shippedCount = ThemeCatalog.LoadShipped().All.Count;
            var store = new FakeThemeStore();
            await store.UpsertAsync(
                "midnight-drive",
                ThemeFixtures.ValidManifestJson("midnight-drive"),
                "midnight-drive-catalog-entry",
                CancellationToken.None);

            // When ThemeCatalog loads through the production shipped∪owner path (CreateForStation,
            // then the ReloadOwnerThemesAsync fold-in a boot warm-up or an import would trigger),
            var catalog = ThemeCatalog.CreateForStation(store, NullLogger<ThemeCatalog>.Instance);
            await catalog.ReloadOwnerThemesAsync(CancellationToken.None);

            // Then the shipped default and the owner theme both resolve, and nothing else snuck in
            // (AC2) — one assertion bundling the whole composite claim, mirroring this codebase's
            // tuple-equality idiom for a single Specification checking several related facts at once.
            var shippedResolves = catalog.TryGetBySlug(ThemeCatalog.ShippedDefaultSlug, out _);
            var ownerResolves = catalog.TryGetBySlug("midnight-drive", out _);
            Assert.Equal(
                (ShippedResolves: true, OwnerResolves: true, TotalCount: shippedCount + 1),
                (ShippedResolves: shippedResolves, OwnerResolves: ownerResolves, TotalCount: catalog.All.Count));
        }
    }

    public sealed class ScenarioTheChoiceWidensToImportedSlugs
    {
        [Fact]
        public async Task TheOwnerThemesSlugIsASelectableChoice()
        {
            // Given a stored owner theme, folded into the runtime catalog through the exact
            // production shipped∪owner load path ScenarioTheCatalogLoadsShippedAndOwnerThemes
            // above already proves (FakeThemeStore, no live DB — this project carries none),
            var store = new FakeThemeStore();
            await store.UpsertAsync(
                "midnight-drive",
                ThemeFixtures.ValidManifestJson("midnight-drive"),
                "midnight-drive-catalog-entry",
                CancellationToken.None);
            var catalog = ThemeCatalog.CreateForStation(store, NullLogger<ThemeCatalog>.Instance);
            await catalog.ReloadOwnerThemesAsync(CancellationToken.None);

            // When the Station:Theme setting choices are read — StationSettingsAllowlist.ThemeChoices
            // is the exact seam SettingsController's GET/PUT response and SettingValidator's own
            // guard both call (PLAN T183), sourced from ThemeCatalog.All, not LoadShipped,
            var choices = StationSettingsAllowlist.ThemeChoices(catalog);

            // Then the owner theme's slug is a selectable choice (AC3) — the closed choice widens
            //      from shipped-only to include imported slugs.
            Assert.Contains(choices, choice => choice.Value == "midnight-drive");
        }
    }

    public sealed class ScenarioTheOfflineFloorHolds
    {
        [Fact]
        public async Task TheShippedDefaultStillResolvesWhenTheStoreIsUnreachable()
        {
            // Given a station.theme store that always throws — the "DB removed" case,
            var catalog = ThemeCatalog.CreateForStation(new ThrowingThemeStore(), NullLogger<ThemeCatalog>.Instance);

            // When the catalog attempts to fold owner themes in,
            await catalog.ReloadOwnerThemesAsync(CancellationToken.None);

            // Then the shipped default still resolves (AC4, F102.7) — the failure degraded silently
            //      to the shipped-only set rather than escaping ReloadOwnerThemesAsync and taking the
            //      whole catalog down with it.
            Assert.True(catalog.TryGetBySlug(ThemeCatalog.ShippedDefaultSlug, out _));
        }

        [Fact]
        public async Task TheShippedDefaultStillResolvesWhenTheStoreIsEmpty()
        {
            // Given a station.theme store with no owner themes stored yet — the never-imported case,
            var catalog = ThemeCatalog.CreateForStation(new FakeThemeStore(), NullLogger<ThemeCatalog>.Instance);

            // When the catalog attempts to fold owner themes in,
            await catalog.ReloadOwnerThemesAsync(CancellationToken.None);

            // Then the shipped default still resolves (AC4, F102.7) — an empty owner set is not a
            //      failure, but it must never leave the catalog without its offline fallback either.
            Assert.True(catalog.TryGetBySlug(ThemeCatalog.ShippedDefaultSlug, out _));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAShippedSlugCannotBeShadowed
    {
        [Fact]
        public async Task ItIsIgnoredAndTheShippedManifestStillResolves()
        {
            // Given the real shipped default's own name, and an owner row claiming that same slug
            // with a different one,
            var shippedName = ThemeCatalog.LoadShipped().All.Single(theme => theme.Slug == ThemeCatalog.ShippedDefaultSlug).Name;
            var store = new FakeThemeStore();
            await store.UpsertAsync(
                ThemeCatalog.ShippedDefaultSlug,
                ThemeFixtures.ValidManifestJson(ThemeCatalog.ShippedDefaultSlug, name: "Imposter"),
                "midnight-drive-catalog-entry",
                CancellationToken.None);

            // When ThemeCatalog loads through the production shipped∪owner path,
            var catalog = ThemeCatalog.CreateForStation(store, NullLogger<ThemeCatalog>.Instance);
            await catalog.ReloadOwnerThemesAsync(CancellationToken.None);

            // Then the slug still resolves to the shipped manifest, never the owner impostor (AC5,
            //      F103.8) — one assertion bundling both halves; the no-duplicate-slug invariant Load
            //      enforces holds because the collision was skipped, not thrown (an import-time
            //      refusal is T184's own concern, not this catalog's).
            var resolves = catalog.TryGetBySlug(ThemeCatalog.ShippedDefaultSlug, out var resolved);
            Assert.Equal(
                (Resolves: true, Name: shippedName),
                (Resolves: resolves, Name: resolved?.Name));
        }
    }
}

/// <summary>
/// <see cref="IThemeStore"/> double whose <see cref="GetAllAsync"/> always throws — simulates the
/// "station database unreachable" case (SPEC F102.7) so
/// <see cref="FeatureOwnerThemeStorageAndResolution.ScenarioTheOfflineFloorHolds"/> can prove
/// <see cref="ThemeCatalog.ReloadOwnerThemesAsync"/> degrades to the shipped-only set instead of
/// propagating — mirrors <c>Story080_SafeSeedOnBoot.cs</c>'s own <c>ThrowingMarkerStore</c> idiom for
/// the identical "simulated DB failure, boot must not depend on it" shape.
/// </summary>
file sealed class ThrowingThemeStore : IThemeStore
{
    public Task UpsertAsync(string slug, string definition, string? importedFrom, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<bool> SaveAsOwnAsync(string slug, string definition, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<IReadOnlyList<OwnerTheme>> GetAllAsync(CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");

    public Task<OwnerTheme?> GetBySlugAsync(string slug, CancellationToken ct) =>
        throw new InvalidOperationException("simulated DB failure");
}
