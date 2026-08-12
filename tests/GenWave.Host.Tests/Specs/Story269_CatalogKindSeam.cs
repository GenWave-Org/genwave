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
// T177 — ScenarioTheGoldenThemeFixtureRoundTrips is now live: Fixtures/golden.theme.json parses
// through ThemeManifestParser and re-serializes byte-identically through ThemeManifestSerializer
// (STORY-269 AC5).
// One assertion per Fact where the scenario allows it; happy path first and exhaustive; the sad
// path (unknown kind vs unknown audience) is its own block.

using System.Text;
using GenWave.Host.Catalog;
using GenWave.Host.Theming;

namespace GenWave.Host.Tests.Specs;

// ── Fixture file access ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Locates and reads <c>Fixtures/golden.theme.json</c> from its SOURCE location (not a build output
/// copy) — mirrors <c>Story231_GoldenCardParity.cs</c>'s own <c>GoldenFixtureFile</c> idiom (itself
/// a <c>file</c>-scoped type, so this file needs its own copy rather than sharing that one): walk up
/// from <see cref="AppContext.BaseDirectory"/> until the repo root (<c>GenWave.sln</c>) is found,
/// then address the file by its fixed source-tree path.
/// </summary>
file static class GoldenThemeFixtureFile
{
    /// <summary>The exact bytes committed at <c>Fixtures/golden.theme.json</c> — read fresh on every
    /// call, matching <c>GoldenFixtureFile</c>'s own no-shared-mutable-state idiom.</summary>
    public static string ReadText() => File.ReadAllText(LocatePath());

    static string LocatePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GenWave.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("repo root (GenWave.sln) not found");

        return Path.Combine(dir.FullName, "tests", "GenWave.Host.Tests", "Fixtures", "golden.theme.json");
    }
}

public static class FeatureCatalogKindDiscriminator
{
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

