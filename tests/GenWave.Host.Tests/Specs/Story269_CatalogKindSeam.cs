// STORY-269 — The catalog admits a second kind (SPEC F103.1, F103.2, F103.3)
//
// BDD specification — xUnit. The seam the whole theme-catalog epic hangs off: the catalog entry
// model gains an explicit `kind` discriminator and a generalized {manifest, meta} shape, so a
// theme kind (and later font/icon/avatar) plugs in additively while personas keep working
// unchanged. This is an INVISIBLE refactor — personas default to kind:"persona", the shelf is
// unchanged, and no theme exists yet.
//
// Also fixes the format contract: golden.theme.json is a real ThemeManifest committed to both the
// app tests and (T178+) the catalog repo, pinned byte-stable — the concrete .theme.json shape both
// repos lock onto.
//
// T176 (this file's live facts) drives CatalogIndexValidator.TryValidate directly — the same
// "test the seam directly, no endpoint exists yet" idiom Story234's own T99/T100 sections use —
// since the kind discriminator lives entirely in the index-parsing seam, not behind an HTTP route.
// PENDING T177 — ScenarioTheGoldenThemeFixtureRoundTrips flips live once the golden fixture lands.
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad
// path (unknown kind vs unknown audience) is its own block.

using System.Text;
using GenWave.Host.Catalog;

namespace GenWave.Host.Tests.Specs;

public static class FeatureCatalogKindDiscriminator
{
    const string PendingGolden = "pending T177 — golden.theme.json format contract";

    static readonly Uri Directory = new("https://catalog.test/repo/");

    // CatalogIndexValidator only checks a declared sha256's SHAPE (64 lowercase hex chars) — a real
    // hash-vs-fetched-bytes check is CatalogProxyService's own later job, once content is actually
    // fetched (never reached by these facts, which drive the validator directly). One fixed,
    // well-formed value is enough everywhere below — mirrors Story234's own T100 fixtures (e.g.
    // ScenarioHostileIndexRejected's "aaaa...a" literals).
    const string Sha256Placeholder = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    static bool TryValidate(string indexJson, out IReadOnlyList<CatalogEntrySummary>? entries, out string? reason) =>
        CatalogIndexValidator.TryValidate(Encoding.UTF8.GetBytes(indexJson), Directory, out entries, out reason);

    // ── HAPPY PATH ──────────────────────────────────────────────────────────

    public sealed class ScenarioAnIndexEntryCarriesAKind
    {
        [Fact]
        public void EachEntryExposesItsKind()
        {
            // Given a catalog index with a persona entry and a theme entry,
            // When the index is parsed,
            // Then each entry exposes its kind ("persona" | "theme") (AC1).
            var index = """
                { "generatedAt": "2026-08-04", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "SHA" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "SHA" } },
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "SHA" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);
            var success = TryValidate(index, out var entries, out _);
            Assert.True(success);

            Assert.Equal([CatalogEntryKind.Persona, CatalogEntryKind.Theme], entries!.Select(e => e.Kind));
        }
    }

    public sealed class ScenarioAPersonaEntryWithoutAnExplicitKindDefaultsToPersona
    {
        [Fact]
        public void ItsKindResolvesToPersona()
        {
            // Given a legacy index entry authored before the kind field — the live genwave-catalog
            // origin's own current shape today: no `kind`, the legacy `card` file-ref name,
            // When it is parsed,
            // Then its kind resolves to "persona" (AC2) — back-compat for the shipped shelf.
            var index = """
                { "generatedAt": "2026-08-04", "entries": [
                  { "slug": "valid-dj", "audience": "everyone",
                    "card": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "SHA" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);
            var success = TryValidate(index, out var entries, out _);
            Assert.True(success);

            Assert.Equal(CatalogEntryKind.Persona, Assert.Single(entries!).Kind);
        }
    }

    public sealed class ScenarioTheTwoFileModelIsKindNeutral
    {
        [Fact]
        public void FileReferencesAreExposedAsManifestAndMeta()
        {
            // Given any entry,
            // When its file references are read,
            // Then they are exposed as {manifest, meta} (the persona `card` renamed) (AC3).
            var index = """
                { "generatedAt": "2026-08-04", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "SHA" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);
            var success = TryValidate(index, out var entries, out _);
            Assert.True(success);
            var entry = Assert.Single(entries!);

            Assert.Equal(
                ("entries/valid-dj/valid-dj.persona.json", "entries/valid-dj/valid-dj.meta.json"),
                (entry.Manifest.Path, entry.Meta.Path));
        }
    }

    public sealed class ScenarioAThemeManifestReferenceUsesTheThemePattern
    {
        [Fact]
        public void TheManifestPathMatchesTheThemeFilePattern()
        {
            // Given a theme entry,
            // When its manifest path is validated,
            // Then it matches entries/<slug>/<slug>.theme.json (AC4), while a persona's stays
            //      <slug>.persona.json (already exercised by the two scenarios above).
            var index = """
                { "generatedAt": "2026-08-04", "entries": [
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "SHA" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);
            var success = TryValidate(index, out var entries, out _);
            Assert.True(success);

            Assert.Equal("entries/gilded-static/gilded-static.theme.json", Assert.Single(entries!).Manifest.Path);
        }
    }

    public sealed class ScenarioTheGoldenThemeFixtureRoundTrips
    {
        [Fact(Skip = PendingGolden)]
        public void ItIsByteIdenticalThroughTheManifestParser()
        {
            // Given the committed golden.theme.json exported from a real theme,
            // When it is parsed as a ThemeManifest and re-serialized,
            // Then it is byte-identical (AC5) — the concrete format contract, pinned in both repos.
            Assert.Fail(PendingGolden);
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAnUnknownKindIsSkippedNotFatal
    {
        [Fact]
        public void TheRestOfTheIndexStillLoads()
        {
            // Given an index entry whose kind the app does not recognise (a future font/icon/avatar)
            // alongside an ordinary, valid persona entry,
            // When the index is parsed,
            // Then that entry is skipped and the rest of the index still loads (AC6) — forward-compat.
            var index = """
                { "generatedAt": "2026-08-04", "entries": [
                  { "slug": "future-font", "kind": "font", "audience": "everyone",
                    "manifest": { "path": "entries/future-font/future-font.font.json", "sha256": "SHA" },
                    "meta": { "path": "entries/future-font/future-font.meta.json", "sha256": "SHA" } },
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "SHA" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);
            var success = TryValidate(index, out var entries, out _);
            Assert.True(success);

            Assert.Equal("valid-dj", Assert.Single(entries!).Slug);
        }
    }

    public sealed class ScenarioAnUnknownAudienceStillRejectsTheIndex
    {
        [Fact]
        public void TheWholeIndexIsRejected()
        {
            // Given an entry with an unrecognised audience,
            // When the index is parsed,
            // Then the whole index is rejected (AC7) — audience is content-safety, unlike kind
            //      which is forward-compat. The two must not be conflated.
            var index = """
                { "generatedAt": "2026-08-04", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "not-a-real-audience",
                    "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "SHA" },
                    "meta": { "path": "entries/valid-dj/valid-dj.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);

            var success = TryValidate(index, out _, out _);

            Assert.False(success);
        }
    }
}
