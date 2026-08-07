// STORY-288 — Uninstall with the guard, proven against real Postgres (SPEC F104.14 · PLAN T208)
//
// BDD specification — xUnit, Postgres-backed (Category=Integration) via DatabaseCollection.
// FontPackRepository.DeleteAsync's own guard — "the delete IS the guard", a single atomic SQL
// statement that refuses to remove a pack while any station.theme row's definition still mentions one
// of its faces — is proven here against the REAL station.theme ∪ station.font_pack(+_face) tables, the
// cross-table half GenWave.Host.Tests' own FakeFontPackStore double (Story288_FontPackUninstall.cs,
// GenWave.Host.Tests) cannot honestly repeat (that fake carries no knowledge of station.theme by
// design — see its own remarks). Story282_FontPackRepository.cs's own precedent: IFontPackStore's real
// SQL is proven here; FontPackController's response-mapping contract is proven against the fake.
//
// Theme rows are seeded directly through ThemeRepository (mirrors Story278_ThemeCatalogIsolation.cs's
// own "write straight to the store, bypass the write-route gates" precedent) — this file's own concern
// is the SQL join, not theme-manifest validation.
//
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad path
// (referenced, unknown slug) is its own block.

using Dapper;
using GenWave.Core.Domain;
using GenWave.MediaLibrary.Station;
using Npgsql;

namespace GenWave.MediaLibrary.Tests.Specs;