    public sealed class ScenarioAPerKindFolderLayoutIsAdmitted
    {
        [Fact]
        public void BothLayoutsCoexistInOneIndex()
        {
            // Given an index mid-migration to the per-kind shelf layout (genwave-catalog#33) — a
            // persona and a show already moved under entries/personas/ and entries/shows/, while a
            // theme still sits at the flat entries/<slug>/,
            // When the index is parsed,
            // Then all three entries are admitted with their recorded paths intact — the kind
            //      folder is an accepted alternative, not a new mandatory shape.
            var index = """
                { "generatedAt": "2026-08-12", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/personas/valid-dj/valid-dj.persona.json", "sha256": "SHA" },
                    "meta": { "path": "entries/personas/valid-dj/valid-dj.meta.json", "sha256": "SHA" } },
                  { "slug": "late-shift", "kind": "show", "audience": "everyone",
                    "manifest": { "path": "entries/shows/late-shift/late-shift.show.json", "sha256": "SHA" },
                    "meta": { "path": "entries/shows/late-shift/late-shift.meta.json", "sha256": "SHA" } },
                  { "slug": "gilded-static", "kind": "theme", "audience": "everyone",
                    "manifest": { "path": "entries/gilded-static/gilded-static.theme.json", "sha256": "SHA" },
                    "meta": { "path": "entries/gilded-static/gilded-static.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);
            var success = TryValidate(index, out var entries, out _);
            Assert.True(success);

            Assert.Equal(
                [
                    "entries/personas/valid-dj/valid-dj.persona.json",
                    "entries/shows/late-shift/late-shift.show.json",
                    "entries/gilded-static/gilded-static.theme.json",
                ],
                entries!.Select(e => e.Manifest.Path));
        }
    }

    public sealed class ScenarioTheGoldenThemeFixtureRoundTrips
    {
        [Fact]
        public void ItIsByteIdenticalThroughTheManifestParser()
        {
            // Given the committed golden.theme.json exported from a real theme,
            // When it is parsed as a ThemeManifest (through the real ThemeCatalog.Load path, the
            // same one every shipped theme goes through) and re-serialized,
            var original = GoldenThemeFixtureFile.ReadText();
            var catalog = ThemeCatalog.Load([new ThemeManifestSource("golden.theme.json", original)]);
            var manifest = Assert.Single(catalog.All);

            // Then it is byte-identical (AC5) — the concrete format contract, pinned in both repos.
            Assert.Equal(original, ThemeManifestSerializer.Serialize(manifest));
        }
    }

    // ── SAD PATH ────────────────────────────────────────────────────────────

    public sealed class ScenarioAnUnknownKindIsSkippedNotFatal
    {
        [Fact]
        public void TheRestOfTheIndexStillLoads()
        {
            // Given an index entry whose kind the app does not recognise (a future icon/avatar —
            // NOT "font", which F104.1/T193 widened this app to recognise; S3 review finding: this
            // fixture used to read kind:"font" here, which now parses as a KNOWN kind and would
            // pass this Fact for the wrong reason — zero declared assets, not forward-compat skip —
            // leaving STORY-269 AC6 with no live coverage) alongside an ordinary, valid persona
            // entry,
            // When the index is parsed,
            // Then that entry is skipped and the rest of the index still loads (AC6) — forward-compat.
            var index = """
                { "generatedAt": "2026-08-04", "entries": [
                  { "slug": "future-icon", "kind": "icon", "audience": "everyone",
                    "manifest": { "path": "entries/future-icon/future-icon.icon.json", "sha256": "SHA" },
                    "meta": { "path": "entries/future-icon/future-icon.meta.json", "sha256": "SHA" } },
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

    public sealed class ScenarioAManifestUnderTheWrongKindFolderRejectsTheIndex
    {
        [Fact]
        public void TheWholeIndexIsRejected()
        {
            // Given a persona entry whose manifest sits under entries/shows/ (genwave-catalog#33) —
            // a kind folder that lies about what the file is, not an alternative layout,
            // When the index is parsed,
            // Then the whole index is rejected — the per-kind path pattern pins the folder, when
            //      present, to the entry's own kind.
            var index = """
                { "generatedAt": "2026-08-12", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/shows/valid-dj/valid-dj.persona.json", "sha256": "SHA" },
                    "meta": { "path": "entries/shows/valid-dj/valid-dj.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);

            var success = TryValidate(index, out _, out _);

            Assert.False(success);
        }
    }

    public sealed class ScenarioANestedPathStillBelongsToItsOwnSlug
    {
        [Fact]
        public void ASlugSquattingUnderAnotherEntrysNestedDirectoryIsRejected()
        {
            // Given a nested-layout persona entry whose manifest sits under ANOTHER entry's
            // directory (the slug segment is the second-to-last in BOTH layouts — this pins the
            // ownership check survived the genwave-catalog#33 widening; a naive first-segment read
            // would compare against "personas" instead),
            // When the index is parsed,
            // Then the whole index is rejected.
            var index = """
                { "generatedAt": "2026-08-12", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/personas/other-dj/valid-dj.persona.json", "sha256": "SHA" },
                    "meta": { "path": "entries/personas/valid-dj/valid-dj.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);

            var success = TryValidate(index, out _, out _);

            Assert.False(success);
        }
    }

    public sealed class ScenarioAMetaStrayingFromItsManifestDirectoryRejectsTheIndex
    {
        [Fact]
        public void TheWholeIndexIsRejected()
        {
            // Given an entry whose manifest sits at the flat entries/<slug>/ while its meta sits
            // under the nested entries/personas/<slug>/ — each path valid in isolation now that
            // both layouts are admitted (genwave-catalog#33),
            // When the index is parsed,
            // Then the whole index is rejected — an entry's files all sit in ONE directory (the
            //      one-directory invariant; it is also what keeps an entry's bare filenames unique).
            var index = """
                { "generatedAt": "2026-08-12", "entries": [
                  { "slug": "valid-dj", "kind": "persona", "audience": "everyone",
                    "manifest": { "path": "entries/valid-dj/valid-dj.persona.json", "sha256": "SHA" },
                    "meta": { "path": "entries/personas/valid-dj/valid-dj.meta.json", "sha256": "SHA" } } ] }
                """.Replace("SHA", Sha256Placeholder);

            var success = TryValidate(index, out _, out _);

            Assert.False(success);
        }
    }
}