public static class FeatureFontPackUninstall
{
    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static FontPackRepository FontPacks(DatabaseFixture db) => new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));
    static ThemeRepository Themes(DatabaseFixture db) => new(new Lazy<NpgsqlDataSource>(() => db.StationDataSource));

    const string Definition = """{"slug":"space-grotesk","family":"Space Grotesk","licence":"OFL-1.1"}""";

    static Task InstallPackAsync(DatabaseFixture db, string slug, string file, byte[] bytes) =>
        FontPacks(db).UpsertAsync(
            slug, "Space Grotesk", Definition, $"{slug}-catalog-entry",
            [new FontPackFaceInput(file, bytes, Sha256Hex(bytes))],
            CancellationToken.None);

    /// <summary>A theme manifest whose display font references <paramref name="file"/> at the exact
    /// <c>/fonts/&lt;file&gt;</c> shape <c>ThemeManifestParser.FontSrcPattern</c> pins (GenWave.Host,
    /// not referenced from this project) — written straight through <see cref="ThemeRepository"/>, so
    /// this file needs no dependency on GenWave.Host to prove the guard's own SQL join. Every brace is
    /// space-padded (mirrors <c>Story287_SaveAsOwn.cs</c>'s own fixture idiom) so no two land adjacent
    /// — a raw string literal using <c>$$"""..."""</c> interpolation would otherwise misparse a bare
    /// <c>}}</c>/<c>{{</c> run in the JSON itself as an interpolation delimiter.</summary>
    static string ThemeReferencing(string slug, string file) => $$"""
        {
          "slug": "{{slug}}",
          "name": "{{slug}}",
          "author": "Test",
          "fonts": {
            "display": { "family": "Fraunces", "assets": [ { "src": "/fonts/{{file}}", "weight": "400", "style": "normal" } ] },
            "sans": { "family": "Source Sans 3", "assets": [ { "src": "/fonts/source-sans-3-variable-latin.woff2", "weight": "400", "style": "normal" } ] }
          },
          "modes": {
            "light": { "bg": "#ffffff" },
            "dark": { "bg": "#000000" }
          }
        }
        """;

    static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    // ---------------------------------------------------------------------
    // HAPPY PATH — an unreferenced pack deletes transactionally
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnUnreferencedPackDeletes(DatabaseFixture db)
    {
        [Fact]
        public async Task TheGuardedDeleteRemovesThePackAndCascadesItsFaces()
        {
            await db.ResetFontPackAsync();
            await db.ResetThemeAsync();
            var repo = FontPacks(db);
            var bytes = "an unreferenced pack's own face payload"u8.ToArray();
            await InstallPackAsync(db, "unreferenced-pack", "unreferenced-face.woff2", bytes);

            // No station.theme row exists at all — nothing to reference the pack.

            var result = await repo.DeleteAsync("unreferenced-pack", CancellationToken.None);

            Assert.IsType<FontPackDeleteResult.Deleted>(result);
            Assert.Equal(
                (PackGone: true, FaceGone: true),
                (PackGone: !(await repo.GetAllAsync(CancellationToken.None)).Any(p => p.Slug == "unreferenced-pack"),
                 FaceGone: await repo.GetFaceByFileAsync("unreferenced-face.woff2", CancellationToken.None) is null));
        }

        [Fact]
        public async Task APackReferencedByAWhollyDifferentFilenameDeletesToo()
        {
            // Guards against a false-positive match — a theme referencing a DIFFERENT, merely
            // similarly-named face must never block this pack's own uninstall (the substring search's
            // own quoted "/fonts/<file>" shape is exact, not a fuzzy prefix/suffix match).
            //
            // Review finding N1: this pair is deliberately DISCRIMINATING against a naive bare-filename
            // strpos (one that drops the guard's own quoted "/fonts/" anchor entirely, searching for
            // fpf.file alone) — a theme referencing "extra-latin.woff2" literally CONTAINS the bare
            // string "latin.woff2" as its own trailing substring, so a naive bare-filename search finds
            // it and wrongly refuses; the real, anchored predicate ("/fonts/latin.woff2", quoted) is NOT
            // a substring of "/fonts/extra-latin.woff2" (the char immediately after "/fonts/" is "e",
            // not "l"), so it correctly reports no reference. A prior version of this fixture ("shared-
            // latin.woff2" vs "shared-latin-extended.woff2") passed identically whether the guard's own
            // quotes were present or stripped — it never actually exercised what the quotes buy, only
            // what the "/fonts/" prefix alone already buys.
            await db.ResetFontPackAsync();
            await db.ResetThemeAsync();
            var repo = FontPacks(db);
            var bytes = "a face named identically to another theme's own suffix"u8.ToArray();
            await InstallPackAsync(db, "near-miss-pack", "latin.woff2", bytes);
            await Themes(db).UpsertAsync(
                "near-miss-theme", ThemeReferencing("near-miss-theme", "extra-latin.woff2"),
                "file", CancellationToken.None);

            var result = await repo.DeleteAsync("near-miss-pack", CancellationToken.None);

            Assert.IsType<FontPackDeleteResult.Deleted>(result);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — a referenced pack refuses, naming the theme, and removes nothing
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAReferencedPackRefuses(DatabaseFixture db)
    {
        [Fact]
        public async Task TheDeleteRefusesNamingTheReferencingThemeAndRemovesNothing()
        {
            await db.ResetFontPackAsync();
            await db.ResetThemeAsync();
            var repo = FontPacks(db);
            var bytes = "a referenced pack's own face payload"u8.ToArray();
            await InstallPackAsync(db, "referenced-pack", "referenced-face.woff2", bytes);
            await Themes(db).UpsertAsync(
                "midnight-drive", ThemeReferencing("midnight-drive", "referenced-face.woff2"),
                "file", CancellationToken.None);

            var result = await repo.DeleteAsync("referenced-pack", CancellationToken.None);

            var referenced = Assert.IsType<FontPackDeleteResult.Referenced>(result);
            Assert.Equal(
                (NamesTheTheme: true, PackStillListed: true, FaceStillServes: true),
                (NamesTheTheme: referenced.ThemeSlugs.Contains("midnight-drive"),
                 PackStillListed: (await repo.GetAllAsync(CancellationToken.None)).Any(p => p.Slug == "referenced-pack"),
                 FaceStillServes: await repo.GetFaceByFileAsync("referenced-face.woff2", CancellationToken.None) is not null));
        }

        [Fact]
        public async Task MultipleReferencingThemesAreAllNamedOrdinalSorted()
        {
            await db.ResetFontPackAsync();
            await db.ResetThemeAsync();
            var repo = FontPacks(db);
            var bytes = "a face two themes both wear"u8.ToArray();
            await InstallPackAsync(db, "widely-worn-pack", "widely-worn-face.woff2", bytes);
            await Themes(db).UpsertAsync(
                "sunday-static", ThemeReferencing("sunday-static", "widely-worn-face.woff2"), "file", CancellationToken.None);
            await Themes(db).UpsertAsync(
                "amber-hour", ThemeReferencing("amber-hour", "widely-worn-face.woff2"), "file", CancellationToken.None);

            var result = await repo.DeleteAsync("widely-worn-pack", CancellationToken.None);

            var referenced = Assert.IsType<FontPackDeleteResult.Referenced>(result);
            Assert.Equal(new[] { "amber-hour", "sunday-static" }, referenced.ThemeSlugs);
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — an unknown slug is a clean miss
    // ---------------------------------------------------------------------

    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public sealed class ScenarioAnUnknownSlug(DatabaseFixture db)
    {
        [Fact]
        public async Task TheDeleteReturnsNotFound()
        {
            await db.ResetFontPackAsync();
            await db.ResetThemeAsync();
            var repo = FontPacks(db);

            var result = await repo.DeleteAsync("no-such-pack", CancellationToken.None);

            Assert.IsType<FontPackDeleteResult.NotFound>(result);
        }
    }
}
